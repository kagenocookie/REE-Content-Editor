using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ReeLib;
using System.Numerics;
using System.Text.Json;

namespace ContentEditor.App;
public class LuaMacroShelf : IWindowHandler, IKeepEnabledWhileSaving
{
    public string HandlerName => $"{AppIcons.SI_LUA} Macro Shelf";
    public bool HasUnsavedChanges => false;
    public int FixedID => -10227;
    private WindowData data = null!;
    protected UIContext context = null!;
    private string MacroMasterListPath => Path.Combine(AppConfig.Instance.LuaUserPath, "lua_macroshelf_masterlist.json");
    private int selectedTabIDX = 0;
    private string newGroupName = string.Empty;
    private class MacroMasterList
    {
        public List<string> Groups { get; set; } = [];
        public List<string> MacroFiles { get; set; } = [];
    }
    private class MacroTabItem
    {
        public required string Name { get; set; }
    }

    private readonly List<MacroTabItem> tabs = [];
    public class MacroEntry
    {
        public string? Path { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Group { get; set; }
        public string? Icon { get; set; }
        public uint IconColor { get; set; }
        public bool IsGameSpecific { get; set; }
    }
    private readonly List<MacroEntry> macros = new();
    private class MacroDraft
    {
        public string Path = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public string Group = "General";
        public string Icon = "SI_LUA";
        public uint IconColor = ImGui.GetColorU32(ImGuiCol.Text);
        public bool IsGameSpecific = true;

