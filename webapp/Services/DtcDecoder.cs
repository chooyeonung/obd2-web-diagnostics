namespace ObdWebApp.Services;

public sealed record DiagnosticTroubleCode(string Code, string Description);

/// <summary>Mode 03 응답의 2바이트 쌍을 P/C/B/U 코드 문자열로 변환한다.</summary>
public static class DtcDecoder
{
    private static readonly char[] Systems = { 'P', 'C', 'B', 'U' };

    public static DiagnosticTroubleCode Decode(byte a, byte b)
    {
        var system = Systems[(a >> 6) & 0x03];
        var d1 = (a >> 4) & 0x03;
        var d2 = a & 0x0F;
        var d3 = (b >> 4) & 0x0F;
        var d4 = b & 0x0F;
        var code = $"{system}{d1}{d2:X}{d3:X}{d4:X}";
        return new DiagnosticTroubleCode(code, Describe(code));
    }

    /// <summary>자주 보이는 코드의 한글 설명. 없는 코드는 일반 안내로 대체.</summary>
    private static string Describe(string code) => KnownCodes.TryGetValue(code, out var desc)
        ? desc
        : code[0] switch
        {
            'P' => "파워트레인(엔진/변속기) 계통 고장코드",
            'C' => "섀시(제동/조향/현가) 계통 고장코드",
            'B' => "바디(에어백/도어 등) 계통 고장코드",
            _ => "네트워크(CAN 통신) 계통 고장코드",
        };

    private static readonly Dictionary<string, string> KnownCodes = new()
    {
        ["P0011"] = "캠샤프트 위치 타이밍 과진각 (뱅크 1)",
        ["P0101"] = "MAF 센서 회로 범위/성능 이상",
        ["P0113"] = "흡기온 센서 회로 높음",
        ["P0128"] = "냉각수 온도가 정상 작동 온도에 미달 (서모스탯 점검)",
        ["P0133"] = "산소센서 응답 느림 (뱅크 1, 센서 1)",
        ["P0171"] = "연료 계통 희박 (뱅크 1)",
        ["P0174"] = "연료 계통 희박 (뱅크 2)",
        ["P0300"] = "다기통 실화 감지",
        ["P0301"] = "1번 실린더 실화",
        ["P0302"] = "2번 실린더 실화",
        ["P0303"] = "3번 실린더 실화",
        ["P0304"] = "4번 실린더 실화",
        ["P0420"] = "촉매 효율 저하 (뱅크 1)",
        ["P0442"] = "증발가스 계통 미세 누설",
        ["P0455"] = "증발가스 계통 대량 누설 (주유구 캡 점검)",
        ["P0500"] = "차속 센서 이상",
        ["P0562"] = "시스템 전압 낮음 (배터리/발전기 점검)",
        ["U0100"] = "ECM/PCM과의 통신 두절",
    };
}
