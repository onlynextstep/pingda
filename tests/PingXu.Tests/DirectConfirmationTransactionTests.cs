using PingXu.Core;

internal static class DirectConfirmationTransactionTests
{
    internal static void Register(Action<string, Action> test)
    {
        foreach (var outcome in new[] { "success", "temporary-failure", "persistent-failure", "restore-failure" })
            test($"No-confirmation transaction without Tick retains recovery: {outcome}", () =>
            {
                var before = new DesktopSnapshot([new("screen", "DISPLAY1", "4K", true, true, true,
                    0, 0, 3840, 2160, 0, 60, [])], "exact-original-native-bytes");
                var requested = new DisplayProfile("p", "portrait", [new("screen", true, true, 0, 0, 2160, 3840, 90, 60)]);
                var trusted = new List<string>();
                var decision = ConfirmationPolicy.Evaluate(new(ConfirmationMode.Never), requested,
                    [requested], before.Displays, "environment", trusted);
                Check(!decision.RequiresConfirmation && !decision.CanRemember);
                var fake = new MemoryDisplay(before, outcome);
                var journaled = false; var savedPrevious = false;
                var transaction = new DisplayTransaction(fake, fake.RestorePersistent,
                    snapshot => { Check(snapshot == before); journaled = true; },
                    snapshot => { Check(snapshot == before); savedPrevious = true; });
                fake.BeforeApply = persist => Check(journaled && (!persist || savedPrevious));
                // No TrialState, Tick, timer, sleep, native API or product process is involved.
                var result = transaction.Begin(before, requested);
                if (result.Success) result = transaction.Finish(true);
                Check(result.Success == (outcome == "success"));
                Check(fake.Applies.SequenceEqual(outcome == "temporary-failure" ? [false] : new[] { false, true }));
                if (outcome == "success")
                {
                    Check(fake.Restored is null && transaction.SafeToContinue && fake.Current.Displays[0].Rotation == 90);
                }
                else
                {
                    Check(fake.Restored == before && fake.PersistRestore == (outcome != "temporary-failure"));
                    Check(transaction.SafeToContinue == (outcome != "restore-failure"));
                    if (outcome != "restore-failure") Check(fake.Current == before);
                }
                Check(trusted.Count == 0);
            });
    }

    private static void Check(bool value) { if (!value) throw new Exception("direct transaction assertion failed"); }

    private sealed class MemoryDisplay(DesktopSnapshot original, string outcome) : IDisplayService
    {
        internal DesktopSnapshot Current = original;
        internal DesktopSnapshot? Restored;
        internal bool PersistRestore;
        internal List<bool> Applies = [];
        internal Action<bool> BeforeApply = _ => { };
        public DesktopSnapshot Capture() => Current;
        public OperationResult Validate(DisplayProfile profile) => OperationResult.Ok();
        public OperationResult Apply(DisplayProfile profile, bool persist)
        {
            BeforeApply(persist); Applies.Add(persist);
            Current = new(profile.Displays.Select(t => new DisplayInfo(t.Id, "DISPLAY1", "4K", true,
                t.Enabled, t.Primary, t.X, t.Y, t.Width, t.Height, t.Rotation, t.RefreshRate, [])).ToList(), "changed");
            return (persist ? outcome is "persistent-failure" or "restore-failure" : outcome == "temporary-failure")
                ? OperationResult.Fail("memory-only injected apply/readback error") : OperationResult.Ok();
        }
        public OperationResult Restore(DesktopSnapshot snapshot) => RestorePersistent(snapshot, false);
        internal OperationResult RestorePersistent(DesktopSnapshot snapshot, bool persist)
        {
            Restored = snapshot; PersistRestore = persist;
            if (outcome == "restore-failure") return OperationResult.Fail("memory-only injected restore error");
            Current = snapshot; return OperationResult.Ok();
        }
    }
}
