using System.Text.Json;
namespace PingXu.Core;

public static class DiagnosticReport
{
    public static string Create(DesktopSnapshot? snapshot, Version appVersion, Version windowsVersion,
        int? presetCount, bool? recoveryNeedsAttention) => JsonSerializer.Serialize(new
        {
            ReportVersion = 1,
            AppVersion = appVersion.ToString(),
            WindowsVersion = windowsVersion.ToString(),
            SnapshotSource = "ApplicationLastRead",
            PresetCount = presetCount,
            RecoveryNeedsAttention = recoveryNeedsAttention,
            // Explicit allow-list: never serialize the original snapshot, NativeData, names, or IDs.
            Displays = snapshot?.Displays.Select((d, index) => new
            {
                ReportIndex = index + 1,
                d.Connected, d.Enabled, d.Primary,
                d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate,
                AvailableModeCount = d.Modes.Count
            }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true });
}
