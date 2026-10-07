using PingXu.Core;
using PingXu.Windows;
using static AdapterTests;

internal static class ReadbackStabilityTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (var persist in new[] { false, true })
            test($"Transient identity readback settles without reapplying persist={persist}", () =>
            {
                var api = new ChangingReadApi(n => n <= 2);
                var result = new WindowsDisplayService(api, _ => { }).Apply(One(), persist);
                Check(result.Success);
                Check(api.ReadsAfterApply >= 4);
                Check(api.Inner.Flags.Count(f => (f & 0x80) != 0) == 1);
            });
        test("One matching sample followed by wrong identity cannot commit", () =>
        {
            var api = new ChangingReadApi(n => n != 1);
            var waits = new List<int>();
            var result = new WindowsDisplayService(api, waits.Add).Apply(One(), false);
            Check(!result.Success && api.ReadsAfterApply > 1 && api.ReadsAfterApply <= 20);
            Check(waits.Count == 12 && waits.All(ms => ms == 250));
            Check(!result.Message.Contains("monitor-") && !result.Message.Contains("看护"));
            Check(api.Inner.Flags.Count(f => (f & 0x80) != 0) == 1);
        });
        test("Matching samples separated by mismatch are not stable", () =>
        {
            var api = new ChangingReadApi(n => n % 2 == 0);
            Check(!new WindowsDisplayService(api, _ => { }).Apply(One(), false).Success);
        });
        test("Failed verification retains expected and actual identities in local diagnostics only", () =>
        {
            var api = new ChangingReadApi(_ => true);
            string? log = null;
            var result = new WindowsDisplayService(api, _ => { }, s => log = s).Apply(One(), false);
            Check(!result.Success && !result.Message.Contains("monitor") && !result.Message.Contains("回读"));
            using var doc = System.Text.Json.JsonDocument.Parse(log!);
            var root = doc.RootElement;
            Check(root.GetProperty("Phase").GetString() == "apply");
            Check(root.GetProperty("Requested")[0].GetProperty("Id").GetString() == "monitor-A");
            var samples = root.GetProperty("Samples");
            Check(samples.GetArrayLength() == 13);
            Check(samples[0].GetProperty("Actual")[0].GetProperty("Id").GetString() == "transient-monitor");
        });
        test("Diagnostic write failure cannot prevent transaction recovery", () =>
        {
            ChangingReadApi? api = null;
            api = new ChangingReadApi(n => api!.Writes == 1 && n <= 13);
            var service = new WindowsDisplayService(api, _ => { }, _ => throw new IOException("full"));
            var before = service.Capture();
            var tx = new DisplayTransaction(service, service.Restore, _ => { }, _ => { });
            Check(!tx.Begin(before, One()).Success && tx.SafeToContinue);
            Check(api.Writes == 2 && DisplayTransaction.SameLayout(before.Displays, service.Capture().Displays));
        });
        test("Persistent readback mismatch restores the persistent original", () =>
        {
            ChangingReadApi? api = null;
            api = new ChangingReadApi(n => api!.Writes == 2 && n <= 13);
            var service = new WindowsDisplayService(api, _ => { });
            var before = service.Capture();
            var tx = new DisplayTransaction(service, service.Restore, _ => { }, _ => { });
            Check(tx.Begin(before, One()).Success && !tx.Finish(true).Success && tx.SafeToContinue);
            Check(api.Inner.Flags.Where(f => (f & 0x80) != 0).SequenceEqual(new uint[] { 0xA0, 0x2A0, 0x2A0 }));
            Check(DisplayTransaction.SameLayout(before.Displays, service.Capture().Displays));
        });
        test("Persistent mismatch before saving never writes the new persistent layout", () =>
        {
            bool drift = false;
            var api = new ChangingReadApi(_ => drift);
            var service = new WindowsDisplayService(api, _ => { });
            var before = service.Capture();
            bool saved = false;
            var tx = new DisplayTransaction(service, (s, p) => { drift = false; return service.Restore(s, p); }, _ => { }, _ => saved = true);
            Check(tx.Begin(before, One()).Success);
            drift = true;
            Check(!tx.Finish(true).Success && tx.SafeToContinue && !saved);
            Check(api.Inner.Flags.Where(f => (f & 0x80) != 0).SequenceEqual(new uint[] { 0xA0, 0xA0 }));
        });
        test("Pre-save verification settles without any native write", () =>
        {
            bool drifting = false;
            int count = 0;
            var api = new ChangingReadApi(_ => drifting && ++count < 3);
            var service = new WindowsDisplayService(api, _ => { });
            Check(service.Apply(One(), false).Success);
            int writes = api.Inner.Flags.Count;
            drifting = true;
            Check(service.Verify(One()).Success && count >= 4);
            Check(api.Inner.Flags.Count == writes);
        });
        test("Transaction uses stable pre-save verification before persistent apply", () =>
        {
            bool drifting = false;
            int count = 0;
            var api = new ChangingReadApi(_ => drifting && ++count < 3);
            var service = new WindowsDisplayService(api, _ => { });
            var before = service.Capture();
            var tx = new DisplayTransaction(service, service.Restore, _ => { }, _ => { });
            Check(tx.Begin(before, One()).Success);
            drifting = true;
            Check(tx.Finish(true).Success && tx.SafeToContinue);
            Check(api.Inner.Flags.Count(f => (f & 0x80) != 0) == 2);
        });
    }

    internal sealed class ChangingReadApi(Func<int, bool> wrong) : ICcdApi
    {
        internal readonly FakeApi Inner = new();
        internal int ReadsAfterApply, Writes;
        bool applied;
        public CcdState Read()
        {
            var state = Inner.Read();
            if (!applied || !wrong(++ReadsAfterApply)) return state;
            var outputs = state.Outputs.ToArray();
            outputs[0] = outputs[0] with { Id = "transient-monitor" };
            return state with { Outputs = outputs };
        }
        public int Set(CcdPlan plan, uint flags)
        {
            var result = Inner.Set(plan, flags);
            if ((flags & 0x80) != 0) { applied = true; ReadsAfterApply = 0; Writes++; }
            return result;
        }
    }
}
