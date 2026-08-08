using System.ComponentModel;
using System.Runtime.CompilerServices;
using ObdWebApp.Models;
using ObdWebApp.Services;

namespace ObdWebApp.ViewModels;

/// <summary>연결 대상 종류.</summary>
public enum TransportKind
{
    Simulator,   // 가상 ECU
    Ble,         // vLinker/ELM327 동글 (Web Bluetooth)
    WebSocket,   // 자작 ESP32 보드 (WiFi 게이트웨이)
}

/// <summary>
/// 대시보드 화면의 ViewModel. 화면 상태와 동작(연결/폴링/DTC)을 모두 소유하고,
/// 뷰(Home.razor)는 이 클래스에 바인딩만 한다. WPF의 MVVM과 동일한 책임 분리.
/// </summary>
public sealed class DashboardViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ObdService _obd;
    private readonly SimulatorTransport _simulator;
    private readonly BleTransport _ble;
    private readonly WebSocketTransport _ws;
    private CancellationTokenSource? _pollCts;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DashboardViewModel(ObdService obd, SimulatorTransport simulator, BleTransport ble, WebSocketTransport ws)
    {
        _obd = obd;
        _simulator = simulator;
        _ble = ble;
        _ws = ws;
        _obd.CommandLogged += OnCommandLogged;
        _obd.Disconnected += OnTransportDisconnected;
    }

    // ---------- 상수/정의 ----------

    public const int PollIntervalMs = 700;
    public static readonly byte[] DashboardPids = { 0x0C, 0x0D, 0x05, 0x04, 0x11, 0x10, 0x2F, 0x42 };

    // ---------- 바인딩 속성 ----------

    private TransportKind _kind = TransportKind.Simulator;
    public TransportKind Kind { get => _kind; set => SetField(ref _kind, value); }

    /// <summary>자작 보드 WebSocket 주소 (ESP32 SoftAP 기본값).</summary>
    private string _wsUrl = "ws://192.168.4.1/ws";
    public string WsUrl { get => _wsUrl; set => SetField(ref _wsUrl, value); }

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; private set => SetField(ref _isConnected, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }

    private bool _isPolling;
    public bool IsPolling { get => _isPolling; private set => SetField(ref _isPolling, value); }

    private string? _error;
    public string? Error { get => _error; private set => SetField(ref _error, value); }

    private string? _vin;
    public string? Vin { get => _vin; private set => SetField(ref _vin, value); }

    private VehicleSnapshot? _snapshot;
    public VehicleSnapshot? Snapshot { get => _snapshot; private set => SetField(ref _snapshot, value); }

    private List<DiagnosticTroubleCode>? _dtcs;
    public List<DiagnosticTroubleCode>? Dtcs { get => _dtcs; private set => SetField(ref _dtcs, value); }

    private bool _confirmClear;
    public bool ConfirmClear { get => _confirmClear; private set => SetField(ref _confirmClear, value); }

    public List<string> Log { get; } = new();
    public string TransportName => _obd.TransportName;
    public bool CanClearDtcs => IsConnected && !IsBusy && Dtcs is { Count: > 0 };

    // ---------- 커맨드 ----------

    public async Task ConnectAsync()
    {
        IsBusy = true; Error = null;
        try
        {
            IObdTransport transport = Kind switch
            {
                TransportKind.Ble => _ble,
                TransportKind.WebSocket => _ws,
                _ => _simulator,
            };
            if (Kind == TransportKind.WebSocket) _ws.Url = WsUrl;

            await _obd.ConnectAsync(transport);
            IsConnected = true;
            Vin = await _obd.ReadVinAsync();
            StartPolling();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally { IsBusy = false; }
    }

    public async Task DisconnectAsync()
    {
        StopPolling();
        try { await _obd.DisconnectAsync(); } catch { /* 연결 해제 실패는 무시 */ }
        IsConnected = false;
        Snapshot = null;
        Vin = null;
        Dtcs = null;
        ConfirmClear = false;
    }

    public async Task ReadDtcsAsync()
    {
        IsBusy = true; Error = null; ConfirmClear = false;
        try { Dtcs = await _obd.ReadDtcsAsync(); }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>DTC 소거는 되돌릴 수 없는 동작 — 2단계 클릭 확인을 거친다.</summary>
    public async Task ClearDtcsCommandAsync()
    {
        if (!ConfirmClear) { ConfirmClear = true; return; }
        ConfirmClear = false;

        IsBusy = true; Error = null;
        try
        {
            var ok = await _obd.ClearDtcsAsync();
            if (ok) Dtcs = await _obd.ReadDtcsAsync();
            else Error = "소거 명령이 거부되었습니다.";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }

    public void ClearLog()
    {
        Log.Clear();
        OnPropertyChanged(nameof(Log));
    }

    // ---------- 폴링 ----------

    private void StartPolling()
    {
        _pollCts = new CancellationTokenSource();
        IsPolling = true;
        _ = PollLoopAsync(_pollCts.Token);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts = null;
        IsPolling = false;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsConnected)
        {
            try
            {
                Snapshot = await _obd.ReadSnapshotAsync(DashboardPids);
            }
            catch (Exception ex)
            {
                Error = $"폴링 오류: {ex.Message}";
            }

            try { await Task.Delay(PollIntervalMs, ct); }
            catch (TaskCanceledException) { break; }
        }
        IsPolling = false;
    }

    // ---------- 이벤트 핸들러 ----------

    private void OnCommandLogged(string cmd, string resp)
    {
        Log.Add($"> {cmd}\n  {resp.Replace("\r", " ").Replace("\n", " ")}");
        if (Log.Count > 200) Log.RemoveRange(0, Log.Count - 200);
        OnPropertyChanged(nameof(Log));
    }

    private void OnTransportDisconnected()
    {
        StopPolling();
        IsConnected = false;
        Error = "연결이 끊어졌습니다.";
    }

    // ---------- 표시 포맷 ----------

    public static string FormatValue(byte pid, double v) => pid switch
    {
        0x0C => ((int)v).ToString("N0"),
        0x42 => v.ToString("F1"),
        0x10 => v.ToString("F1"),
        _ => ((int)Math.Round(v)).ToString(),
    };

    // ---------- INotifyPropertyChanged ----------

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged(string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public async ValueTask DisposeAsync()
    {
        StopPolling();
        _obd.CommandLogged -= OnCommandLogged;
        _obd.Disconnected -= OnTransportDisconnected;
        await Task.CompletedTask;
    }
}
