using System.Text.Json;
namespace PingXu.Core;
public sealed class ProfileStore(string directory)
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public List<DisplayProfile> Load()
    {
        var path = Path.Combine(directory, "profiles.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : [];
    }
    public static List<DisplayProfile> Parse(string content)
    {
        if (content.Length > 2_000_000) throw new ArgumentException("配置文件过大。");
        var items = JsonSerializer.Deserialize<List<DisplayProfile>>(content) ?? throw new ArgumentException("配置文件内容为空。");
        if (items.Count > 100 || items.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 60 || p.Displays is null || p.Displays.Count == 0 || p.Displays.Count > 32 || p.Displays.Any(d => d is null || string.IsNullOrWhiteSpace(d.Id)))) throw new ArgumentException("配置文件格式不正确。");
        if (items.Select(p => p.Id).Distinct().Count() != items.Count) throw new ArgumentException("配置文件包含重复预设标识。");
        // Validate shape without requiring this computer's devices: imports may belong to another desk.
        foreach (var p in items)
        {
            if (p.SchemaVersion != 1) throw new ArgumentException("此预设使用了不支持的配置版本，请使用对应版本的屏搭打开。原文件未修改。");
            LayoutPlanner.CheckProfileShape(p);
        }
        return items;
    }
    public void Save(List<DisplayProfile> profiles)
    {
        var content = JsonSerializer.Serialize(profiles, Json); Parse(content);
        ConfigurationBackups.Save(directory, "profiles.json", content, value => Parse(value));
    }
    public static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { using (var writer = new StreamWriter(stream, leaveOpen: true)) { writer.Write(content); writer.Flush(); } stream.Flush(true); } File.Move(tmp, path, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
