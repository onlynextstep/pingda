using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PingXu.Core;
using Canvas = System.Windows.Controls.Canvas;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;

namespace PingXu.App;

/// <summary>
/// Attach once to the persistent outer canvas. Redrawing its children is safe and cancels a pending drag.
/// commitDraft must only replace the in-memory draft (and may render); it is called once on changed drop.
/// select runs on mouse-up after capture/preview cleanup, so its render cannot destroy an active gesture.
/// Dispose when replacing the canvas/closing its owner. All methods run on the canvas dispatcher.
/// </summary>
public sealed class StudioDragController : IDisposable
{
    private static readonly ConditionalWeakTable<Canvas, StudioDragController> Attached = new();
    private readonly Canvas canvas;
    private readonly Func<DisplayProfile?> getDraft;
    private readonly Func<bool> canEdit;
    private readonly Action<DisplayProfile> commitDraft;
    private readonly Action<string> select;
    private readonly Action<string>? status;
    private readonly IStudioDragCapture capture;
    private readonly bool wasFocusable;
    private LayoutDrag? drag;
    private DisplayProfile? before;
    private StudioDragMapping? mapping;
    private StudioDragVisual? visual;
    private Point start;
    private bool dragging, disposed;
    private readonly List<(FrameworkElement Element, Transform Transform, int Z)> saved = new();

    public static StudioDragController Attach(Canvas canvas, Func<DisplayProfile?> getDraft,
        Func<bool> canEdit, Action<DisplayProfile> commitDraft, Action<string> select,
        Action<string>? status = null) => new(canvas, getDraft, canEdit, commitDraft, select, status, new MouseCaptureAdapter());

