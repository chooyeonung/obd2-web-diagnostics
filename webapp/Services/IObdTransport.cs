namespace ObdWebApp.Services;

/// <summary>
/// OBD 어댑터와의 저수준 통신 추상화.
/// 구현체: SimulatorTransport(가짜 ECU), BleTransport(Web Bluetooth → vLinker/ELM327).
/// 향후 자작 ESP32 보드용 WebSocketTransport를 추가해도 상위 계층은 그대로 재사용된다.
/// </summary>
public interface IObdTransport : IAsyncDisposable
{
    string Name { get; }
    bool IsConnected { get; }

    /// <summary>어댑터가 보낸 원시 텍스트 조각(청크)이 도착할 때마다 발생.</summary>
    event Action<string>? DataReceived;

    event Action? Disconnected;

    Task ConnectAsync();
    Task DisconnectAsync();

    /// <summary>원시 문자열 전송(개행 포함 여부는 호출자가 관리).</summary>
    Task SendAsync(string data);
}
