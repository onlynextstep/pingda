using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PingXu.Core;

namespace PingXu.App.Tests;

internal sealed class WindowFixture : IDisposable
{
    public MainWindow Window { get; }
    public FakeDisplayService Service { get; } = new();
    public string ConstructorUsed { get; }
    public FrameworkElement Content { get; }
    private bool loaded;
    private bool sourceInitialized;

    public WindowFixture(string dataDirectory)
    {
        // Prefer the optional data-directory seam when the parent agent adds it.
        // Unknown mandatory parameters fail closed rather than selecting production defaults.
        var ctor = typeof(MainWindow).GetConstructors()
            .Where(c => c.GetParameters() is var p && p.Length > 0 && p[0].ParameterType == typeof(IDisplayService)
                && p.Skip(1).All(x => x.IsOptional || IsDataDirectory(x)))
            .OrderByDescending(c => c.GetParameters().Any(IsDataDirectory)).First();
        var args = ctor.GetParameters().Select((p, i) => i == 0 ? (object)Service
            : IsDataDirectory(p) ? dataDirectory : p.DefaultValue).ToArray();
        ConstructorUsed = ctor.ToString()!;
        Window = (MainWindow)ctor.Invoke(args);
        // Store is path-only; replacing it creates no directory and protects even guard regressions.
        Set("store", new ProfileStore(dataDirectory));
        Window.Loaded += (_, _) => loaded = true;
        Window.SourceInitialized += (_, _) => sourceInitialized = true;
        Get<DispatcherTimer>("hardwareTimer").Stop();
        Window.ApplyTemplate();
        Content = (FrameworkElement)Window.Content;

        // Keep the real XAML tree; carry Window's inherited typography over when detaching it.
        Content.SetValue(TextElement.FontFamilyProperty, Window.FontFamily);
        Content.SetValue(TextElement.FontSizeProperty, Window.FontSize);
        Content.SetValue(TextElement.ForegroundProperty, Window.Foreground);
        Window.Content = null;
        Reset();
    }

    private static bool IsDataDirectory(ParameterInfo p) => p.ParameterType == typeof(string)
        && (p.Name?.Contains("data", StringComparison.OrdinalIgnoreCase) == true)
        && (p.Name.Contains("dir", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("path", StringComparison.OrdinalIgnoreCase));

    public T Get<T>(string name) => (T)(typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(MainWindow).FullName, name)).GetValue(Window)!;

