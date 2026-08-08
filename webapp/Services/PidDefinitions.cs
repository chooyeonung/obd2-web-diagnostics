namespace ObdWebApp.Services;

/// <summary>SAE J1979 Mode 01 PID 정의: 이름, 단위, 데이터 길이, 디코딩 공식.</summary>
public sealed record PidDefinition(byte Pid, string Name, string Unit, int DataBytes, Func<byte[], double> Decode)
{
    public string PidHex => Pid.ToString("X2");
}

public static class PidDefinitions
{
    /// <summary>이 앱이 사용하는 Mode 01 PID 테이블. 필요 시 여기에 추가하면 UI까지 자동 반영된다.</summary>
    public static readonly IReadOnlyDictionary<byte, PidDefinition> Mode01 = new Dictionary<byte, PidDefinition>
    {
        [0x04] = new(0x04, "엔진 부하", "%", 1, d => d[0] * 100.0 / 255.0),
        [0x05] = new(0x05, "냉각수 온도", "°C", 1, d => d[0] - 40.0),
        [0x0C] = new(0x0C, "엔진 회전수", "rpm", 2, d => (d[0] * 256 + d[1]) / 4.0),
        [0x0D] = new(0x0D, "차량 속도", "km/h", 1, d => d[0]),
        [0x0F] = new(0x0F, "흡기 온도", "°C", 1, d => d[0] - 40.0),
        [0x10] = new(0x10, "흡입 공기량(MAF)", "g/s", 2, d => (d[0] * 256 + d[1]) / 100.0),
        [0x11] = new(0x11, "스로틀 개도", "%", 1, d => d[0] * 100.0 / 255.0),
        [0x2F] = new(0x2F, "연료 잔량", "%", 1, d => d[0] * 100.0 / 255.0),
        [0x42] = new(0x42, "제어모듈 전압", "V", 2, d => (d[0] * 256 + d[1]) / 1000.0),
    };
}
