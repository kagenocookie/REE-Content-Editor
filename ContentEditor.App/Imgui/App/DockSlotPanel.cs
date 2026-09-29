using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;

namespace ContentEditor.App;

/// <summary>
/// Persistent panel of the default layout that always keeps its own name. Windows that belong in the panel's slot don't get their own window,
/// they're shown as tabs inside the panel instead.
/// </summary>
public abstract class DockSlotPanel : IWindowHandler
{
    public abstract DockSlot Slot { get; }
    protected abstract string Title { get; }
    protected abstract string EmptyHint { get; }

    public abstract int FixedID { get; }
    public string HandlerName => Title;
    public bool HasUnsavedChanges => false;
    DockSlot IWindowHandler.DefaultDockSlot => Slot;

    private static readonly DockSlot[] HostingSlots = [DockSlot.Hierarchy, DockSlot.Inspector];

    private static IWindowHandler? requestedTab;
    private static bool focusRequested;

    protected UIContext context = null!;

    /// <summary>
    /// Whether the given window handler gets shown as a tab in one of the slot panels instead of its own window.
    /// </summary>
    public static bool IsHostedEditor(IWindowHandler? handler) => handler != null && handler is not DockSlotPanel && HostingSlots.Contains(handler.DefaultDockSlot);

    /// <summary>
    /// Selects the tab of the given hosted window and focuses the panel it's in.
    /// </summary>
    public static void Focus(IWindowHandler editor)
    {
        requestedTab = editor;
        focusRequested = true;
    }

    public void Init(UIContext context)
    {
        this.context = context;
    }

    public void OnWindow()
    {
        var window = EditorWindow.CurrentWindow;
        if (window == null) return;

        var editors = window.ActiveImguiWindows.Where(w => w.Handler != null && w.Handler.DefaultDockSlot == Slot && IsHostedEditor(w.Handler)).ToList();
        DockLayout.DockNextWindow(Slot);
        // no close button, the panel is always there
        var visible = ImGui.Begin($"{Title}###{GetType().Name}");
        DockLayout.TrackCurrentWindow(Slot);
        if (focusRequested && requestedTab?.DefaultDockSlot == Slot) {
            focusRequested = false;
            ImGui.SetWindowFocus();
        }
        var focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows | ImGuiFocusedFlags.NoPopupHierarchy);

        WindowData? selected = null;
        var document = window.ActiveViewportWindow?.Handler as IViewportDocument;
        if (!visible || document != null) {
            foreach (var editor in editors) editor.Context.StateBool = false;
        }
        if (!visible) {
        } else if (document != null) {
            // the active viewport window provides its own content, the regular tabs come back once the scene view (or anything else) is active again
            ImGui.PushID("##ViewportDocument");
            try {
                ShowDocument(document);
            } catch (Exception e) {
                Logger.Error(e, $"Error occurred in the {Title} panel");
            }
            ImGui.PopID();
        } else if (editors.Count == 0) {
            ImGui.SetCursorPosY((ImGui.GetWindowHeight() - ImGui.GetTextLineHeight()) / 2);
            using var _ = ImguiHelpers.OverrideStyleCol(ImGuiCol.Text, Colors.Faded);
            ImguiHelpers.TextCentered(EmptyHint);
        } else {
            selected = ShowTabs(window, editors, focused);
        }
        ImGui.End();

        OnTabsUpdated(window, editors, selected);
    }

    private WindowData? ShowTabs(EditorWindow window, List<WindowData> editors, bool focused)
    {
        WindowData? selected = null;
        if (!ImGui.BeginTabBar("##PanelTabs"u8, ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.FittingPolicyScroll)) {
            return null;
        }

        foreach (var editor in editors) {
            var handler = editor.Handler!;
            var flags = GetTabFlags(handler);
            if (requestedTab == handler) {
                flags |= ImGuiTabItemFlags.SetSelected;
                requestedTab = null;
            }

            var open = true;
            var isSelected = ImGui.BeginTabItem($"{GetTabLabel(handler)}###{handler.GetType().Name}{editor.ID}", ref open, flags);
            if (isSelected) {
                selected = editor;
                editor.Size = ImGui.GetWindowSize();
                editor.Position = ImGui.GetWindowPos();
                ImGui.PushID(editor.ID);
                try {
                    handler.OnIMGUI();
                } catch (Exception e) {
                    Logger.Error(e, $"Error occurred in window {editor.Name}");
                }
                ImGui.PopID();
                ImGui.EndTabItem();
            }
            // used for the window's hotkeys and the global close hotkey
            editor.Context.StateBool = isSelected && focused;

            if (!open) {
                window.CloseSubwindow(editor);
            }
        }
        ImGui.EndTabBar();
        return selected;
    }

    /// <summary>
    /// Draws the panel's content provided by the active viewport document.
    /// </summary>
    protected abstract void ShowDocument(IViewportDocument document);

    protected virtual string GetTabLabel(IWindowHandler handler) => handler.HandlerName;
    protected virtual ImGuiTabItemFlags GetTabFlags(IWindowHandler handler) => ImGuiTabItemFlags.None;

    /// <summary>
    /// Called every frame after the panel was drawn, with the currently selected tab (null if none or the panel isn't visible).
    /// </summary>
    protected virtual void OnTabsUpdated(EditorWindow window, List<WindowData> editors, WindowData? selected)
    {
    }

    /// <summary>
    /// Selects the tab of the given hosted window on the next frame without focusing the panel.
    /// </summary>
    protected static void RequestTab(IWindowHandler? handler) => requestedTab = handler;

    public void OnIMGUI()
    {
    }

    public bool RequestClose() => false;
}

