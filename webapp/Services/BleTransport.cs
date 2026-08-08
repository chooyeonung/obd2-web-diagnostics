using Microsoft.JSInterop;

namespace ObdWebApp.Services;

/// <summary>
/// Web Bluetooth(BLE)로 vLinker MC+ 등 ELM327 호환 동글과 통신하는 트랜스포트.
/// 실제 BLE 처리는 wwwroot/js/obd-ble.js 모듈이 담당하고, 이 클래스는 JS interop 래퍼다.
/// 주의: Web Bluetooth는 보안 컨텍스트(HTTPS 또는 localhost) + 사용자 제스처(버튼 클릭)에서만 동작한다.
/// </summary>
public sealed class BleTransport : IObdTransport
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BleTransport>? _selfRef;

    public string Name => DeviceName is null ? "vLinker / ELM327 (BLE)" : $"BLE: {DeviceName}";
    public string? DeviceName { get; private set; }
    public bool IsConnected { get; private set; }

    public event Action<string>? DataReceived;
    public event Action? Disconnected;

    public BleTransport(IJSRuntime js) => _js = js;

    public async Task ConnectAsync()
    {
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/obd-ble.js");

        var supported = await _module.InvokeAsync<bool>("isSupported");
        if (!supported)
            throw new NotSupportedException("이 브라우저는 Web Bluetooth를 지원하지 않습니다. Chrome/Edge를 사용하세요.");

        _selfRef ??= DotNetObjectReference.Create(this);
        DeviceName = await _module.InvokeAsync<string>("connect", _selfRef);
        IsConnected = true;
    }

    public async Task DisconnectAsync()
    {
        if (_module is not null)
            await _module.InvokeVoidAsync("disconnect");
        IsConnected = false;
    }

    public async Task SendAsync(string data)
    {
        if (_module is null || !IsConnected)
            throw new InvalidOperationException("BLE가 연결되어 있지 않습니다.");
        await _module.InvokeVoidAsync("send", data);
    }

    [JSInvokable]
    public void OnBleData(string text) => DataReceived?.Invoke(text);

    [JSInvokable]
    public void OnBleDisconnected()
    {
        IsConnected = false;
        Disconnected?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (IsConnected) await DisconnectAsync();
            if (_module is not null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // 페이지 이탈 시 JS 런타임이 먼저 사라진 경우 — 무시
        }
        _selfRef?.Dispose();
    }
}