    public void Set(string name, object? value) => (typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(MainWindow).FullName, name)).SetValue(Window, value);

    public void RaiseHardwareTick(bool complete = true)
    {
        // Invoke the registered event only; never start the timer or dispatch Windows messages.
        if (Get<IDisplayService>("service") is not WorkflowDisplayService
            || Get<Func<DisplayProfile, Task>?>("switchLayout") == null)
            throw new InvalidOperationException("硬件事件测试必须注入假采集与记录切换边界");
        var timer = Get<DispatcherTimer>("hardwareTimer");
        var tick = typeof(DispatcherTimer).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(EventHandler));
        ((EventHandler?)tick.GetValue(timer))?.Invoke(timer, EventArgs.Empty);
        if (complete) (SynchronizationContext.Current as WorkflowCallbacks)?.Complete();
    }

    // Only Save/Manage rejection is allowed; the ApplyDraft rejection has a separate guarded async entry.
    private static readonly HashSet<string> Allowed = ["GenerateProfiles", "SelectProfile", "RenderScenes", "RenderCanvas",
        "PopulateInspector", "Rotation_Changed", "Mode_Changed", "Enabled_Click", "Primary_Click", "Position_Click", "Align_Click", "UpdateTarget", "ScreenNumber", "Save_Click", "Manage_Click", "CommitProfiles"];
    public object? Invoke(string name, params object?[] args)
    {
        if (!Allowed.Contains(name)) throw new InvalidOperationException($"不允许调用 {name}");
        if ((name is "Save_Click" or "Manage_Click") && Get<bool>("profilesReady"))
            throw new InvalidOperationException("Save/Manage 仅允许预设库未就绪的拒绝分支");
        return (typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainWindow).FullName, name)).Invoke(Window, args);
    }

    public async Task InvokeBlockedApplyAsync()
    {
        if (!Get<bool>("recoveryBlocked") || Get<bool>("busy") || Draft == null)
            throw new InvalidOperationException("仅允许 recoveryBlocked=true、busy=false、有效草稿的 ApplyDraft 拒绝分支");
        var method = typeof(MainWindow).GetMethod("ApplyDraft", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainWindow).FullName, "ApplyDraft");
        var task = (Task)method.Invoke(Window, null)!;
        // The guard must return before its first asynchronous I/O. Never pump a dispatcher to finish it.
        if (!task.IsCompleted) throw new Exception("恢复锁定未同步拒绝，禁止进入消息循环等待真实操作");
        await task.ConfigureAwait(false);
        AssertIsolated();
    }

    public DisplayProfile Draft => Get<DisplayProfile>("draft");
    public List<DisplayProfile> Profiles => Get<List<DisplayProfile>>("profiles");
    public DisplayTarget Left => Draft.Displays.Single(x => x.Id == FakeDisplayService.Left);

    public void Reset()
    {
        Set("busy", false);
        Set("lastSignal", DateTime.MinValue);
        Set("populating", false);
        Set("profilesReady", true);
        Set("recoveryBlocked", false);
        Set("libraryRevision", 1);
        Set("editingPresetId", null);
        Set("creatingPreset", false);
        Set("switchLayout", null);
        Set("dirty", false);
        var snapshot = FakeDisplayService.Snapshot();
        Set("snapshot", snapshot);
        var aliases = Get<Dictionary<string, string>>("aliases");
        aliases.Clear();
        aliases[FakeDisplayService.Left] = "左侧4K";
        aliases[FakeDisplayService.Middle] = "中间带鱼屏";
        aliases[FakeDisplayService.Right] = "右侧带鱼屏";
        // Five existing user-saved scenes are a UI fixture, NOT generic first-run product defaults.
        Set("profiles", new[] { ("双屏专注", 0, false, true), ("三屏协作", 0, false, false),
            ("三屏阅读", 90, false, false), ("仅4K横屏", 0, true, false), ("仅4K竖屏", 90, true, false) }
            .Select((p, i) => new DisplayProfile("fixture-" + i, p.Item1,
                LayoutPlanner.Arrange(snapshot.Displays, FakeDisplayService.Left, p.Item2, p.Item3, p.Item4))).ToList());
        var current = new DisplayProfile("fake-current", "模拟三屏布局", snapshot.Displays.Select(x =>
            new DisplayTarget(x.Id, x.Enabled, x.Primary, x.X, x.Y, x.Width, x.Height, x.Rotation, x.RefreshRate)).ToList());
        Invoke("SelectProfile", current);
        SelectMonitor(FakeDisplayService.Left);
        Set("dirty", false);
        Get<TextBlock>("ConnectionLabel").Text = "模拟硬件 · 3 台已连接 / 3 台已启用";
    }

    public void SelectMonitor(string id) { Set("selected", id); Invoke("PopulateInspector"); Invoke("RenderCanvas"); }
    public void Rotate(int index) => Get<ComboBox>("RotationBox").SelectedIndex = index;
    public void ToggleEnabled(bool enabled)
    {
        var toggle = Get<CheckBox>("EnabledToggle");
        toggle.IsChecked = enabled;
    }

    public void AssertIsolated()
    {
        if (loaded || sourceInitialized || Window.IsLoaded || Window.IsVisible || Content.IsLoaded
            || PresentationSource.FromVisual(Content) != null || new WindowInteropHelper(Window).Handle != IntPtr.Zero)
            throw new Exception("窗口被加载、显示或创建了 HWND");
        if (Get<object?>("tray") != null || Get<object?>("source") != null || Get<Dictionary<int, string>>("hotkeyTargets").Count != 0)
            throw new Exception("托盘/系统消息钩子/快捷键不应初始化");
        if (Get<DispatcherTimer>("hardwareTimer").IsEnabled) throw new Exception("硬件轮询未停止");
        if (Service.Calls.Count != 0) throw new Exception("意外服务调用：" + string.Join(", ", Service.Calls));
    }

    public string Render(string path, int width, int height, int rotation = 90)
    {
        Reset();
        Invoke("SelectProfile", Profiles.First(x => x.Displays.Count(d => d.Enabled) == 3
            && x.Displays.Single(d => d.Id == FakeDisplayService.Left).Rotation == rotation));
        SelectMonitor(FakeDisplayService.Left);
        Get<TextBlock>("ConnectionLabel").Text = "3 台在线（模拟）";
        Get<TextBlock>("Subtitle").Text = "假硬件 UI 冒烟截图 · 未读取或修改真实显示器";
        Get<TextBlock>("StatusLabel").Text = "仅验证 UI · 离屏渲染 · 假三屏数据";
        // A current-state screenshot must represent an actually matching fake snapshot, not a preview.
        var activeDraft = Draft;
        Set("snapshot", FakeDisplayService.Snapshot() with { Displays = FakeDisplayService.Snapshot().Displays.Select(d =>
        {
            var t = activeDraft.Displays.Single(t => t.Id == d.Id);
            return d with { Enabled = t.Enabled, Primary = t.Primary, X = t.X, Y = t.Y, Width = t.Width, Height = t.Height, Rotation = t.Rotation, RefreshRate = t.RefreshRate };
        }).ToList() });
        Set("dirty", false);
        var size = new Size(width, height);
        Content.Measure(size);
        Content.Arrange(new Rect(size));
        Content.UpdateLayout();
        Invoke("RenderCanvas");
        Content.Measure(size);
        Content.Arrange(new Rect(size));
        Content.UpdateLayout();
        var canvas = Get<Canvas>("LayoutCanvas");
        if (canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0 || VisualChecks.Descendants(canvas).OfType<Button>().Count(b => b.Tag is string) != 3)
            throw new Exception("真实布局画布没有生成三块显示器");
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen()) dc.DrawRectangle(Window.Background, null, new Rect(size));
        image.Render(background);
        image.Render(Content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(path)) encoder.Save(stream);
        // Decode the actual output: validates dimensions, not a look-alike mock UI.
        using var saved = File.OpenRead(path);
        var decoded = BitmapDecoder.Create(saved, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        if (decoded.PixelWidth != width || decoded.PixelHeight != height) throw new Exception("PNG 尺寸不匹配");
        var pixels = new byte[width * height * 4];
        image.CopyPixels(pixels, width * 4, 0);
        var colors = new HashSet<int>();
        int visibleSamples = 0, totalSamples = 0;
        for (int y = 0; y < height; y += 8)
        for (int x = 0; x < width; x += 8)
        {
            int offset = (y * width + x) * 4;
            totalSamples++;
            if (pixels[offset + 3] > 0 && Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2])) > 32)
                visibleSamples++;
            colors.Add(BitConverter.ToInt32(pixels, offset));
        }
        if (colors.Count < 8 || visibleSamples < totalSamples / 100)
            throw new Exception("截图疑似黑屏、透明或单色空白");
        AssertIsolated();
        var scene = Get<StackPanel>("SceneStrip");
        return $"{width}×{height} px / 96 DPI；软件离屏渲染；采样颜色 {colors.Count}；画布 {canvas.ActualWidth:F0}×{canvas.ActualHeight:F0}；真实屏幕按钮 3；场景卡片 {scene.Children.Count}；{VisualChecks.Background(this)}；{VisualChecks.Primary(this)}。";
    }

    public void Dispose()
    {
        Get<DispatcherTimer>("hardwareTimer").Stop();
        Set("exiting", true);
        // No Show, Close, Application.Run, dispatcher pumping, input synthesis, or production entrypoint.
    }
}
