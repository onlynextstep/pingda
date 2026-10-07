namespace PingXu.Core;

/// <summary>
/// Pure first-run defaults from a measured extended desktop. No storage, platform API or machine-specific rules.
/// Call only when the profile file does not exist; an existing empty library is a user's choice, not first run.
/// Never merge these defaults into or replace an existing library automatically.
/// </summary>
public static class DefaultProfileFactory
{
    /// <summary>
    /// First result preserves every observed target field (including inactive entries) and exact active positions.
    /// For multiple active screens, additionally derive one single-screen candidate per currently active device,
    /// retaining its observed rotated dimensions, angle and integer Hz; only its position becomes primary origin.
    /// Does not assume 4K, model, count=3, landscape, or a refresh rate; never enables a currently inactive device.
    /// Names are generic labels, not Windows display numbers; the caller may relabel generated profiles.
    /// Every result passes Core structural checks, but candidates still need native Validate/guardian before Apply.
    /// No rotation candidates are generated: the model does not advertise supported rotation/target timing pairs.
    /// Empty/ambiguous/contradictory/non-extended observations throw ArgumentException without saving anything.
    /// More than DisplayLimits.MaximumConnected connected devices also throw, even if most are disabled.
    /// Disconnected history does not consume this limit. Over-limit input is never truncated into defaults.
    /// Fresh profile IDs are generated per call; input records and lists are never mutated.
    /// </summary>
    public static List<DisplayProfile> Create(IReadOnlyList<DisplayInfo> hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        var observed = hardware.ToArray();
        LayoutPlanner.CheckObserved(observed);
        var current = observed.Select(LayoutPlanner.ToTarget).ToList();
        var profiles = new List<DisplayProfile> { New("当前实际布局", current) };
        var active = observed.Where(d => d.Enabled).OrderByDescending(d => d.Primary)
            .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        if (active.Length == 1) return profiles;

        for (var i = 0; i < active.Length; i++)
        {
            var selected = active[i];
            var targets = current.Select(t => StringComparer.OrdinalIgnoreCase.Equals(t.Id, selected.Id)
                ? t with { Primary = true, X = 0, Y = 0 }
                : t with { Enabled = false, Primary = false }).ToList();
            var profile = New(selected.Primary ? "仅当前主屏" : $"单屏方案 {i + 1}", targets);
            LayoutPlanner.Check(profile, observed);
            profiles.Add(profile);
        }
        return profiles;
    }

    private static DisplayProfile New(string name, List<DisplayTarget> targets) => new(Guid.NewGuid().ToString("N"), name, targets);
}
