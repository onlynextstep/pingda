using PingXu.Core;

namespace PingXu.App;

/// <summary>View labels are derived from measured devices, never persisted as user aliases.</summary>
public static class DisplayPresentation
{
    public static Dictionary<string, int> Numbers(IEnumerable<DisplayInfo> hardware, IEnumerable<DisplayTarget>? targets = null)
    {
        var observed = hardware.ToArray();
        var connected = observed.Where(d => d.Connected).Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return observed.Select(d => d.Id).Concat(targets?.Select(d => d.Id) ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => connected.Contains(id) ? 0 : 1).ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select((id, i) => (id, number: i + 1)).ToDictionary(x => x.id, x => x.number, StringComparer.OrdinalIgnoreCase);
    }

    public static Dictionary<string, string> Labels(IReadOnlyList<DisplayInfo> hardware,
        IReadOnlyDictionary<string, string> aliases, IReadOnlyDictionary<string, int> numbers)
    {
        var names = hardware.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Clean(g.First().Name), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, number) in numbers)
        {
            // Case-insensitive even for dictionaries supplied by JSON or callers using a different comparer.
            var custom = aliases.FirstOrDefault(k => StringComparer.OrdinalIgnoreCase.Equals(k.Key, id)).Value;
            if (!string.IsNullOrWhiteSpace(custom)) { result[id] = custom; continue; }
            var model = names.GetValueOrDefault(id, "");
            result[id] = model.Length == 0 ? $"显示器 {number:00}"
                : names.Values.Count(n => StringComparer.OrdinalIgnoreCase.Equals(n, model)) > 1 ? $"{model} · {number:00}" : model;
        }
        return result;
    }

    static string Clean(string? name) => string.IsNullOrWhiteSpace(name) ? "" : string.Join(" ", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static bool SameLayout(IEnumerable<DisplayTarget> left, IEnumerable<DisplayTarget> right)
    {
        var a = left.Where(d => d.Enabled).OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        var b = right.Where(d => d.Enabled).OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        return a.Length == b.Length && a.Zip(b).All(p => StringComparer.OrdinalIgnoreCase.Equals(p.First.Id, p.Second.Id)
            && p.First == p.Second with { Id = p.First.Id });
    }
}
