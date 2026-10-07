using System.Windows;
using System.Windows.Controls;
using PingXu.Core;
using MessageBox = System.Windows.MessageBox;

namespace PingXu.App;

public partial class MainWindow
{
    StudioDragController? studioDrag;
    string? editingPresetId;
    string? pendingSceneId;
    bool creatingPreset;
    bool selectingPreset;
    int editorRevision;
    // The live adapter is ApplyProfile; isolated UI tests inject a recording adapter.
    readonly Func<DisplayProfile, Task>? switchLayout;

    void AttachWorkspace()
    {
        studioDrag = StudioDragController.Attach(LayoutCanvas, () => draft,
            () => !busy && DisplayCapacitySupported && !recoveryBlocked,
            p => { draft = p; MarkDraftChanged(); },
            id => { selected = id; PopulateInspector(); RenderCanvas(); }, Status);
        Closed += (_, _) => studioDrag?.Dispose();
    }

    void MarkDraftChanged()
    {
        dirty = true;
        UpdateWorkspaceActions();
    }

    void UpdateWorkspaceActions()
    {
        if (WorkspaceTitle == null) return;
        bool editing = editingPresetId != null;
        ManagePresetsButton.Content = selectingPreset || editing ? "退出编辑" : "编辑预设";
        ManagePresetsButton.IsEnabled = !busy && profilesReady;
        PresetShelfLabel.Text = selectingPreset ? "选择要修改的预设" : editing ? "编辑模式 · 点击预设只载入，不切屏" : "预设";
        PresetMoreButton.Visibility = editing && !creatingPreset ? Visibility.Visible : Visibility.Collapsed;
        PresetMoreButton.IsEnabled = !busy;
        SavePresetButton.Content = creatingPreset ? "保存预设" : "保存修改";
        WorkspaceTitle.Text = editing ? (creatingPreset ? "新建预设" : "编辑预设") : "工作空间";
        PreviewTag.Text = editing ? "编辑预设" : dirty ? "有未应用的修改" : "当前布局";
        PresetNameRow.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        SavePresetButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        SavePresetButton.IsEnabled = !busy && profilesReady && DisplayCapacitySupported;
        CancelEditButton.Visibility = editing || selectingPreset || dirty ? Visibility.Visible : Visibility.Collapsed;
        CancelEditButton.IsEnabled = !busy;
        CancelEditButton.Content = selectingPreset ? "退出编辑" : editing ? "取消" : "取消修改";
        ApplyButton.Visibility = !editing ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.IsEnabled = !editing && dirty && !busy && !recoveryBlocked && DisplayCapacitySupported;
        ApplyButton.ToolTip = dirty ? "将工作空间中的布局应用到显示器" : "当前没有布局改动；拖动屏幕或调整右侧设置后可应用";
        SaveAsButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        SaveAsButton.IsEnabled = NewPresetButton.IsEnabled = !busy && DisplayCapacitySupported && profilesReady;
        InspectorHint.Text = editing ? "保存预设不会立即切换屏幕。" : "修改后点击“应用布局”。";
        Subtitle.Text = editing ? "设置布局后保存预设。" : "拖动屏幕调整位置。";
        if (snapshot != null && !DisplayCapacitySupported)
            Subtitle.Text = $"检测到 {snapshot.Displays.Count(d => d.Connected)} 块屏幕，最多支持 {DisplayLimits.MaximumConnected} 块。当前仅查看。";
        if (!editing && draft != null && snapshot != null)
            SceneTitle.Text = dirty ? "有未应用的修改" : profiles.FirstOrDefault(p => SameLayout(p.Displays, snapshot.Displays.Select(ToTarget)))?.Name ?? "当前布局";
    }

    bool CanLeaveEditor()
    {
        if (busy) return false;
        return !dirty || Dialogs.ConfirmDiscard(this);
    }

    void ShowActualWorkspace()
    {
        studioDrag?.Cancel(); editingPresetId = null; creatingPreset = false; selectingPreset = false; dirty = false;
        if (snapshot == null) return;
        draft = new("current", "当前布局", snapshot.Displays.Select(ToTarget).ToList());
        if (!draft.Displays.Any(d => d.Id == selected)) selected = draft.Displays.FirstOrDefault(d => d.Enabled)?.Id;
        UpdateWorkspaceActions(); RenderCanvas(); PopulateInspector();
    }

    void NewPreset_Click(object sender, RoutedEventArgs e) => BeginNewPreset();
    void EnterPresetSelection()
    {
        if (busy || !profilesReady || !DisplayCapacitySupported || !CanLeaveEditor()) return;
        var current = snapshot == null ? null : profiles.FirstOrDefault(p =>
            SameLayout(p.Displays, snapshot.Displays.Select(ToTarget)));
        ShowActualWorkspace();
        if (current != null) { BeginEditPreset(current); return; }
        // An unsaved/custom system layout has no current preset to overwrite.
        selectingPreset = true;
        UpdateWorkspaceActions(); RenderScenes();
    }

    async Task ActivateSceneCard(DisplayProfile profile)
    {
        if (selectingPreset || editingPresetId != null) BeginEditPreset(profile);
        else await SwitchScene(profile);
    }

