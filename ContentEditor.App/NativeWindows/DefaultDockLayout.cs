using System.Numerics;
using ContentEditor.Core;

namespace ContentEditor.App.Windowing;

/// <summary>
/// Builds and maintains the default editor layout: hierarchy | viewport | inspector, with the asset browser below the hierarchy and viewport.
/// </summary>
internal static class DefaultDockLayout
{
    // increment whenever the default layout structure changes so existing users get the new one
    private const int LayoutVersion = 1;

    private static bool loaded;
    private static bool resetRequested;
    private static bool rebuilt;

    public static void RequestReset() => resetRequested = true;

    /// <summary>
    /// Returns true once after the layout has been (re)built, so the owner can fix up things like tab order.
    /// </summary>
    public static bool ConsumeRebuilt()
    {
        var value = rebuilt;
        rebuilt = false;
        return value;
    }

    /// <summary>
    /// Ensures the default layout exists within the given dockspace. Must be called before the dockspace is submitted.
    /// </summary>
    public static void Update(uint dockspaceId, Vector2 size)
    {
        var settings = AppConfig.Settings.DockLayout;
        if (!loaded) {
            loaded = true;
            DockLayout.SetSlotNode(DockSlot.Hierarchy, settings.Hierarchy);
            DockLayout.SetSlotNode(DockSlot.Viewport, settings.Viewport);
            DockLayout.SetSlotNode(DockSlot.Inspector, settings.Inspector);
            DockLayout.SetSlotNode(DockSlot.AssetBrowser, settings.AssetBrowser);
        }

        DockLayout.NextFrame();
        FollowMovedSlots();

        if (!resetRequested && settings.Version == LayoutVersion && NodeExists(dockspaceId) && DockLayout.Slots.Any(slot => NodeExists(DockLayout.GetSlotNode(slot)))) {
            return;
        }
        if (size.X <= 0 || size.Y <= 0) return;

        resetRequested = false;
        Build(dockspaceId, size);
    }

    private static unsafe void Build(uint dockspaceId, Vector2 size)
    {
        ImGuiP.DockBuilderRemoveNode(dockspaceId);
        ImGuiP.DockBuilderAddNode(dockspaceId, (ImGuiDockNodeFlags)ImGuiDockNodeFlagsPrivate.Space | ImGuiDockNodeFlags.PassthruCentralNode);
        ImGuiP.DockBuilderSetNodeSize(dockspaceId, size);

        // the remaining (center) node keeps the central node flag, so the viewport can't be removed by undocking everything from it
        var viewport = dockspaceId;
        var inspector = ImGuiP.DockBuilderSplitNode(viewport, ImGuiDir.Right, 0.22f, null, &viewport);
        var assetBrowser = ImGuiP.DockBuilderSplitNode(viewport, ImGuiDir.Down, 0.3f, null, &viewport);
        var hierarchy = ImGuiP.DockBuilderSplitNode(viewport, ImGuiDir.Left, 0.25f, null, &viewport);
        ImGuiP.DockBuilderFinish(dockspaceId);

        DockLayout.SetSlotNode(DockSlot.Hierarchy, hierarchy);
        DockLayout.SetSlotNode(DockSlot.Viewport, viewport);
        DockLayout.SetSlotNode(DockSlot.Inspector, inspector);
        DockLayout.SetSlotNode(DockSlot.AssetBrowser, assetBrowser);
        // move any already open windows into their new slots
        DockLayout.ForceRedock = true;
        rebuilt = true;

        AppConfig.Settings.DockLayout.Version = LayoutVersion;
        SaveSlotNodes();
    }

    /// <summary>
    /// If the user moved all windows of a slot (or its placeholder) into a different dock node, that node becomes the slot's new node
    /// so that windows opened later on also go there.
    /// </summary>
    private static void FollowMovedSlots()
    {
        var changed = false;
        foreach (var slot in DockLayout.Slots) {
            var docked = DockLayout.GetDockedNodes(slot);
            if (docked.Count == 0 || docked.Contains(DockLayout.GetSlotNode(slot))) continue;

            var newNode = docked.FirstOrDefault(NodeExists);
            if (newNode == 0) continue;

            DockLayout.SetSlotNode(slot, newNode);
            changed = true;
        }
        if (changed) SaveSlotNodes();
    }

