using PingXu.Core;
using PingXu.Windows;
using static AdapterTests;

internal static class DisplayCapacityRaceTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (var operation in new[] { "validate", "temporary", "persistent" })
            test($"Capacity {operation} rejects latest seven connected before any native Set", () =>
            {
                var api = new HotplugApi();
                var service = new WindowsDisplayService(api);
                Check(service.Capture().Displays.Count(d => d.Connected) == 6);
                api.Inner.State = AddInactive(api.Inner.State);
                var result = operation == "validate" ? service.Validate(One()) : service.Apply(One(), operation == "persistent");
                RequireLimit(result);
                Check(api.Inner.Flags.Count == 0);
            });

        foreach (var persist in new[] { false, true })
        {
            test($"Capacity forward readback detects newly connected disabled seventh persist={persist}", () =>
            {
                var api = new HotplugApi { ConnectOnApply = 1 };
                var result = new WindowsDisplayService(api).Apply(One(), persist);
                RequireLimit(result);
                Check(!result.Message.Contains("看护") && !result.Message.Contains("未更改") && !result.Message.Contains("已恢复"));
                Check(api.Inner.Flags.SequenceEqual(new uint[] { 0x60, persist ? 0x2A0u : 0xA0u }));
                Check(CcdLogic.Describe(api.Inner.State).Count(d => d.Connected) == 7);
            });
            test($"Capacity Restore remains available with seven connected before and after persist={persist}", () =>
            {
                var api = new HotplugApi();
                var service = new WindowsDisplayService(api);
                var saved = service.Capture();
                api.Inner.State = AddInactive(api.Inner.State);
                var result = service.Restore(saved, persist);
                Check(result.Success);
                Check(api.Inner.Flags.SequenceEqual(new uint[] { 0x60, persist ? 0x2A0u : 0xA0u }));
                var actual = service.Capture();
                Check(actual.Displays.Count(d => d.Connected) == 7);
                Check(DisplayTransaction.SameLayout(saved.Displays.Where(d => d.Enabled).ToList(),
                    actual.Displays.Where(d => d.Enabled).ToList()));
            });
            test($"Capacity offline seventh record allows forward apply persist={persist}", () =>
            {
                var api = new HotplugApi();
                api.Inner.State = AddInactive(api.Inner.State, connected: false);
                Check(new WindowsDisplayService(api).Apply(One(), persist).Success);
                Check(api.Inner.Flags.SequenceEqual(new uint[] { 0x60, persist ? 0x2A0u : 0xA0u }));
            });
        }
        foreach (var applyNumber in new[] { 1, 2 })
            test($"Capacity readback on forward write {applyNumber} makes transaction restore original native snapshot", () =>
            {
                var api = new HotplugApi { ConnectOnApply = applyNumber };
                var service = new WindowsDisplayService(api);
                var before = service.Capture();
                var transaction = new DisplayTransaction(service, service.Restore, _ => { }, _ => { });
                var result = transaction.Begin(before, One());
                if (applyNumber == 2)
                {
                    Check(result.Success);
                    result = transaction.Finish(true);
                }
                RequireLimit(result);
                Check(result.Message.Contains("已恢复") && !result.Message.Contains("未更改"));
                Check(transaction.SafeToContinue);
                var writes = api.Inner.Flags.Where(f => (f & 0x80) != 0);
                Check(writes.SequenceEqual(applyNumber == 1 ? new uint[] { 0xA0, 0xA0 } : new uint[] { 0xA0, 0x2A0, 0x2A0 }));
                var actual = service.Capture();
                Check(actual.Displays.Count(d => d.Connected) == 7);
                Check(DisplayTransaction.SameLayout(before.Displays.Where(d => d.Enabled).ToList(),
                    actual.Displays.Where(d => d.Enabled).ToList()));
            });
    }

    private static void RequireLimit(OperationResult result)
    {
        if (result.Success || !result.Message.Contains('7') || !result.Message.Contains('6') || !result.Message.Contains("连接"))
            throw new Exception($"Expected connected capacity rejection (7 > 6), got success={result.Success}: {result.Message}");
    }

    // Add a distinct physical output and inactive available route, leaving measured active modes untouched.
    private static CcdState AddInactive(CcdState state, bool connected = true)
    {
        var path = state.Paths.First(p => !CcdLogic.Active(p));
        path.Source.Id = path.Target.Id = (uint)(100 + state.Outputs.Length);
        path.Source.ModeIndex = path.Target.ModeIndex = uint.MaxValue;
        path.Source.StatusFlags = 0;
        path.Target.Available = connected ? 1 : 0;
        var output = state.Outputs.Last() with { Target = CcdLogic.Target(path),
            Id = $"hotplug-{state.Outputs.Length}", DeviceName = $"DISPLAY{state.Outputs.Length + 1}" };
        return state with { Paths = [.. state.Paths, path], Outputs = [.. state.Outputs, output] };
    }

    // Fake only CCD reads/writes. Production service, planner, transaction and recovery remain real.
    private sealed class HotplugApi : ICcdApi
    {
        internal readonly FakeApi Inner = new();
        internal int ConnectOnApply;
        private int applies;
        internal HotplugApi()
        {
            while (Inner.State.Outputs.Length < 6) Inner.State = AddInactive(Inner.State);
        }
        public CcdState Read() => Inner.Read();
        public int Set(CcdPlan plan, uint flags)
        {
            var result = Inner.Set(plan, flags);
            if ((flags & 0x80) != 0 && ++applies == ConnectOnApply) Inner.State = AddInactive(Inner.State);
            return result;
        }
    }
}