        public MacroEntry ToEntry() => new MacroEntry {
            Path = Path,
            Name = Name,
            Description = Description,
            Group = Group,
            Icon = Icon,
            IconColor = IconColor,
            IsGameSpecific = IsGameSpecific
        };
    }
    private MacroDraft macroDraft = new();
    private string[]? macroIconsArray;
    private enum SidebarMenu
    {
        None,
        NewMacro,
        MacroSettings
    }
    private SidebarMenu activeSidebarMenu = SidebarMenu.None;
    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
        macroIconsArray = BuildMacroIconsArray();
        ScanForMacros();
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
        switch (activeSidebarMenu) {
            case SidebarMenu.NewMacro: ShowNewMacroMenu(); break;
            case SidebarMenu.MacroSettings: ShowMacroSettingsMenu(); break;
            case SidebarMenu.None: break;
        }
        ImGui.BeginChild("Tabs");
        ShowTabs();
        ImGui.EndChild();
    }
    private void ShowSidebar()
    {
        SidebarToggle($"{AppIcons.SI_GenericAdd}", SidebarMenu.NewMacro, "Create a new Macro");
        if (ImGui.Button($"{AppIcons.SI_Update}")) {
            ScanForMacros();
        }
        ImguiHelpers.Tooltip("Rescan macros folder"u8);
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FolderOpenFileExplorer, new[] { Colors.IconSecondary, Colors.IconPrimary })) {
            FileSystemUtils.ShowFileInExplorer(AppConfig.Instance.LuaUserPath);
        }
        ImguiHelpers.Tooltip("Open Macros folder in File Explorer"u8);
        SidebarToggle($"{AppIcons.SI_Settings}", SidebarMenu.MacroSettings, "Macro Settings");
    }
    private void SidebarToggle(string icon, SidebarMenu activeMenu, string tooltip)
    {
        bool isActive = activeSidebarMenu == activeMenu;
        if (ImguiHelpers.ToggleButton($"{icon}", ref isActive, Colors.IconActive)) { activeSidebarMenu = isActive ? activeMenu : SidebarMenu.None; }
        ImguiHelpers.Tooltip(tooltip);
    }
    private void ShowNewMacroMenu()
    {
        ImGui.BeginChild("NewMacro", new Vector2(450, 0));
        AppImguiHelpers.InputFilepath("LUA Script Path"u8, ref macroDraft.Path, FileFilters.LuaFile);
        ImguiHelpers.IsRequired();

        ImGui.InputText("Macro Name", ref macroDraft.Name, 64);
        ImguiHelpers.IsRequired();

        ImGui.InputTextMultiline("Description", ref macroDraft.Description, 128, new Vector2(0, ImGui.GetFrameHeight() * 2));

        ShowMacroGroupsCombo("Group", ref macroDraft.Group);

        string fakeComboText = $"{ResolveMacroIcon(macroDraft.Icon)} {macroDraft.Icon}";
        ImGui.SetNextItemAllowOverlap();
        ImGui.InputText("##fakeComboInputBox", ref fakeComboText, 64, ImGuiInputTextFlags.ReadOnly); //SILVER: They will never know
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMax().X - ImGui.GetFrameHeight(), ImGui.GetItemRectMin().Y));
        ImGui.SetNextItemAllowOverlap();
        if (ImGui.ArrowButton("##fakeComboDropdown", ImGuiDir.Down)) {
            ImGui.OpenPopup("IconPickerPopup");
        }
        ImGui.SameLine();
        ImGui.Text("Icon");

        if (ImGui.BeginPopup("IconPickerPopup")) {
            int iconCount = 0;
            const int iconsPerRow = 12;
            foreach (var icon in macroIconsArray!) {
                if (iconCount++ % iconsPerRow != 0) {
                    ImGui.SameLine();
                }
                bool isSelectedIcon = icon == macroDraft.Icon;
                ImGui.PushStyleColor(ImGuiCol.Text, isSelectedIcon ? Colors.IconActive : ImguiHelpers.GetColor(ImGuiCol.Text));
                ImGui.PushStyleColor(ImGuiCol.Border, isSelectedIcon ? Colors.IconActive : ImguiHelpers.GetColor(ImGuiCol.Text));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, isSelectedIcon ? 2 : 0);
                if (ImGui.Button(ResolveMacroIcon(icon), new Vector2(32, 32))) {
                    macroDraft.Icon = icon;
                }
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(2);
                ImguiHelpers.Tooltip(icon);
            }
            ImGui.EndPopup();
        }
        Vector4 color = ImGui.ColorConvertU32ToFloat4(macroDraft.IconColor);
        if (ImGui.ColorEdit4("Icon Color", ref color)) {
            macroDraft.IconColor = ImGui.ColorConvertFloat4ToU32(color);
        }
        ImGui.Checkbox("Game Specific", ref macroDraft.IsGameSpecific);
        ImGui.Separator();

        using (var _ = ImguiHelpers.Disabled((string.IsNullOrWhiteSpace(macroDraft.Path) || string.IsNullOrWhiteSpace(macroDraft.Name)))) {
            if (ImGui.Button($"{AppIcons.SI_GenericAdd} Add Macro", new Vector2(-1, 0))) {
                SaveMacro(macroDraft.ToEntry());
                macroDraft = new();
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator(ImguiHelpers.GetColor(ImGuiCol.Separator), 2, 0, ImGui.GetContentRegionAvail().Y);
        ImGui.SameLine();
    }
    private void ShowMacroSettingsMenu()
    {
        // TODO SILVER
        ImGui.BeginChild("MacroSettings", new Vector2(450, 0));
        ImGui.Text("Lorem Ipsum");
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
            ShowTabContent(tabs[selectedTabIDX].Name);
        }
    }
    private void ShowTabContent(string tab)
    {
        var filteredMacros = macros.Where(x => x.Group == tab).OrderBy(x => x.Name).ToList();
        if (filteredMacros.Count == 0) {
            ImGui.TextDisabled("No macros added.");
            return;
        }

        foreach (var macro in filteredMacros) {

            ImGui.PushID(macro.Name);
            ImGui.BeginChild($"MacroCard_{macro.Name}", new Vector2(0, 70), ImGuiChildFlags.Borders);

            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.ColorConvertU32ToFloat4(macro.IconColor));
            ImGui.Text($"{ResolveMacroIcon(macro.Icon!)}");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.BeginGroup();

            ImGui.Text(macro.Name);
            ImGui.TextDisabled(macro.Description);

            ImGui.EndGroup();

            ImGui.SameLine(ImGui.GetContentRegionAvail().X - (ImGui.CalcTextSize($"{AppIcons.SI_GenericClose}").X * 2 + ImGui.GetStyle().FramePadding.X * 3 + ImGui.GetStyle().ItemSpacing.X));
            if (ImGui.Button($"{AppIcons.Play}")) {
                //TODO SILVER
            }
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_GenericDelete2}")) {
                DeleteMacro(macro);
            }

            ImGui.EndChild();
            ImGui.PopID();
        }
    }

    private bool ShowMacroGroupsCombo(string label, ref string previewText)
    {
        bool changed = false;
        if (ImGui.BeginCombo(label, previewText)) {
            foreach (var tab in tabs) {
                bool isSelected = tab.Name == previewText;
                if (ImGui.Selectable(tab.Name, isSelected)) {
                    previewText = tab.Name;
                    changed = true;
                }

                if (isSelected) {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }
        // TODO SILVER
        if (ImGui.InputTextWithHint("New Group", "Press Enter to confirm", ref newGroupName, 64, ImGuiInputTextFlags.EnterReturnsTrue)) {
            if (!string.IsNullOrWhiteSpace(newGroupName)) {
                string groupName = newGroupName.Trim();

                if (!tabs.Any(x => x.Name.Equals( groupName, StringComparison.OrdinalIgnoreCase))) {
                    tabs.Add(new MacroTabItem { Name = groupName });
                    SaveMacrosMasterList();
                }
                newGroupName = string.Empty;
                changed = true;
            }
        }

        return changed;
    }
    private void ScanForMacros()
    {
        try {
            macros.Clear();
            tabs.Clear();
            MacroMasterList master;
            string globalMacroPath = GetMacroFolderPath(false);
            string gameMacroPath = GetMacroFolderPath(true);
            Directory.CreateDirectory(globalMacroPath);
            Directory.CreateDirectory(gameMacroPath);

            if (File.Exists(MacroMasterListPath)) {
                master = JsonSerializer.Deserialize<MacroMasterList>( File.ReadAllText(MacroMasterListPath) ) ?? new MacroMasterList();
            } else {
                master = new MacroMasterList();
            }

            LoadMacrosFromFolder(globalMacroPath, master);
            LoadMacrosFromFolder(gameMacroPath, master);

            if (master.Groups.Count == 0) {
                master.Groups.Add("General");
            }

            foreach (string groupName in master.Groups) {
                bool exists = false;
                foreach (var tab in tabs) {
                    if (tab.Name == groupName) {
                        exists = true;
                        break;
                    }
                }

                if (!exists) {
                    tabs.Add(new MacroTabItem { Name = groupName });
                }
            }

            SaveMacrosMasterList();
        } catch (Exception e) {
            Logger.Error($"Failed to load macros: {e}");
        }
    }
    private void LoadMacrosFromFolder(string folder, MacroMasterList master)
    {
        foreach (string filePath in Directory.GetFiles(folder, "*.json")) {
            try {
                var macro = JsonSerializer.Deserialize<MacroEntry>( File.ReadAllText(filePath));

                if (macro != null) {
                    macros.Add(macro);
                    string relativePath = Path.GetRelativePath( AppConfig.Instance.LuaUserPath, filePath);

                    if (!master.MacroFiles.Contains(relativePath)) {
                        master.MacroFiles.Add(relativePath);
                    }

                    if (!string.IsNullOrWhiteSpace(macro.Group) && !master.Groups.Contains(macro.Group)) {
                        master.Groups.Add(macro.Group);
                    }

                }
            } catch (Exception e) {
                Logger.Error($"Failed to load macro file {filePath}: {e}");
            }
        }
    }
    private void SaveMacro(MacroEntry macro)
    {
        macros.Add(macro);
        string filePath = GetMacroFilePath(macro);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(macro, JsonConfig.configJsonOptions));
        SaveMacrosMasterList();
    }
    private void DeleteMacro(MacroEntry macro)
    {
        macros.Remove(macro);
        var filePath = GetMacroFilePath(macro);
        if (File.Exists(filePath)) {
            File.Delete(filePath);
        }
        SaveMacrosMasterList();
    }
    private void SaveMacrosMasterList()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MacroMasterListPath)!);
        var master = new MacroMasterList {
            Groups = tabs.Select(x => x.Name).Distinct().ToList(),
            MacroFiles = macros.Select(GetMacroFilePath).Select(p => Path.GetRelativePath(AppConfig.Instance.LuaUserPath, p)).ToList()
        };
        File.WriteAllText(MacroMasterListPath, JsonSerializer.Serialize(master, JsonConfig.jsonOptions));
    }

    private static string[] BuildMacroIconsArray()
    {
        return typeof(AppIcons).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Where(f => f.Name.StartsWith("SI_")).Select(f => f.Name).ToArray();
    }
    private static string ResolveMacroIcon(string iconName)
    {
        var field = typeof(AppIcons).GetField(iconName);
        return field?.GetValue(null)?.ToString() ?? $"{AppIcons.SI_LUA}";
    }
    private static string GetMacroFolderPath(bool gameSpecific)
    {
        return gameSpecific ? Path.Combine(AppConfig.Instance.LuaUserPath, "macros", EditorWindow.CurrentWindow!.Workspace.Game.name) : Path.Combine( AppConfig.Instance.LuaUserPath, "macros", "global");
    }
    private static string GetMacroFilePath(MacroEntry macro)
    {
        return Path.Combine(GetMacroFolderPath(macro.IsGameSpecific), $"{macro.Name}.json");
    }
    public bool RequestClose()
    {
        return false;
    }
}
