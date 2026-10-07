namespace PingXu.Core;

/// <summary>
/// One immutable drag snapshot. Deltas are canvas DIPs at the scale captured by Start;
/// Preview is unrestricted by collisions, Drop chooses the nearest non-overlapping edge.
/// Only enabled coordinates change; identity, modes, primary and disabled records survive.
/// No hardware, validation service, persistence or mutation of the caller's list occurs.
/// </summary>
public sealed class LayoutDrag
{
    private const int Limit = 100000; // Same desktop coordinate bound as LayoutPlanner.Check.
    private readonly DisplayProfile original;
    private readonly DisplayTarget moving;
    private readonly DisplayTarget[] others;
    private readonly double scale;
    private readonly long minX, maxX, minY, maxY;

    private LayoutDrag(DisplayProfile profile, DisplayTarget target, double pixelsToDip)
    {
        original = profile with { Displays = profile.Displays.ToList() };
        moving = target;
        others = original.Displays.Where(d => d.Enabled && !Same(d.Id, target.Id)).ToArray();
        scale = pixelsToDip;
        minX = minY = -Limit;
        maxX = maxY = Limit;
        if (moving.Primary && others.Length > 0)
        {
            // Rebase the other active screens around the moved primary without overflowing.
            minX = others.Max(d => (long)d.X - Limit);
            maxX = others.Min(d => (long)d.X + Limit);
            minY = others.Max(d => (long)d.Y - Limit);
            maxY = others.Min(d => (long)d.Y + Limit);
        }
    }

    /// <summary>Returns null for a non-editable shape. Does not invent/enable a primary or remove screens.</summary>
    public static LayoutDrag? Start(DisplayProfile? profile, string id, double pixelsToDip)
    {
        if (profile?.Displays is not { Count: > 0 and <= 32 } all ||
            !double.IsFinite(pixelsToDip) || pixelsToDip <= 0 ||
            all.Any(d => d is null || string.IsNullOrWhiteSpace(d.Id)) ||
            all.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != all.Count)
            return null;
        var active = all.Where(d => d.Enabled).ToArray();
        if (active.Count(d => d.Primary) != 1 || all.Any(d => d.Primary && !d.Enabled) ||
            active.Any(d => d.Width <= 0 || d.Height <= 0 || d.Width > 32768 || d.Height > 32768 ||
                Math.Abs((long)d.X) > Limit || Math.Abs((long)d.Y) > Limit ||
                (d.Primary && (d.X != 0 || d.Y != 0)))) return null;
        var target = active.SingleOrDefault(d => Same(d.Id, id));
        if (target is null) return null;
        var fixedScreens = active.Where(d => !Same(d.Id, id)).ToArray();
        // A single-screen drag cannot repair a collision between two stationary screens.
        for (int i = 0; i < fixedScreens.Length; i++)
        for (int j = i + 1; j < fixedScreens.Length; j++)
            if (Overlaps(fixedScreens[i].X, fixedScreens[i].Y, fixedScreens[i], fixedScreens[j])) return null;
        return new LayoutDrag(profile, target, pixelsToDip);
    }

    /// <summary>Unsnapped preview; a primary move rebases all enabled coordinates to keep primary at (0,0).</summary>
    public DisplayProfile Preview(double deltaX, double deltaY)
    {
        var (x, y) = Position(deltaX, deltaY);
        return At(x, y);
    }

