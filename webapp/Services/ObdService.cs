using System.Text;

namespace ObdWebApp.Services;

/// <summary>차량에서 읽어온 실시간 값 한 세트.</summary>
public sealed class VehicleSnapshot
{
    public Dictionary<byte, double> Values { get; } = new();
    public double? Get(byte pid) => Values.TryGetValue(pid, out var v) ? v : null;
}

/// <summary>
/// 고수준 진단 API. ELM327 응답 텍스트를 파싱해 의미 있는 값으로 변환한다.
/// 실차 응답의 흔한 변형(SEARCHING 프리픽스, 멀티라인, NO DATA)에 관대하게 동작하도록 작성.
/// </summary>
public sealed class ObdService : IAsyncDisposable
{
    private Elm327Client? _client;

    public bool IsConnected => _client?.Transport.IsConnected ?? false;
    public string TransportName => _client?.Transport.Name ?? "-";

    public event Action<string, string>? CommandLogged;
    public event Action? Disconnected;

    public async Task ConnectAsync(IObdTransport transport)
    {
        await DisposeClientAsync();

        _client = new Elm327Client(transport);
        _client.CommandLogged += (c, r) => CommandLogged?.Invoke(c, r);
        transport.Disconnected += () => Disconnected?.Invoke();

        await transport.ConnectAsync();
        await _client.InitializeAsync();
    }

    public async Task DisconnectAsync()
    {
        if (_client is not null)
            await _client.Transport.DisconnectAsync();
    }

    // ---------- Mode 01: 실시간 데이터 ----------

    public async Task<double?> ReadPidAsync(byte pid)
    {
        if (_client is null) return null;
        if (!PidDefinitions.Mode01.TryGetValue(pid, out var def)) return null;

        var resp = await _client.SendCommandAsync("01" + def.PidHex);
        var data = ExtractDataBytes(resp, "41" + def.PidHex, def.DataBytes);
        return data is null ? null : def.Decode(data);
    }

    public async Task<VehicleSnapshot> ReadSnapshotAsync(IEnumerable<byte> pids)
    {
        var snap = new VehicleSnapshot();
        foreach (var pid in pids)
        {
            var v = await ReadPidAsync(pid);
            if (v is not null) snap.Values[pid] = v.Value;
        }
        return snap;
    }

    // ---------- Mode 03/04: 고장코드 ----------

    public async Task<List<DiagnosticTroubleCode>> ReadDtcsAsync()
    {
        var result = new List<DiagnosticTroubleCode>();
        if (_client is null) return result;

        var resp = await _client.SendCommandAsync("03");
        var hex = ConcatHexAfter(resp, "43");
        if (hex is null) return result;

        var bytes = HexToBytes(hex);
        var offset = 0;
        // CAN 응답은 [개수] 바이트가 선행된다. 바이트 수가 홀수면 첫 바이트를 개수로 간주하고 건너뛴다.
        if (bytes.Length % 2 == 1) offset = 1;

        for (var i = offset; i + 1 < bytes.Length; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0) continue; // 빈 슬롯
            result.Add(DtcDecoder.Decode(bytes[i], bytes[i + 1]));
        }
        return result;
    }

    /// <summary>고장코드 및 관련 학습값 소거(Mode 04). 사용자 확인을 거친 뒤에만 호출할 것.</summary>
    public async Task<bool> ClearDtcsAsync()
    {
        if (_client is null) return false;
        var resp = await _client.SendCommandAsync("04");
        return resp.Contains("44");
    }

    // ---------- Mode 09: 차대번호(VIN) ----------

    public async Task<string?> ReadVinAsync()
    {
        if (_client is null) return null;
        var resp = await _client.SendCommandAsync("0902");
        var hex = ConcatHexAfter(resp, "4902");
        if (hex is null || hex.Length < 4) return null;

        // 응답 형태: 4902 [레코드번호 1바이트] [VIN ASCII...] — 레코드번호를 건너뛴다
        var bytes = HexToBytes(hex[2..]);
        var sb = new StringBuilder();
        foreach (var b in bytes)
            if (b is >= 0x20 and < 0x7F) sb.Append((char)b);
        var vin = sb.ToString().Trim();
        return vin.Length >= 11 ? vin : null; // VIN은 17자, 최소한의 유효성만 확인
    }

    // ---------- 응답 파싱 유틸 ----------

    /// <summary>응답에서 기대 프리픽스(예: "410C") 뒤의 데이터 바이트를 추출한다.</summary>
    private static byte[]? ExtractDataBytes(string response, string expectedPrefix, int dataBytes)
    {
        foreach (var line in SplitLines(response))
        {
            var hex = HexOnly(line);
            var idx = hex.IndexOf(expectedPrefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;

            var start = idx + expectedPrefix.Length;
            if (hex.Length < start + dataBytes * 2) continue;

            return HexToBytes(hex.Substring(start, dataBytes * 2));
        }
        return null;
    }

    /// <summary>응답의 모든 라인에서 프리픽스 이후의 16진수 문자열을 이어붙인다(멀티프레임 대응).</summary>
    private static string? ConcatHexAfter(string response, string prefix)
    {
        var joined = HexOnly(string.Concat(SplitLines(response).Select(StripFrameIndex).Select(HexOnly)));
        var idx = joined.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var hex = joined[(idx + prefix.Length)..];
        return hex.Length % 2 == 1 ? hex[..^1] : hex;
    }

    /// <summary>ISO-TP 멀티프레임 표시("0:", "1:" 등)를 제거한다.</summary>
    private static string StripFrameIndex(string line)
    {
        var colon = line.IndexOf(':');
        return colon is > 0 and < 4 ? line[(colon + 1)..] : line;
    }

    private static IEnumerable<string> SplitLines(string response) =>
        response.Split('\r', '\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Where(l => !l.Contains("SEARCHING", StringComparison.OrdinalIgnoreCase))
                .Where(l => !l.Contains("NO DATA", StringComparison.OrdinalIgnoreCase))
                .Where(l => !l.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                .Where(l => !l.Contains("OK", StringComparison.OrdinalIgnoreCase));

    private static string HexOnly(string s) =>
        new(s.Where(Uri.IsHexDigit).ToArray());

    private static byte[] HexToBytes(string hex)
    {
        var result = new byte[hex.Length / 2];
        for (var i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return result;
    }

    private async Task DisposeClientAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
    }

    public ValueTask DisposeAsync() => new(DisposeClientAsync());
}
