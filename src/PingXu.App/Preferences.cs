using System.IO;
using System.Text.Json;
using PingXu.Core;
namespace PingXu.App;
public record Preferences(bool Hotkeys, Dictionary<string, string> Aliases)
{
    public int SchemaVersion { get; init; } = 1;
    public bool TrayHintShown { get; init; }
    public static bool ClaimTrayHint(string? directory = null)
    {
        var current = Load(directory);
        if (current.TrayHintShown) return false;
        (current with { TrayHintShown = true }).Save(directory);
        return true;
    }
    public ConfirmationOptions Confirmation { get; init; } = new();
    public List<string> TrustedProfiles { get; init; } = [];
    public Dictionary<string, HotkeyGesture>? Shortcuts { get; init; }
    public static Preferences Load(string? directory = null)
    {
        var path = Path.Combine(directory ?? Program.DataDirectory, "preferences.json");
        if (!File.Exists(path)) return new(false, new(StringComparer.OrdinalIgnoreCase));
        return Parse(File.ReadAllText(path));
    }
    public static Preferences Parse(string content)
    {
        if (content.Length > 2_000_000) throw new InvalidDataException("偏好设置文件过大。");
        var p = JsonSerializer.Deserialize<Preferences>(content) ?? throw new InvalidDataException("偏好设置为空。");
        if (p.SchemaVersion != 1) throw new InvalidDataException("此偏好设置使用了不支持的版本，请使用对应版本的屏搭打开。原文件未修改。");
        if (p.Aliases == null || p.Aliases.Count > 100 || p.Aliases.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Value) || x.Value.Length > 40)) throw new InvalidDataException("屏幕别名格式不正确。");
        p.ValidateConfirmation();
        if (p.Shortcuts != null) ShortcutBindings.Validate(p.Shortcuts);
        return p;
    }
    void ValidateConfirmation()
    {
        if (Confirmation is null) throw new InvalidDataException("切换确认设置为空。");
        Confirmation.Validate();
        if (TrustedProfiles is null || TrustedProfiles.Count > 512 || TrustedProfiles.Any(k => k is null || k.Length != 67 || !k.StartsWith("v1:", StringComparison.Ordinal) || k[3..].Any(c => !Uri.IsHexDigit(c))))
            throw new InvalidDataException("预设信任记录格式不正确。");
    }
    public void Save(string? directory = null)
    {
        ConfigurationBackups.Save(directory ?? Program.DataDirectory, "preferences.json", JsonSerializer.Serialize(this, ProfileStore.Json), value => Parse(value));
    }
    // Always merge with the latest file: alias/hotkey saves must not erase confirmation policy or trust.
    public static void SaveInterface(bool hotkeys, Dictionary<string, string> aliases, string? directory = null) =>
        (Load(directory) with { Hotkeys = hotkeys, Aliases = new(aliases, StringComparer.OrdinalIgnoreCase) }).Save(directory);
    public static void Remember(string key, string? directory = null)
    {
        var current = Load(directory);
        (current with { TrustedProfiles = current.TrustedProfiles.Where(k => k != key).TakeLast(511).Append(key).ToList() }).Save(directory);
    }
}
