using System.Text;

namespace ObdWebApp.Services;

/// <summary>
/// ELM327 명령/응답 처리기. 트랜스포트가 무엇이든(시뮬레이터, BLE, 향후 WebSocket)
/// "명령 전송 → '>' 프롬프트까지 응답 수집" 이라는 ELM327의 대화 규칙을 동일하게 적용한다.
/// </summary>
public sealed class Elm327Client : IAsyncDisposable
{
    private readonly IObdTransport _transport;
    private readonly StringBuilder _buffer = new();
    private readonly SemaphoreSlim _gate = new(1, 1); // 동시에 한 명령만
    private TaskCompletionSource<string>? _pending;

    /// <summary>(명령, 응답) 로그 이벤트 — UI 통신 로그용.</summary>
    public event Action<string, string>? CommandLogged;

    public IObdTransport Transport => _transport;

    public Elm327Client(IObdTransport transport)
    {
        _transport = transport;
        _transport.DataReceived += OnData;
    }

    private void OnData(string chunk)
    {
        lock (_buffer)
        {
            _buffer.Append(chunk);
            if (chunk.Contains('>'))
            {
                var full = _buffer.ToString();
                _buffer.Clear();
                var response = full.Replace(">", "").Trim();
                _pending?.TrySetResult(response);
            }
        }
    }

    /// <summary>
    /// 명령을 보내고 '>' 프롬프트까지의 전체 응답 텍스트를 돌려준다.
    /// </summary>
    public async Task<string> SendCommandAsync(string command, int timeoutMs = 5000)
    {
        await _gate.WaitAsync();
        try
        {
            lock (_buffer) _buffer.Clear();
            _pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            await _transport.SendAsync(command + "\r");

            var completed = await Task.WhenAny(_pending.Task, Task.Delay(timeoutMs));
            if (completed != _pending.Task)
                throw new TimeoutException($"응답 시간 초과: {command}");

            var response = await _pending.Task;
            CommandLogged?.Invoke(command, response);
            return response;
        }
        finally
        {
            _pending = null;
            _gate.Release();
        }
    }

    /// <summary>
    /// 표준 초기화 시퀀스. 에코/공백/헤더를 꺼서 파싱을 단순화하고 프로토콜 자동 탐지를 설정한다.
    /// ATZ 직후 SEARCHING 등으로 응답이 느릴 수 있어 타임아웃을 길게 준다.
    /// </summary>
    public async Task InitializeAsync()
    {
        await SendCommandAsync("ATZ", 12000);  // 리셋
        await SendCommandAsync("ATE0");        // 에코 끔
        await SendCommandAsync("ATL0");        // 라인피드 끔
        await SendCommandAsync("ATS0");        // 공백 제거
        await SendCommandAsync("ATH0");        // 헤더 제거
        await SendCommandAsync("ATSP0");       // 프로토콜 자동
    }

    public async ValueTask DisposeAsync()
    {
        _transport.DataReceived -= OnData;
        await _transport.DisposeAsync();
    }
}