    void PresetMore_Click(object sender, RoutedEventArgs e)
    {
        if (busy || creatingPreset || editingPresetId == null) return;
        var profile = profiles.FirstOrDefault(p => p.Id == editingPresetId);
        if (profile == null) return;
        var menu = new ContextMenu { PlacementTarget = PresetMoreButton };
        var copy = new MenuItem { Header = "复制为新预设" };
        copy.Click += (_, _) =>
        {
            if (busy || draft == null) return;
            var basis = draft with { Name = PresetNameBox.Text.Trim() };
            creatingPreset = true; editingPresetId = Guid.NewGuid().ToString("N");
            draft = basis with { Id = editingPresetId };
            PresetNameBox.Text = basis.Name[..Math.Min(basis.Name.Length, 55)] + " · 副本";
            dirty = true; UpdateWorkspaceActions(); RenderScenes();
        };
        var delete = new MenuItem { Header = "删除预设" };
        delete.Click += (_, _) => DeletePreset(profile);
        menu.Items.Add(copy); menu.Items.Add(delete); menu.IsOpen = true;
    }
    void BeginNewPreset() => BeginPreset(null, false);
    string DefaultPresetName()
    {
        for (int n = 1; ; n++)
        {
            var name = n == 1 ? "新预设" : $"新预设 {n}";
            if (!profiles.Any(p => StringComparer.OrdinalIgnoreCase.Equals(p.Name, name))) return name;
        }
    }
    void BeginEditPreset(DisplayProfile profile) => BeginPreset(profile, false);
    void BeginPreset(DisplayProfile? profile, bool fromDraft)
    {
        if (busy || snapshot == null || !DisplayCapacitySupported || !profilesReady || (!fromDraft && !CanLeaveEditor())) return;
        if (profile == null && profiles.Count >= 100) { Status("最多保存100个预设，请先删除不需要的预设。"); return; }
        studioDrag?.Cancel();
        selectingPreset = false;
        var basis = profile ?? (fromDraft ? draft : null) ?? new("current", "当前布局", snapshot.Displays.Select(ToTarget).ToList());
        creatingPreset = profile == null; editingPresetId = profile?.Id ?? Guid.NewGuid().ToString("N"); editorRevision = libraryRevision;
        SelectProfile(basis with { Id = editingPresetId, Name = profile?.Name ?? DefaultPresetName() });
        dirty = creatingPreset; PresetNameBox.Text = draft!.Name;
        SceneTitle.Text = creatingPreset ? "新建预设" : "编辑 · " + draft.Name;
        UpdateWorkspaceActions(); PopulateInspector(); RenderCanvas(); PresetNameBox.Focus(); PresetNameBox.SelectAll();
    }

    void PresetName_Changed(object sender, TextChangedEventArgs e)
    {
        if (editingPresetId != null && !busy && draft != null && PresetNameBox.Text != draft.Name) dirty = true;
    }

    async void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (busy || editingPresetId == null || draft == null || snapshot == null || !profilesReady) return;
        try
        {
            string name = PresetNameBox.Text.Trim();
            if (name.Length is < 1 or > 60) { Status("请填写 1–60 个字符的预设名称。"); PresetNameBox.Focus(); return; }
            var saved = draft with { Id = editingPresetId, Name = name, Displays = draft.Displays.ToList() };
            LayoutPlanner.Check(saved, snapshot.Displays);
            if (!creatingPreset && !profiles.Any(p => p.Id == editingPresetId)) throw new InvalidOperationException("此预设已被删除，请重新新建。");
            var updated = creatingPreset ? profiles.Append(saved).ToList() : profiles.Select(p => p.Id == saved.Id ? saved : p).ToList();
            CommitProfiles(updated, editorRevision); ShowActualWorkspace(); LibraryChanged(); StatusSuccess("预设已保存，屏幕布局未改变。");
            await RefreshPendingHardware(immediately: true);
        }
        catch (Exception ex) { Status("预设未保存：" + ex.Message); }
    }

    void DeletePreset(DisplayProfile profile)
    {
        if (busy || !profilesReady) return;
        int revision = libraryRevision;
        if (MessageBox.Show(this, $"删除预设“{profile.Name}”？\n只删除预设，不改变屏幕布局。", "删除预设", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { CommitProfiles(profiles.Where(p => p.Id != profile.Id).ToList(), revision); if (editingPresetId == profile.Id) ShowActualWorkspace(); LibraryChanged(); StatusSuccess("预设已删除。"); }
        catch (Exception ex) { Status(ex.Message); }
    }

    async void CancelEdit_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        ShowActualWorkspace(); StatusSuccess("已取消修改。");
        await RefreshPendingHardware(immediately: true);
    }

    async Task SwitchScene(DisplayProfile profile)
    {
        if (busy || pendingSceneId != null) return;
        if (recoveryBlocked) { Status("恢复状态未确认，请先在设置中检查恢复保护。"); return; }
        var target = profile with { Displays = profile.Displays.ToList() };
        pendingSceneId = target.Id;
        try
        {
            // The discard dialog pumps messages too; own the request before opening it.
            if (!CanLeaveEditor()) return;
            // Render the user's intent before the first asynchronous hardware read.
            RenderScenes();
            if (!await RefreshActualWorkspace()) return;
            if (target.Displays.Any(d => d.Enabled && !snapshot!.Displays.Any(live => live.Connected && StringComparer.OrdinalIgnoreCase.Equals(live.Id, d.Id))))
            { Status("此预设需要的屏幕未连接，请连接后刷新。"); return; }
            if (SameLayout(target.Displays, snapshot!.Displays.Select(ToTarget))) { StatusSuccess("已在使用此预设，无需重复切换。"); return; }
            if (switchLayout != null) await switchLayout(target); else await ApplyProfile(target);
        }
        finally
        {
            // Never infer success from a click: the badge and settled color use the readback.
            pendingSceneId = null;
            RenderScenes();
        }
    }

    void LibraryChanged()
    {
        RenderScenes(); RebuildTray(); UpdateWorkspaceActions();
        if (source != null) SetHotkeys(hotkeysEnabled);
    }
}