    internal StudioDragController(Canvas canvas, Func<DisplayProfile?> getDraft, Func<bool> canEdit,
        Action<DisplayProfile> commitDraft, Action<string> select, Action<string>? status, IStudioDragCapture capture)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(getDraft);
        ArgumentNullException.ThrowIfNull(canEdit);
        ArgumentNullException.ThrowIfNull(commitDraft);
        ArgumentNullException.ThrowIfNull(select);
        this.canvas = canvas; this.getDraft = getDraft; this.canEdit = canEdit;
        this.commitDraft = commitDraft; this.select = select; this.status = status; this.capture = capture;
        canvas.VerifyAccess();
        if (Attached.TryGetValue(canvas, out var previous)) previous.Dispose();
        Attached.Add(canvas, this);
        wasFocusable = canvas.Focusable;
        canvas.Focusable = true;
        canvas.PreviewMouseLeftButtonDown += MouseDown;
        canvas.PreviewMouseMove += MouseMove;
        canvas.PreviewMouseLeftButtonUp += MouseUp;
        canvas.PreviewKeyDown += KeyDown;
        canvas.LostMouseCapture += LostCapture;
        canvas.MouseLeave += MouseLeave;
        canvas.SizeChanged += SizeChanged;
        canvas.Unloaded += Unloaded;
        canvas.LayoutUpdated += LayoutUpdated;
        canvas.AddHandler(StudioDrawing.LayoutRedrawingEvent, new RoutedEventHandler(Redrawing));
    }

    private void MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (PointerDown(e.GetPosition(canvas)))
        {
            // Suppress Button's own mouse capture/click; keyboard activation still uses its Click handler.
            e.Handled = true;
            canvas.Focus();
        }
    }
    private void MouseMove(object sender, MouseEventArgs e)
    { if (drag != null) { PointerMove(e.GetPosition(canvas), e.LeftButton == MouseButtonState.Pressed); e.Handled = true; } }
    private void MouseUp(object sender, MouseButtonEventArgs e)
    { if (drag != null) { PointerUp(e.GetPosition(canvas)); e.Handled = true; } }
    private void KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Escape && drag != null) { Cancel(); e.Handled = true; } }
    private void LostCapture(object sender, MouseEventArgs e)
    { if (dragging && !capture.IsCaptured(canvas)) Cancel(); }
    private void MouseLeave(object sender, MouseEventArgs e) { if (!dragging) Cancel(); }
    private void SizeChanged(object sender, SizeChangedEventArgs e) => Cancel();
    private void Unloaded(object sender, RoutedEventArgs e) => Cancel();
    private void Redrawing(object sender, RoutedEventArgs e) => Cancel();
    private void LayoutUpdated(object? sender, EventArgs e) { if (drag != null && !StillCurrent()) Cancel(); }

    // The same event seam is exercised by isolated tests with a fake capture adapter; no OS mouse is moved.
    internal bool PointerDown(Point point)
    {
        canvas.VerifyAccess();
        Cancel();
        if (disposed || !canEdit()) return false;
        var currentMapping = StudioDrawing.GetDragMapping(canvas);
        var hit = currentMapping?.Displays.LastOrDefault(d => d.Bounds.Contains(point));
        var profile = getDraft();
        if (hit == null || currentMapping == null || profile == null) return false;
        var session = LayoutDrag.Start(profile, hit.Id, currentMapping.Scale);
        if (session == null) return false;
        var target = profile.Displays.Single(d => StringComparer.OrdinalIgnoreCase.Equals(d.Id, hit.Id));
        var expected = new Rect(currentMapping.OriginX + target.X * currentMapping.Scale,
            currentMapping.OriginY + target.Y * currentMapping.Scale,
            target.Width * currentMapping.Scale, target.Height * currentMapping.Scale);
        if (expected != hit.Bounds) // Ignore rounding below; stale model/mapping must never move the wrong screen.
        {
            if (Math.Abs(expected.X - hit.Bounds.X) > .001 || Math.Abs(expected.Y - hit.Bounds.Y) > .001 ||
                Math.Abs(expected.Width - hit.Bounds.Width) > .001 || Math.Abs(expected.Height - hit.Bounds.Height) > .001) return false;
        }
        before = profile with { Displays = profile.Displays.ToList() };
        drag = session; mapping = currentMapping; visual = hit; start = point;
        return true;
    }

    internal void PointerMove(Point point, bool leftPressed)
    {
        if (drag == null) return;
        if (!leftPressed || !StillCurrent() || !double.IsFinite(point.X) || !double.IsFinite(point.Y)) { Cancel(); return; }
        var delta = point - start;
        if (!dragging)
        {
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (!capture.Capture(canvas)) { Cancel(); return; }
            dragging = true;
            foreach (var element in visual!.Elements)
            {
                saved.Add((element, element.RenderTransform, Panel.GetZIndex(element)));
                Panel.SetZIndex(element, int.MaxValue);
            }
        }
        if (!capture.IsCaptured(canvas)) { Cancel(); return; }
        // Pure visual translation leaves the draft untouched and the original world transform fixed.
        // Tiny viewports have an outer shrink, so convert root-canvas DIPs into surface DIPs exactly once.
        foreach (var item in saved)
        {
            var transform = new TransformGroup();
            transform.Children.Add(item.Transform);
            transform.Children.Add(new TranslateTransform(delta.X / mapping!.SurfaceScale, delta.Y / mapping.SurfaceScale));
            item.Element.RenderTransform = transform;
        }
    }

    internal void PointerUp(Point point)
    {
        if (drag == null) return;
        if (!StillCurrent() || !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            (dragging && !capture.IsCaptured(canvas))) { Cancel(); return; }
        string id = visual!.Id;
        var result = dragging ? drag.Drop(point.X - start.X, point.Y - start.Y) : null;
        bool changed = result != null && !result.Displays.SequenceEqual(before!.Displays);
        // Releasing capture raises LostMouseCapture synchronously. Clear state BEFORE callbacks/release.
        Cancel();
        if (changed) commitDraft(result!);
        select(id);
    }

    private bool StillCurrent()
    {
        var current = getDraft();
        return !disposed && canEdit() && current != null && before != null &&
            current.Id == before.Id && current.Name == before.Name && current.Displays.SequenceEqual(before.Displays) &&
            ReferenceEquals(mapping, StudioDrawing.GetDragMapping(canvas)) &&
            visual!.Elements.All(e => e.IsDescendantOf(canvas));
    }

    /// <summary>Cancel pending/active interaction without any commit or selection callback.</summary>
    public void Cancel()
    {
        canvas.VerifyAccess();
        bool release = dragging;
        drag = null; before = null; visual = null; mapping = null; dragging = false;
        foreach (var item in saved)
        { item.Element.RenderTransform = item.Transform; Panel.SetZIndex(item.Element, item.Z); }
        saved.Clear();
        if (release && capture.IsCaptured(canvas)) capture.Release(canvas);
    }

    public void Dispose()
    {
        canvas.VerifyAccess();
        if (disposed) return;
        Cancel(); disposed = true;
        canvas.PreviewMouseLeftButtonDown -= MouseDown;
        canvas.PreviewMouseMove -= MouseMove;
        canvas.PreviewMouseLeftButtonUp -= MouseUp;
        canvas.PreviewKeyDown -= KeyDown;
        canvas.LostMouseCapture -= LostCapture;
        canvas.MouseLeave -= MouseLeave;
        canvas.SizeChanged -= SizeChanged;
        canvas.Unloaded -= Unloaded;
        canvas.LayoutUpdated -= LayoutUpdated;
        canvas.RemoveHandler(StudioDrawing.LayoutRedrawingEvent, new RoutedEventHandler(Redrawing));
        canvas.Focusable = wasFocusable;
        Attached.Remove(canvas);
    }
}

internal interface IStudioDragCapture
{
    bool Capture(Canvas canvas);
    bool IsCaptured(Canvas canvas);
    void Release(Canvas canvas);
}

internal sealed class MouseCaptureAdapter : IStudioDragCapture
{
    public bool Capture(Canvas canvas) => Mouse.Capture(canvas, CaptureMode.Element);
    public bool IsCaptured(Canvas canvas) => ReferenceEquals(Mouse.Captured, canvas);
    public void Release(Canvas canvas) { if (IsCaptured(canvas)) canvas.ReleaseMouseCapture(); }
}
