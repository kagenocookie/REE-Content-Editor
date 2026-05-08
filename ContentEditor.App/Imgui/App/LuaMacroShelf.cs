using System.Numerics;
using ContentEditor.Core;

namespace ContentEditor.App.Imgui.App;
public class LuaMacroShelf : IWindowHandler, IKeepEnabledWhileSaving
{
    public string HandlerName => $"{AppIcons.SI_LUA} Macro Shelf";
    public bool HasUnsavedChanges => false;
    public int FixedID => -10227;
    private WindowData data = null!;
    protected UIContext context = null!;
    private bool isAddMode = false;
    private int selectedTabIDX = 0;
    private enum MacroTab
    {
        Prefab,
        GameObject,
        Collision,
        Primitive,
        Ungrouped
    }
    private class MacroTabItem
    {
        public required string Name { get; set; }
        public required MacroTab ID { get; set; }
    }

    private readonly List<MacroTabItem> tabs = new()
    {
        new MacroTabItem { Name = "Prefab", ID = MacroTab.Prefab },
        new MacroTabItem { Name = "GameObject", ID = MacroTab.GameObject },
        new MacroTabItem { Name = "Collision", ID = MacroTab.Collision },
        new MacroTabItem { Name = "Primitive", ID = MacroTab.Primitive },
        new MacroTabItem { Name = "Ungrouped", ID = MacroTab.Ungrouped },
    };
    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }
    public void OnWindow() => this.ShowDefaultWindow(context);

    public void OnIMGUI()
    {
        ShowMacroShelf();
    }
    private void ShowMacroShelf()
    {
        float sidebarW = ImGui.CalcTextSize($"{AppIcons.SI_GenericClose}").X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.BeginChild("Sidebar", new Vector2(sidebarW, 0));
        ShowSidebar();
        ImGui.EndChild();
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator(ImguiHelpers.GetColor(ImGuiCol.Separator), 2, 0, ImGui.GetContentRegionAvail().Y);
        ImGui.SameLine();
        if (isAddMode) {
            ShowNewMacroMenu();
        }
        ImGui.BeginChild("Tabs", new Vector2(0, 0));
        ShowTabs();
        ImGui.EndChild();
    }
    private void ShowSidebar()
    {
        ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericAdd}", ref isAddMode, Colors.IconActive);
        ImguiHelpers.Tooltip("Add new macro"u8);

    }
    private void ShowNewMacroMenu()
    {
        ImGui.BeginChild("NewMacro", new Vector2(450, 0));
        string newMacroPath = string.Empty;
        string newMacroName = string.Empty;
        if (AppImguiHelpers.InputFilepath("LUA Script Path",ref newMacroPath, FileFilters.LuaFile)) {

        }
        ImGui.SameLine();
        ImGui.TextColored(Colors.TextActive, "*");
        ImGui.InputText("Macro Name", ref newMacroPath, 20);
        ImGui.SameLine();
        ImGui.TextColored(Colors.TextActive, "*");

        ImGui.EndChild();
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator(ImguiHelpers.GetColor(ImGuiCol.Separator), 2, 0, ImGui.GetContentRegionAvail().Y);
        ImGui.SameLine();
    }
    private void ShowTabs()
    {
        float tabWidth = 80;
        int tabsPerRow = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / tabWidth));
        for (int row = 0; row < tabs.Count; row += tabsPerRow) {
            bool isOnThisRow = selectedTabIDX >= row && selectedTabIDX < row + tabsPerRow;

            if (ImGui.BeginTabBar($"##MacroTabs{row}", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton | ImGuiTabBarFlags.DrawSelectedOverline)) {
                ImGui.PushStyleColor(ImGuiCol.TabSelected, isOnThisRow ? ImguiHelpers.GetColor(ImGuiCol.TabSelected) : ImguiHelpers.GetColor(ImGuiCol.Tab));
                ImGui.PushStyleColor(ImGuiCol.TabSelectedOverline, isOnThisRow ? ImguiHelpers.GetColor(ImGuiCol.TabSelectedOverline) : ImguiHelpers.GetColor(ImGuiCol.Tab));

                for (int i = row; i < Math.Min(row + tabsPerRow, tabs.Count); i++) {
                    ImGui.PushID(i);
                    if (ImGui.BeginTabItem(tabs[i].Name, (selectedTabIDX == i) ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None)) {
                        ImGui.EndTabItem();
                    }
                    if (ImGui.IsItemClicked()) {
                        selectedTabIDX = i;
                    }
                    ImGui.PopID();
                }
                ImGui.PopStyleColor(2);
                ImGui.EndTabBar();
            }
        }

        if (selectedTabIDX >= 0 && selectedTabIDX < tabs.Count) {
            ShowTabContent(tabs[selectedTabIDX].ID);
        }
    }
    private void ShowTabContent(MacroTab tab)
    {
        switch (tab) {
            case MacroTab.Prefab: ImGui.Text("Macros and some lorem ipsum text here..."); break;
            case MacroTab.GameObject: ImGui.Text("Yeppers"); break;
            case MacroTab.Collision: ImGui.Text("Crazy placeholder here"); break;
            case MacroTab.Primitive: ImGui.Text("Lorem Ipsum Vol.II"); break;
            case MacroTab.Ungrouped: ImGui.Text("Lorem Ipsum Vol.III"); break;
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}
