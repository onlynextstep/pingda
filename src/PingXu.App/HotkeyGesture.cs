using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Input;
using PingXu.Core;

namespace PingXu.App;

public record HotkeyGesture(uint Modifiers, uint Key)
{
    [JsonIgnore]
    public string Label => string.Join(" + ", new[] {
        (Modifiers & 2) != 0 ? "Ctrl" : null, (Modifiers & 1) != 0 ? "Alt" : null,
        (Modifiers & 4) != 0 ? "Shift" : null,
        Key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A ? ((char)Key).ToString() :
        Key is >= 0x60 and <= 0x69 ? "Num " + (Key - 0x60) : "F" + (Key - 0x6F)
    }.Where(x => x != null));

    public void Validate()
    {
        if ((Modifiers & ~7u) != 0 || (Modifiers & 3) == 0)
            throw new InvalidDataException("请使用 Ctrl 或 Alt，可同时搭配 Shift。");
        if (!(Key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x60 and <= 0x69 or >= 0x70 and <= 0x7A))
            throw new InvalidDataException("请搭配字母、数字或 F1～F11；不使用系统保留组合。");
        if (Key == 0x73 && (Modifiers & 1) != 0)
            throw new InvalidDataException("Alt + F4 用于关闭窗口，请换一个组合。");
    }

    public static HotkeyGesture FromInput(System.Windows.Input.Key key, ModifierKeys mods)
    {
        uint modifiers = ((mods & ModifierKeys.Control) != 0 ? 2u : 0) |
            ((mods & ModifierKeys.Alt) != 0 ? 1u : 0) | ((mods & ModifierKeys.Shift) != 0 ? 4u : 0) |
            ((mods & ModifierKeys.Windows) != 0 ? 8u : 0);
        var value = new HotkeyGesture(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        value.Validate(); return value;
    }
}

public static class ShortcutBindings
{
    public static void Validate(IReadOnlyDictionary<string, HotkeyGesture> bindings)
    {
        if (bindings.Count > 128) throw new InvalidDataException("最多设置 128 个预设快捷键。");
        var used = new HashSet<HotkeyGesture>();
        foreach (var (id, key) in bindings)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || key == null)
                throw new InvalidDataException("预设快捷键格式不正确。");
            key.Validate();
            if (!used.Add(key)) throw new InvalidDataException($"{key.Label} 重复了，每个组合只能绑定一个预设。");
        }
    }

    // null means legacy defaults; an explicitly empty dictionary means the user cleared all bindings.
    public static Dictionary<string, HotkeyGesture> Resolve(IReadOnlyDictionary<string, HotkeyGesture>? saved,
        IReadOnlyList<DisplayProfile> profiles)
    {
        if (saved != null) Validate(saved);
        return saved == null
            ? profiles.Take(5).Select((p, i) => (p.Id, Key: new HotkeyGesture(3, (uint)(0x31 + i)))).ToDictionary(x => x.Id, x => x.Key)
            : profiles.Where(p => saved.ContainsKey(p.Id)).ToDictionary(p => p.Id, p => saved[p.Id]);
    }
}

/// <summary>Acquire a complete set or release every successful registration. No configuration writes.</summary>
public static class HotkeyRegistration
{
    public static Dictionary<int, string> Register(IReadOnlyDictionary<string, HotkeyGesture> bindings,
        Func<int> nextId, Func<int, HotkeyGesture, bool> register, Action<int> unregister)
    {
        ShortcutBindings.Validate(bindings);
        var acquired = new Dictionary<int, string>();
        try
        {
            foreach (var (profileId, key) in bindings)
            {
                int id = nextId();
                if (!register(id, key)) throw new InvalidOperationException($"{key.Label} 已被占用或系统不允许，请换一个组合。");
                acquired.Add(id, profileId);
            }
            return acquired;
        }
        catch
        {
            foreach (int id in acquired.Keys) unregister(id);
            throw;
        }
    }
}
