using System.Runtime.InteropServices;
using System.Text.Json;

namespace PingXu.Windows;

internal static class SnapshotCodec
{
    // Raw blittable CCD bytes preserve the complete union, rational timing and status data.
    // Endpoints are decoded only for validation; Restore always rebinds to freshly queried IDs.
    private sealed record Envelope(int Version, DateTimeOffset CapturedAtUtc, string Machine,
        byte[] Paths, byte[] Modes, Output[] Outputs);
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true, MaxDepth = 32 };
    internal static string Encode(CcdState state) => JsonSerializer.Serialize(new Envelope(1, DateTimeOffset.UtcNow,
        Environment.MachineName, Bytes(state.Paths), Bytes(state.Modes), state.Outputs), Options);
    internal static byte[] Bytes<T>(T[] data) where T : unmanaged => MemoryMarshal.AsBytes(data.AsSpan()).ToArray();
    internal static CcdState Decode(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16 * 1024 * 1024)
            throw new InvalidOperationException("原生显示快照为空或过大，无法恢复。");
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(json, Options);
            if (envelope is null || envelope.Version != 1 || envelope.Paths is null || envelope.Modes is null ||
                envelope.Outputs is null || envelope.Outputs.Any(o => o is null || o.Modes is null) ||
                envelope.CapturedAtUtc == default || !string.Equals(envelope.Machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("原生显示快照版本、来源或内容无效，无法恢复。");
            var state = new CcdState(Read<PathInfo>(envelope.Paths), Read<ModeInfo>(envelope.Modes), envelope.Outputs);
            _ = CcdLogic.Describe(state);
            return state;
        }
        catch (JsonException e) { throw new InvalidOperationException("原生显示快照 JSON 损坏，无法恢复。", e); }
    }
    private static T[] Read<T>(byte[] bytes) where T : unmanaged
    {
        if (bytes.Length == 0 || bytes.Length % Marshal.SizeOf<T>() != 0 || bytes.Length / Marshal.SizeOf<T>() > 65536)
            throw new InvalidOperationException("原生显示快照的数组长度无效。");
        return MemoryMarshal.Cast<byte, T>(bytes).ToArray();
    }
}
