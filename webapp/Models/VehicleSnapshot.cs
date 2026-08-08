namespace ObdWebApp.Models;

/// <summary>차량에서 읽어온 실시간 값 한 세트.</summary>
public sealed class VehicleSnapshot
{
    public Dictionary<byte, double> Values { get; } = new();
    public double? Get(byte pid) => Values.TryGetValue(pid, out var v) ? v : null;
}
