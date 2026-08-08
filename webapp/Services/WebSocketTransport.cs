using System.Net.WebSockets;
using System.Text;

namespace ObdWebApp.Services;

/// <summary>
/// 자작 ESP32 보드(WiFi 게이트웨이 펌웨어)와 WebSocket으로 통신하는 트랜스포트.
/// 펌웨어가 ELM327을 에뮬레이션하므로 상위 계층(Elm327Client 이후)은 그대로 재사용된다.
/// 주의: HTTPS로 서빙된 페이지에서는 ws:// 연결이 차단된다(혼합 콘텐츠).
/// ESP32가 직접 서빙하는 페이지(http://192.168.4.1) 또는 localhost 개발 환경에서 사용할 것.
/// </summary>
public sealed class WebSocketTransport : IObdTransport
{
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;

    /// <summary>ESP32 SoftAP 기본 주소. UI에서 변경 가능.</summary>
    public string Url { get; set; } = "ws://192.168.4.1/ws";

    public string Name => $"자작 보드 (WebSocket: {Url})";
    public bool IsConnected { get; private set; }

    public event Action<string>? DataReceived;
    public event Action? Disconnected;

    public async Task ConnectAsync()
    {
        _socket = new ClientWebSocket();
        _cts = new CancellationTokenSource();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        await _socket.ConnectAsync(new Uri(Url), timeout.Token);

        IsConnected = true;
        _ = ReceiveLoopAsync(_cts.Token);
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;
        _cts?.Cancel();
        if (_socket is { State: WebSocketState.Open })
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch { /* 종료 중 오류는 무시 */ }
        }
        _socket?.Dispose();
        _socket = null;
    }

    public async Task SendAsync(string data)
    {
        if (_socket is not { State: WebSocketState.Open })
            throw new InvalidOperationException("WebSocket이 연결되어 있지 않습니다.");

        var bytes = Encoding.UTF8.GetBytes(data);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested && _socket is { State: WebSocketState.Open })
            {
                var result = await _socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (result.Count > 0)
                    DataReceived?.Invoke(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
        }
        catch (OperationCanceledException) { /* 정상 종료 */ }
        catch (WebSocketException) { /* 원격 끊김 */ }
        finally
        {
            if (IsConnected)
            {
                IsConnected = false;
                Disconnected?.Invoke();
            }
        }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