    /// <summary>
    /// Exact nearest squared-distance projection onto legal integer edge segments (at least one pixel
    /// of shared edge). Obstacles split those segments; no incremental collision clamp can trap a drag.
    /// Existing stationary gaps/components are preserved. A single screen remains at the origin.
    /// </summary>
    public DisplayProfile Drop(double deltaX, double deltaY)
    {
        var (desiredX, desiredY) = Position(deltaX, deltaY);
        if (others.Length == 0) return At(0, 0);
        (long X, long Y)? best = null;
        double bestDistance = double.PositiveInfinity;
        foreach (var anchor in others.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
        {
            Edge(true, (long)anchor.X - moving.Width, (long)anchor.Y - moving.Height + 1, (long)anchor.Y + anchor.Height - 1);
            Edge(true, (long)anchor.X + anchor.Width, (long)anchor.Y - moving.Height + 1, (long)anchor.Y + anchor.Height - 1);
            Edge(false, (long)anchor.Y - moving.Height, (long)anchor.X - moving.Width + 1, (long)anchor.X + anchor.Width - 1);
            Edge(false, (long)anchor.Y + anchor.Height, (long)anchor.X - moving.Width + 1, (long)anchor.X + anchor.Width - 1);
        }
        return best is { } point ? At(point.X, point.Y) : original with { Displays = original.Displays.ToList() };

        void Edge(bool vertical, long fixedValue, long low, long high)
        {
            if (fixedValue < (vertical ? minX : minY) || fixedValue > (vertical ? maxX : maxY)) return;
            low = Math.Max(low, vertical ? minY : minX);
            high = Math.Min(high, vertical ? maxY : maxX);
            if (low > high) return;
            var intervals = new List<(long Low, long High)> { (low, high) };
            foreach (var obstacle in others)
            {
                long start = vertical ? obstacle.X : obstacle.Y;
                long size = vertical ? obstacle.Width : obstacle.Height;
                long movingSize = vertical ? moving.Width : moving.Height;
                if (fixedValue >= start + size || fixedValue + movingSize <= start) continue;
                long blockedLow = (vertical ? (long)obstacle.Y - moving.Height : (long)obstacle.X - moving.Width) + 1;
                long blockedHigh = (vertical ? (long)obstacle.Y + obstacle.Height : (long)obstacle.X + obstacle.Width) - 1;
                var remaining = new List<(long Low, long High)>();
                foreach (var interval in intervals)
                {
                    if (blockedHigh < interval.Low || blockedLow > interval.High) remaining.Add(interval);
                    else
                    {
                        if (interval.Low < blockedLow) remaining.Add((interval.Low, blockedLow - 1));
                        if (interval.High > blockedHigh) remaining.Add((blockedHigh + 1, interval.High));
                    }
                }
                intervals = remaining;
                if (intervals.Count == 0) return;
            }
            foreach (var interval in intervals)
            {
                long variable = Math.Clamp(vertical ? desiredY : desiredX, interval.Low, interval.High);
                long x = vertical ? fixedValue : variable, y = vertical ? variable : fixedValue;
                double dx = x - desiredX, dy = y - desiredY;
                double distance = dx * dx + dy * dy;
                if (distance < bestDistance || (distance == bestDistance && best is { } tie &&
                    (x < tie.X || (x == tie.X && y < tie.Y))))
                { best = (x, y); bestDistance = distance; }
            }
        }
    }

    private (long X, long Y) Position(double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return (moving.X, moving.Y);
        return ((long)Math.Round(Math.Clamp(moving.X + dx / scale, minX, maxX), MidpointRounding.AwayFromZero),
            (long)Math.Round(Math.Clamp(moving.Y + dy / scale, minY, maxY), MidpointRounding.AwayFromZero));
    }

    private DisplayProfile At(long x, long y)
    {
        long originX = moving.Primary ? x : 0, originY = moving.Primary ? y : 0;
        return original with { Displays = original.Displays.Select(d => !d.Enabled ? d : d with
        {
            X = checked((int)((Same(d.Id, moving.Id) ? x : d.X) - originX)),
            Y = checked((int)((Same(d.Id, moving.Id) ? y : d.Y) - originY))
        }).ToList() };
    }

    private static bool Overlaps(long x, long y, DisplayTarget a, DisplayTarget b) =>
        x < (long)b.X + b.Width && x + a.Width > b.X && y < (long)b.Y + b.Height && y + a.Height > b.Y;
    private static bool Same(string a, string b) => StringComparer.OrdinalIgnoreCase.Equals(a, b);
}
