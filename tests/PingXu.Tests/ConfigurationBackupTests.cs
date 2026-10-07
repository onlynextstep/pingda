using PingXu.Core;

internal static class ConfigurationBackupTests
{
    public static void Register(Action<string, Action> test)
    {
        void Case(string name, Action<string> run) => test(name, () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "PingXu-backup-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { run(root); } finally { Directory.Delete(root, true); }
        });
        void Validate(string s) { if (!s.StartsWith("valid-")) throw new ArgumentException("invalid"); }
        void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } catch (IOException) { return; } throw new Exception("expected rejection"); }
        Case("backup restores validated previous value and preserves replaced original", root =>
        {
            ConfigurationBackups.Save(root, "profiles.json", "valid-A", Validate);
            ConfigurationBackups.Save(root, "profiles.json", "valid-B", Validate);
            var backup = ConfigurationBackups.List(root, "profiles.json", Validate).Single();
            var original = ConfigurationBackups.Restore(root, "profiles.json", backup.Path, Validate);
            if (File.ReadAllText(Path.Combine(root, "profiles.json")) != "valid-A" || File.ReadAllText(original) != "valid-B") throw new Exception("restore lost contents");
        });
        Case("backup keeps last five distinct previous contents", root =>
        {
            for (int i = 0; i < 9; i++) ConfigurationBackups.Save(root, "profiles.json", "valid-" + i, Validate);
            ConfigurationBackups.Save(root, "profiles.json", "valid-8", Validate);
            var backups = ConfigurationBackups.List(root, "profiles.json", Validate);
            if (backups.Count != 5 || backups.Select(b => File.ReadAllText(b.Path)).Distinct().Count() != 5) throw new Exception("retention mismatch");
        });
        Case("repeated previous contents become recent again", root =>
        {
            foreach (var value in new[] { "A", "B", "C", "D", "E", "A", "F", "G" })
                ConfigurationBackups.Save(root, "profiles.json", "valid-" + value, Validate);
            var values = ConfigurationBackups.List(root, "profiles.json", Validate).Select(b => File.ReadAllText(b.Path)).ToArray();
            if (!values.SequenceEqual(new[] { "valid-F", "valid-A", "valid-E", "valid-D", "valid-C" })) throw new Exception("wrong recency: " + string.Join(",", values));
        });
        Case("invalid-data backup is skipped without hiding valid history", root =>
        {
            ConfigurationBackups.Save(root, "preferences.json", "valid-A", Validate);
            ConfigurationBackups.Save(root, "preferences.json", "valid-B", Validate);
            ConfigurationBackups.Save(root, "preferences.json", "valid-C", Validate);
            File.WriteAllText(ConfigurationBackups.List(root, "preferences.json", Validate)[0].Path, "broken");
            if (ConfigurationBackups.List(root, "preferences.json", s => { if (!s.StartsWith("valid-")) throw new InvalidDataException(); }).Count != 1)
                throw new Exception("valid history hidden");
        });
        Case("corrupt live config is not silently overwritten and restore preserves corrupt original", root =>
        {
            ConfigurationBackups.Save(root, "profiles.json", "valid-A", Validate);
            ConfigurationBackups.Save(root, "profiles.json", "valid-B", Validate);
            File.WriteAllText(Path.Combine(root, "profiles.json"), "broken");
            Reject(() => ConfigurationBackups.Save(root, "profiles.json", "valid-C", Validate));
            var backup = ConfigurationBackups.List(root, "profiles.json", Validate).Single();
            var original = ConfigurationBackups.Restore(root, "profiles.json", backup.Path, Validate);
            if (File.ReadAllText(original) != "broken") throw new Exception("lost corrupt original");
        });
        Case("backup rejects unsupported names and outside restore paths", root =>
        {
            Reject(() => ConfigurationBackups.Save(root, "../profiles.json", "valid-A", Validate));
            Reject(() => ConfigurationBackups.Save(root, "transactions.json", "valid-A", Validate));
            var external = Path.Combine(root, "external.json"); File.WriteAllText(external, "valid-A");
            Reject(() => ConfigurationBackups.Restore(root, "profiles.json", external, Validate));
        });
        Case("restore preserves malformed original bytes exactly", root =>
        {
            ConfigurationBackups.Save(root, "profiles.json", "valid-A", Validate);
            ConfigurationBackups.Save(root, "profiles.json", "valid-B", Validate);
            byte[] bytes = [0xFF, 0xFE, 0, 0xC0, 0x80, 0xFF];
            File.WriteAllBytes(Path.Combine(root, "profiles.json"), bytes);
            var candidate = ConfigurationBackups.List(root, "profiles.json", Validate).Single();
            var original = ConfigurationBackups.Restore(root, "profiles.json", candidate.Path, Validate);
            if (!File.ReadAllBytes(original).SequenceEqual(bytes)) throw new Exception("original bytes changed");
        });
        Case("listing backups is read-only and ignores damaged candidates", root =>
        {
            if (ConfigurationBackups.List(root, "profiles.json", Validate).Count != 0 || Directory.GetFileSystemEntries(root).Length != 0) throw new Exception("read wrote files");
            ConfigurationBackups.Save(root, "profiles.json", "valid-A", Validate);
            ConfigurationBackups.Save(root, "profiles.json", "valid-B", Validate);
            var path = ConfigurationBackups.List(root, "profiles.json", Validate).Single().Path;
            File.WriteAllText(path, "broken");
            if (ConfigurationBackups.List(root, "profiles.json", Validate).Count != 0) throw new Exception("damaged backup accepted");
            Reject(() => ConfigurationBackups.Restore(root, "profiles.json", path, Validate));
            if (File.ReadAllText(Path.Combine(root, "profiles.json")) != "valid-B") throw new Exception("modified before validation");
        });
        Case("profile store supports old arrays and rejects future schema without overwriting", root =>
        {
            const string legacy = "[{\"Id\":\"p\",\"Name\":\"Legacy\",\"Displays\":[{\"Id\":\"d\",\"Enabled\":true,\"Primary\":true,\"Width\":1920,\"Height\":1080,\"RefreshRate\":60}]}]";
            var parsed = ProfileStore.Parse(legacy);
            var future = legacy.Replace("\"Name\":", "\"SchemaVersion\":999,\"Name\":");
            File.WriteAllText(Path.Combine(root, "profiles.json"), future);
            Reject(() => new ProfileStore(root).Load());
            Reject(() => new ProfileStore(root).Save(parsed));
            if (File.ReadAllText(Path.Combine(root, "profiles.json")) != future) throw new Exception("future config overwritten");
        });
    }
}
