using System.Security.Cryptography;
using System.Text;

namespace PingXu.Core;

public sealed record ConfigurationBackup(string Path, DateTime SavedAtUtc);

/// <summary>Validated history for two user configuration files; never used for display transactions.</summary>
public static class ConfigurationBackups
{
    const int MaximumBytes = 8_000_000;
    static string Root(string directory, string name)
    {
        if (name is not ("profiles.json" or "preferences.json")) throw new ArgumentException("不支持此配置文件。");
        var root = Path.GetFullPath(directory);
        CheckPath(root);
        return root;
    }

    static void CheckPath(string path)
    {
        for (var current = new FileInfo(path) as FileSystemInfo; current != null;
             current = current is DirectoryInfo dir ? dir.Parent : ((FileInfo)current).Directory)
            if ((File.Exists(current.FullName) || Directory.Exists(current.FullName)) &&
                (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("配置路径包含链接，未读写文件。");
    }

    static string Read(string path)
    {
        CheckPath(path);
        if (new FileInfo(path).Length > MaximumBytes) throw new IOException("配置文件过大，未读取。");
        return File.ReadAllText(path);
    }

    static T Locked<T>(string root, Func<T> action)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())));
        using var mutex = new Mutex(false, @"Local\PingXu.Configuration." + hash);
        bool held = false;
        try
        {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(2)); } catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("配置正在被其他操作使用，请稍后重试。");
            return action();
        }
        finally { if (held) mutex.ReleaseMutex(); }
    }

    public static IReadOnlyList<ConfigurationBackup> List(string directory, string name, Action<string> validate)
    {
        var root = Root(directory, name);
        var history = Path.Combine(root, "configuration-backups", name);
        CheckPath(history);
        if (!Directory.Exists(history)) return [];
        var result = new List<ConfigurationBackup>();
        foreach (var path in Directory.EnumerateFiles(history, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _)) continue;
            try { validate(Read(path)); result.Add(new(path, File.GetLastWriteTimeUtc(path))); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or System.Text.Json.JsonException) { }
        }
        return result.OrderByDescending(b => b.SavedAtUtc).ThenBy(b => b.Path, StringComparer.Ordinal).ToList();
    }

    public static void Save(string directory, string name, string content, Action<string> validate)
    {
        var root = Root(directory, name);
        validate(content);
        Locked(root, () =>
        {
            var path = Path.Combine(root, name); CheckPath(path);
            if (File.Exists(path))
            {
                var previous = Read(path); validate(previous); // Never silently overwrite damaged or future-format data.
                if (previous == content) return true;
                var history = Path.Combine(root, "configuration-backups", name); CheckPath(history);
                var backups = List(root, name, validate);
                var existing = backups.FirstOrDefault(b => Read(b.Path) == previous);
                var backupPath = existing?.Path ?? Path.Combine(history, Guid.NewGuid().ToString("N") + ".json");
                if (existing == null) ProfileStore.AtomicWrite(backupPath, previous);
                // A repeated configuration is recent again; do not prune by its first-ever backup date.
                var latest = backups.Count == 0 ? DateTime.MinValue : backups.Max(b => b.SavedAtUtc);
                var now = DateTime.UtcNow;
                File.SetLastWriteTimeUtc(backupPath, now > latest ? now : latest.AddTicks(1));
            }
            ProfileStore.AtomicWrite(path, content);
            // Only our validated historical files are eligible for retention cleanup.
            foreach (var backup in List(root, name, validate).Skip(5)) File.Delete(backup.Path);
            return true;
        });
    }

    /// <returns>The preserved original path, or empty when no original existed.</returns>
    public static string Restore(string directory, string name, string backupPath, Action<string> validate)
    {
        var root = Root(directory, name);
        return Locked(root, () =>
        {
            var candidate = Path.GetFullPath(backupPath);
            if (!List(root, name, validate).Any(b => string.Equals(b.Path, candidate, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("请选择列表中的有效备份。");
            var content = Read(candidate); validate(content);
            var path = Path.Combine(root, name); CheckPath(path);
            string original = "";
            if (File.Exists(path))
            {
                original = Path.Combine(root, "configuration-originals", name + "." + Guid.NewGuid().ToString("N") + ".original");
                CheckPath(original); Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                File.Copy(path, original, false); // Byte-for-byte preservation, including malformed JSON or encoding.
            }
            ProfileStore.AtomicWrite(path, content);
            return original;
        });
    }
}