/// <summary>
/// Hierarchy panel, shows scene and prefab editors as tabs. Selecting a tab also makes its scene the active one.
/// </summary>
public class HierarchyPanel : DockSlotPanel
{
    public override DockSlot Slot => DockSlot.Hierarchy;
    protected override string Title => Lang.Windows.Slot_Hierarchy.ToString();
    protected override string EmptyHint => Lang.Windows.Slot_HierarchyHint.ToString();
    public override int FixedID => -200;

    private WindowData? syncedTab;
    private Scene? lastActiveScene;

    protected override void ShowDocument(IViewportDocument document) => document.OnHierarchyIMGUI();

    protected override string GetTabLabel(IWindowHandler handler)
    {
        if (handler is FileEditor fileEditor) {
            var icon = AppIcons.GetIcon(fileEditor);
            return icon == '\0' ? $"{handler.HandlerName}: {fileEditor.Handle.Filename}" : $"{icon} {fileEditor.Handle.Filename}";
        }
        return handler.HandlerName;
    }

    protected override ImGuiTabItemFlags GetTabFlags(IWindowHandler handler)
        => handler.HasUnsavedChanges ? ImGuiTabItemFlags.UnsavedDocument : ImGuiTabItemFlags.None;

    /// <summary>
    /// Keeps the selected tab and the active scene in sync, in both directions.
    /// </summary>
    protected override void OnTabsUpdated(EditorWindow window, List<WindowData> editors, WindowData? selected)
    {
        var sceneManager = window.SceneManager;
        var active = sceneManager.ActiveMasterScene;
        if (active != lastActiveScene) {
            // the active scene got changed from elsewhere (e.g. the Scenes menu), show its hierarchy
            lastActiveScene = active;
            var match = active == null ? null : editors.FirstOrDefault(e => (e.Handler as ISceneEditor)?.GetScene()?.RootScene == active);
            if (match != null && match != selected) {
                RequestTab(match.Handler);
                syncedTab = match;
            }
            return;
        }

        if (selected == null || selected == syncedTab) return;

        // scenes get loaded lazily the first time their editor is drawn, so wait until it's there
        var root = (selected.Handler as ISceneEditor)?.GetScene()?.RootScene;
        if (root == null) return;

        syncedTab = selected;
        if (!root.IsActive && sceneManager.RootMasterScenes.Contains(root)) {
            // deferred so we don't change the scene view windows in the middle of drawing them
            window.InvokeFromUIThread(() => sceneManager.ChangeMasterScene(root));
        }
    }
}

/// <summary>
/// Inspector panel, shows object inspectors as tabs.
/// </summary>
public class InspectorPanel : DockSlotPanel
{
    public override DockSlot Slot => DockSlot.Inspector;
    protected override string Title => Lang.Windows.Slot_Inspector.ToString();
    protected override string EmptyHint => Lang.Windows.Slot_InspectorHint.ToString();
    public override int FixedID => -201;

    protected override void ShowDocument(IViewportDocument document) => document.OnInspectorIMGUI();

    protected override string GetTabLabel(IWindowHandler handler)
    {
        if (handler is not ObjectInspector inspector || inspector.Target == null) return handler.HandlerName;

        var icon = AppIcons.GetIcon(inspector.Target);
        return icon == '\0' ? inspector.Target.ToString() ?? handler.HandlerName : $"{icon} {inspector.Target}";
    }
}
