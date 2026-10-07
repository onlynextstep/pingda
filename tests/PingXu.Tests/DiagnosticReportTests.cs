using System.Text.Json;
using PingXu.Core;

internal static class DiagnosticReportTests
{
    public static void Register(Action<string, Action> test)
    {
        DesktopSnapshot Snapshot() => new([
            new("SECRET-ID", "SECRET-DEVICE", "SECRET-ALIAS", true, true, true, -1920, 0, 1920, 1080, 0, 60, [new(1920, 1080, 60)]),
            new("SECOND-ID", "SECOND-DEVICE", "SECOND-ALIAS", true, false, false, 0, 0, 3840, 2160, 90, 144, [])], "SECRET-NATIVE");
        test("diagnostic report contains layout facts but no native identifiers or aliases", () =>
        {
            var report = DiagnosticReport.Create(Snapshot(), new Version(0, 5, 3), new Version(10, 0, 22631), 5, true);
            if (report.Contains("SECRET") || report.Contains("SECOND")) throw new Exception("private source fields leaked");
            using var json = JsonDocument.Parse(report);
            var displays = json.RootElement.GetProperty("Displays");
            var allowed = new[] { "ReportVersion", "AppVersion", "WindowsVersion", "SnapshotSource", "PresetCount", "RecoveryNeedsAttention", "Displays" };
            if (!json.RootElement.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(allowed.Order())) throw new Exception("report exported fields beyond allow-list");
            if (!displays[0].TryGetProperty("ReportIndex", out var reportIndex) || reportIndex.GetInt32() != 1 || displays[0].TryGetProperty("Number", out _)) throw new Exception("ambiguous display numbering");
            if (displays.GetArrayLength() != 2 || displays[0].GetProperty("X").GetInt32() != -1920 || displays[1].GetProperty("Enabled").GetBoolean()) throw new Exception("missing topology facts");
            if (!json.RootElement.GetProperty("RecoveryNeedsAttention").GetBoolean()) throw new Exception("recovery state lost");
        });
        test("diagnostic unavailable layout remains unknown rather than claiming zero displays", () =>
        {
            using var json = JsonDocument.Parse(DiagnosticReport.Create(null, new Version(0, 5, 3), new Version(10, 0), null, null));
            if (json.RootElement.GetProperty("Displays").ValueKind != JsonValueKind.Null || json.RootElement.GetProperty("PresetCount").ValueKind != JsonValueKind.Null) throw new Exception("unknown data misrepresented");
        });
    }
}
