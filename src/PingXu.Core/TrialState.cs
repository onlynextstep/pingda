namespace PingXu.Core;
public enum TrialStatus { Pending, Keep, Revert }
public sealed class TrialState(double durationSeconds)
{
    public TrialStatus Status { get; private set; }
    public bool Confirm(double elapsed) { Tick(elapsed); if (Status != TrialStatus.Pending) return false; Status = TrialStatus.Keep; return true; }
    public void Tick(double elapsed) { if (Status == TrialStatus.Pending && elapsed >= durationSeconds) Status = TrialStatus.Revert; }
    public void Cancel() { if (Status == TrialStatus.Pending) Status = TrialStatus.Revert; }
}
