using System.Text;

namespace ObdWebApp.Services;

/// <summary>
/// ELM327 동글 + 가솔린 차량 ECU를 흉내내는 시뮬레이터.
/// 실차/동글 없이 웹앱 전체 흐름(초기화 → PID 폴링 → DTC 조회/소거)을 개발·테스트하기 위한 용도.
/// 응답 포맷은 ATE0(에코 끔)/ATS0(공백 제거)/ATH0(헤더 제거) 기준의 ISO 15765-4(CAN) 형태를 따른다.
/// </summary>
public sealed class SimulatorTransport : IObdTransport
{
    public string Name => "시뮬레이터 (가상 ECU)";
    public bool IsConnected { get; private set; }

    public event Action<string>? DataReceived;
    public event Action? Disconnected;

    private bool _echo = true;
    private double _t;                                   // 시뮬레이션 시간축
    private double _coolant = 23;                        // 시동 직후 냉각수 온도(°C)
    private readonly List<(byte A, byte B)> _dtcs = new() // 저장된 고장코드: P0133, P0420
    {
        (0x01, 0x33),
        (0x04, 0x20),
    };

    private const string Vin = "KMHEM42APXA000001"; // 가상의 현대차 VIN

    public Task ConnectAsync()
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        Disconnected?.Invoke();
        return Task.CompletedTask;
    }

    public async Task SendAsync(string data)
    {
        if (!IsConnected) throw new InvalidOperationException("시뮬레이터가 연결되지 않았습니다.");

        var raw = data.Trim();
        var cmd = raw.ToUpperInvariant().Replace(" ", "");

        await Task.Delay(50); // 실제 동글의 응답 지연 흉내

        var response = Handle(cmd);
        var echoPart = _echo ? raw + "\r" : "";
        DataReceived?.Invoke(echoPart + response + "\r\r>");
    }

    private string Handle(string cmd)
    {
        _t += 0.35;
        if (_coolant < 92) _coolant += 0.4; // 워밍업

        // ---- AT 명령 (ELM327 설정) ----
        if (cmd.StartsWith("AT"))
        {
            if (cmd == "ATZ") { _echo = true; return "ELM327 v1.5"; }
            if (cmd == "ATE0") { _echo = false; return "OK"; }
            if (cmd == "ATE1") { _echo = true; return "OK"; }
            if (cmd == "ATRV") return $"{12.1 + 2.0 * EngineOn():F1}V";
            return "OK"; // ATL0, ATS0, ATH0, ATSP0 등
        }

        // ---- Mode 01: 실시간 데이터 ----
        if (cmd.StartsWith("01") && cmd.Length >= 4)
        {
            var pid = cmd.Substring(2, 2);
            return pid switch
            {
                "00" => "4100BE3FA813",                       // 지원 PID 비트맵
                "04" => Resp01("04", B((int)(Load() * 255 / 100.0))),
                "05" => Resp01("05", B((int)_coolant + 40)),
                "0C" => Resp01("0C", W((int)(Rpm() * 4))),
                "0D" => Resp01("0D", B((int)Speed())),
                "0F" => Resp01("0F", B(35 + 40)),             // 흡기온 35°C
                "10" => Resp01("10", W((int)(Maf() * 100))),
                "11" => Resp01("11", B((int)(Throttle() * 255 / 100.0))),
                "2F" => Resp01("2F", B((int)(68 * 255 / 100.0))), // 연료 68%
                "42" => Resp01("42", W((int)((12.1 + 2.0 * EngineOn()) * 1000))),
                _ => "NO DATA",
            };
        }

        // ---- Mode 03: 저장된 DTC 조회 ----
        if (cmd == "03")
        {
            if (_dtcs.Count == 0) return "4300";
            var sb = new StringBuilder("43");
            sb.Append(B(_dtcs.Count));
            foreach (var (a, b) in _dtcs) { sb.Append(B(a)); sb.Append(B(b)); }
            return sb.ToString();
        }

        // ---- Mode 04: DTC 소거 ----
        if (cmd == "04")
        {
            _dtcs.Clear();
            return "44";
        }

        // ---- Mode 09 PID 02: VIN ----
        if (cmd == "0902")
        {
            var hex = Convert.ToHexString(Encoding.ASCII.GetBytes(Vin));
            return "490201" + hex;
        }

        return "NO DATA";
    }

    // ---- 가상 주행 모델 ----
    private double EngineOn() => 1.0;
    private double Rpm() => 850 + 1650 * (0.5 + 0.5 * Math.Sin(_t * 0.4)) * Drive();
    private double Speed() => 65 * (0.5 + 0.5 * Math.Sin(_t * 0.25)) * Drive();
    private double Throttle() => 12 + 40 * (0.5 + 0.5 * Math.Sin(_t * 0.4));
    private double Load() => 20 + 45 * (0.5 + 0.5 * Math.Sin(_t * 0.4));
    private double Maf() => 3 + 18 * (0.5 + 0.5 * Math.Sin(_t * 0.4));
    private double Drive() => 0.3 + 0.7 * (0.5 + 0.5 * Math.Sin(_t * 0.08)); // 서행↔주행 반복

    private static string B(int v) => Math.Clamp(v, 0, 255).ToString("X2");
    private static string W(int v) => Math.Clamp(v, 0, 65535).ToString("X4");
    private static string Resp01(string pid, string dataHex) => "41" + pid + dataHex;

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