    private static void SaveSlotNodes()
    {
        var settings = AppConfig.Settings.DockLayout;
        settings.Hierarchy = DockLayout.GetSlotNode(DockSlot.Hierarchy);
        settings.Viewport = DockLayout.GetSlotNode(DockSlot.Viewport);
        settings.Inspector = DockLayout.GetSlotNode(DockSlot.Inspector);
        settings.AssetBrowser = DockLayout.GetSlotNode(DockSlot.AssetBrowser);
        AppConfig.Settings.Save();
    }

    private const ImGuiWindowFlags PlaceholderFlags = ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoFocusOnAppearing;

    /// <summary>
    /// Shows a placeholder window in every slot that has nothing else docked in it.
    /// ImGui hides empty dock nodes (other than the central one) and gives their space to the sibling node, so without these the layout would collapse whenever a slot is empty.
    /// The placeholders can be moved around like any other window, the slot follows wherever it gets docked.
    /// </summary>
    public static unsafe void ShowEmptySlotPlaceholders()
    {
        foreach (var slot in DockLayout.Slots) {
            var nodeId = DockLayout.GetSlotNode(slot);
            var node = ImGuiP.DockBuilderGetNode(nodeId);
            // the user might have split the slot further, in which case the child nodes are responsible for themselves
            if (!node.IsNull && node.Handle->ChildNodes_0 != null) continue;

            var (title, hint) = GetSlotLabels(slot);
            var windowName = $"{title}###DockSlot_{slot}";
            if (node.IsNull) {
                // the slot's node is gone (e.g. its last window was dragged out of it)
                // keep the placeholder around as a floating window unless the slot's windows are still open somewhere so it can be docked again
                if (DockLayout.HasOpenWindows(slot)) continue;
                ImGui.SetNextWindowSize(new Vector2(400, 300) * UI.UIScale, ImGuiCond.FirstUseEver);
            } else {
                // only the slot's own windows replace the placeholder, the user might have docked it together with other slots' windows
                if (DockLayout.HasWindowsDockedIn(slot, nodeId)) continue;
                ImGui.SetNextWindowDockID(nodeId, DockLayout.ForceRedock ? ImGuiCond.Always : ImGuiCond.Appearing);
            }

            var visible = ImGui.Begin(windowName, PlaceholderFlags);
            DockLayout.TrackCurrentWindow(slot, isPlaceholder: true);
            if (visible) {
                ImGui.SetCursorPosY((ImGui.GetWindowHeight() - ImGui.GetTextLineHeight()) / 2);
                using var _ = ImguiHelpers.OverrideStyleCol(ImGuiCol.Text, Colors.Faded);
                ImguiHelpers.TextCentered(hint);
            }
            ImGui.End();
        }
    }

    private static (string title, string hint) GetSlotLabels(DockSlot slot) => slot switch {
        DockSlot.Hierarchy => (Lang.Windows.Slot_Hierarchy.ToString(), Lang.Windows.Slot_HierarchyHint.ToString()),
        DockSlot.Viewport => (Lang.Windows.Slot_Viewport.ToString(), Lang.Windows.Slot_ViewportHint.ToString()),
        DockSlot.Inspector => (Lang.Windows.Slot_Inspector.ToString(), Lang.Windows.Slot_InspectorHint.ToString()),
        DockSlot.AssetBrowser => (Lang.Windows.Slot_AssetBrowser.ToString(), Lang.Windows.Slot_AssetBrowserHint.ToString()),
        _ => (string.Empty, string.Empty),
    };

    private static bool NodeExists(uint nodeId) => nodeId != 0 && !ImGuiP.DockBuilderGetNode(nodeId).IsNull;
}
