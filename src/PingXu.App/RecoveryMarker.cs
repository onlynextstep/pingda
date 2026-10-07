using System.IO;
using System.Text.Json;
using PingXu.Core;
namespace PingXu.App;
public record RecoveryMarker(string TransactionPath, bool Unresolved)
{
    static string FilePath => Path.Combine(Program.DataDirectory, "recovery-state.json");
    public static RecoveryMarker? Read() => File.Exists(FilePath) ? JsonSerializer.Deserialize<RecoveryMarker>(File.ReadAllText(FilePath)) : null;
    public static bool NeedsAttention()
    {
        var marker = Read(); if (marker?.Unresolved != true) return false;
        var statusPath = marker.TransactionPath + ".status";
        if (!File.Exists(statusPath)) return true;
        try { var status = JsonSerializer.Deserialize<TrialProgress>(File.ReadAllText(statusPath)); return !(status?.SafeToContinue == true && status.Phase is "committed" or "reverted" or "error"); } catch { return true; }
    }
    public void Save() => ProfileStore.AtomicWrite(FilePath, JsonSerializer.Serialize(this));
    public static bool GuardianRunning()
    {
        using var gate = new Mutex(false, "Local\\PingXu.DisplayTransaction." + Environment.UserName);
        bool owned = false;
        try { try { owned = gate.WaitOne(0); } catch (AbandonedMutexException) { owned = true; } return !owned; }
        finally { if (owned) gate.ReleaseMutex(); }
    }
}
