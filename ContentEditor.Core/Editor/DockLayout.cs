namespace ContentEditor;

/// <summary>
/// Panels of the default editor layout that a window can request to be docked into when it first opens.
/// </summary>
public enum DockSlot
{
    None,
    Hierarchy,
    Viewport,
    Inspector,
    AssetBrowser,
}

public static class DockLayout
{
    public static readonly DockSlot[] Slots = [DockSlot.Hierarchy, DockSlot.Viewport, DockSlot.Inspector, DockSlot.AssetBrowser];

    private static readonly uint[] slotNodes = new uint[Slots.Length + 1];

    // dock nodes that windows of each slot were docked in (with and without the slot's placeholder), and whether any non-placeholder window of the slot was open at all
    private static HashSet<uint>[] dockedNodes = CreateNodeSets();
    private static HashSet<uint>[] prevDockedNodes = CreateNodeSets();
    private static HashSet<uint>[] windowDockedNodes = CreateNodeSets();
    private static HashSet<uint>[] prevWindowDockedNodes = CreateNodeSets();
    private static bool[] openSlots = new bool[Slots.Length + 1];
    private static bool[] prevOpenSlots = new bool[Slots.Length + 1];

    private static HashSet<uint>[] CreateNodeSets() => Enumerable.Range(0, Slots.Length + 1).Select(_ => new HashSet<uint>()).ToArray();

    /// <summary>
    /// When set, windows get re-docked into their slot regardless of whether they were already placed during this session (e.g. right after the layout was reset).
    /// </summary>
    public static bool ForceRedock { get; set; }

    public static uint GetSlotNode(DockSlot slot) => slotNodes[(int)slot];
    public static void SetSlotNode(DockSlot slot, uint nodeId) => slotNodes[(int)slot] = nodeId;

    /// <summary>
    /// Records where the current ImGui window of the given slot is docked. Should be called between Begin() and End().
    /// </summary>
    public static void TrackCurrentWindow(DockSlot slot, bool isPlaceholder = false)
    {
        if (slot == DockSlot.None) return;

        var dockId = ImGui.GetWindowDockID();
        if (dockId != 0) dockedNodes[(int)slot].Add(dockId);
        if (!isPlaceholder) {
            openSlots[(int)slot] = true;
            if (dockId != 0) windowDockedNodes[(int)slot].Add(dockId);
        }
    }

    /// <summary>
    /// Moves the window tracking data from the current frame into the previous frame's. Should be called once at the start of every frame.
    /// </summary>
    public static void NextFrame()
    {
        (prevDockedNodes, dockedNodes) = (dockedNodes, prevDockedNodes);
        (prevWindowDockedNodes, windowDockedNodes) = (windowDockedNodes, prevWindowDockedNodes);
        (prevOpenSlots, openSlots) = (openSlots, prevOpenSlots);
        foreach (var set in dockedNodes) set.Clear();
        foreach (var set in windowDockedNodes) set.Clear();
        Array.Clear(openSlots);
    }

    /// <summary>
    /// Dock nodes that windows of the given slot (including its placeholder) were docked in during the previous frame.
    /// </summary>
    public static IReadOnlyCollection<uint> GetDockedNodes(DockSlot slot) => prevDockedNodes[(int)slot];

    /// <summary>
    /// Whether any non-placeholder window of the given slot was docked in the given node during the previous frame.
    /// </summary>
    public static bool HasWindowsDockedIn(DockSlot slot, uint nodeId) => prevWindowDockedNodes[(int)slot].Contains(nodeId);

    /// <summary>
    /// Whether any window of the given slot, docked or not, was shown during the previous frame (not counting its placeholder).
    /// </summary>
    public static bool HasOpenWindows(DockSlot slot) => prevOpenSlots[(int)slot];

    /// <summary>
    /// Requests the next ImGui window to be docked into the given slot. Only applies the first time the window is shown during the current session, so any manual rearrangement is kept.
    /// </summary>
    /// <returns>Whether the slot exists. If it does, SetNextWindowPos() should not be called for the window as that would undock it again.</returns>
    public static bool DockNextWindow(DockSlot slot)
    {
        if (slot == DockSlot.None) return false;

        var nodeId = GetSlotNode(slot);
        if (nodeId == 0 || ImGuiP.DockBuilderGetNode(nodeId).IsNull) return false;

        ImGui.SetNextWindowDockID(nodeId, ForceRedock ? ImGuiCond.Always : ImGuiCond.Once);
        return true;
    }

    /// <summary>
    /// Selects the tab of a docked window without focusing it, optionally also moving it to the front of its tab bar.
    /// </summary>
    /// <returns>False if the window is not (yet) docked into a tab bar.</returns>
    public static bool SelectDockedTab(string windowName, bool moveToFront)
    {
        var window = ImGuiP.FindWindowByName(windowName);
        if (window.IsNull || window.DockNode.IsNull || window.DockNode.TabBar.IsNull) return false;

        var tabBar = window.DockNode.TabBar;
        var tab = ImGuiP.TabBarFindTabByID(tabBar, window.TabId);
        if (tab.IsNull) return false;

        if (moveToFront) {
            var order = ImGuiP.TabBarGetTabOrder(tabBar, tab);
            if (order > 0) ImGuiP.TabBarQueueReorder(tabBar, tab, -order);
        }
        tabBar.NextSelectedTabId = window.TabId;
        return true;
    }
}
