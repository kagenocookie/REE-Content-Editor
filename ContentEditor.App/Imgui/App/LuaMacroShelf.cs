using ContentEditor.App.Lua;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
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
    private static string MacroMasterListPath => Path.Combine(AppConfig.Instance.LuaUserPath, "lua_macroshelf_masterlist.json");
    private static string MacroGlobalFolderPath => Path.Combine(AppConfig.Instance.LuaUserPath, "macros", "global");
    private static string MacroGameFolderPath => Path.Combine(AppConfig.Instance.LuaUserPath, "macros", EditorWindow.CurrentWindow!.Workspace.Game.name);
    private int selectedTabIDX = 0;
    private string newGroupName = string.Empty;
    private bool isShowNewGroupMenu = false;
    private bool isEditMacroDataMode = false;
    private string[] macroIconsArray = [];
    private class MacroMasterList
    {
        public List<string> Groups { get; set; } = [
            "General",
        ];
        public List<string> MacroFiles { get; set; } = [];
    }
    private MacroMasterList master = new();
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
        [System.Text.Json.Serialization.JsonIgnore]
        public string? JsonFilePath { get; set; }
    }
    private readonly List<MacroEntry> macros = new();
    private MacroEntry? pendingDeleteMacro;
    private class MacroDraft
    {
        public string Path = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public string Group = "General";
        public string Icon = "SI_LUA";
        public uint IconColor = ImGui.GetColorU32(ImGuiCol.Text);
        public bool IsGameSpecific = true;
        public string JsonFilePath = string.Empty;

        public MacroEntry ToEntry() => new MacroEntry {
            Path = GetLuaScriptRelativePath(Path),
            Name = Name,
            Description = Description,
            Group = Group,
            Icon = Icon,
            IconColor = IconColor,
            IsGameSpecific = IsGameSpecific,
            JsonFilePath = JsonFilePath
        };
    }
    private MacroDraft macroDraft = new();
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
        if (pendingDeleteMacro != null) {
            ImGui.OpenPopup(Lang.General.ConfirmTitle);
            AppImguiHelpers.ShowActionModal(Lang.General.ConfirmTitle, $"{AppIcons.SI_GenericDelete2}", Colors.IconTertiary,
                Lang.MacroShelf.Confirm_DeleteMacroFile.FormatRef(pendingDeleteMacro.Name!),
                () => {
                    DeleteMacro(pendingDeleteMacro);
                    pendingDeleteMacro = null;
                },
                () => {
                    pendingDeleteMacro = null;
                }
            );
        }
    }
    private void ShowSidebar()
    {
        SidebarToggle($"{AppIcons.SI_GenericAdd}", SidebarMenu.NewMacro);
        ImguiHelpers.Tooltip(Lang.MacroShelf.Tooltip_CreateMacro);
        using (var _ = ImguiHelpers.Disabled(activeSidebarMenu != SidebarMenu.NewMacro)) {
            if (ImGui.Button($"{AppIcons.SI_GenericClear}")) {
                macroDraft = new();
                isEditMacroDataMode = false;
            }
            ImguiHelpers.Tooltip(Lang.MacroShelf.Tooltip_ClearMacroData);
        }
        ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconActive);
        if (ImGui.Button($"{AppIcons.SI_Update}")) {
            ScanForMacros();
        }
        ImGui.PopStyleColor();
        ImguiHelpers.Tooltip(Lang.MacroShelf.Tooltip_RescanMacros);
        if (ImGui.Button($"{AppIcons.SI_FolderOpen}")) {
            FileSystemUtils.ShowFileInExplorer(AppConfig.Instance.LuaUserPath);
        }
        ImguiHelpers.Tooltip(Lang.MacroShelf.Tooltip_OpenMacrosFolder);
        AppImguiHelpers.WikiLinkButton("https://github.com/kagenocookie/REE-Content-Editor/wiki/Lua-API", true);
        //SidebarToggle($"{AppIcons.SI_Settings}", SidebarMenu.MacroSettings, "Macro Settings");
    }
    private void SidebarToggle(string icon, SidebarMenu activeMenu)
    {
        bool isActive = activeSidebarMenu == activeMenu;
        if (ImguiHelpers.ToggleButton($"{icon}", ref isActive, Colors.IconActive)) { activeSidebarMenu = isActive ? activeMenu : SidebarMenu.None; }
    }
    private void ShowNewMacroMenu()
    {
        ImGui.BeginChild("NewMacro", new Vector2(450, 0));
        AppImguiHelpers.InputFilepath(Lang.MacroShelf.Label_MacroLuaPath, ref macroDraft.Path, FileFilters.LuaFile);//TODO SILVER: This should open to the user's lua folder
        ImguiHelpers.IsRequired();
        bool isValidLuaPath = string.IsNullOrWhiteSpace(macroDraft.Path) || IsPathInsideLuaFolder(macroDraft.Path);
        if (!isValidLuaPath) {
            ImGui.TextColored(Colors.Error, Lang.MacroShelf.Error_InvalidLuaPath); // SILVER: Maybe move this some place else?
        }
        ImGui.InputText(Lang.MacroShelf.Label_MacroName, ref macroDraft.Name, 64);
        ImguiHelpers.IsRequired();
        ImGui.InputText(Lang.MacroShelf.Label_MacroDesc, ref macroDraft.Description, 128);

        ShowMacroGroupsCombo(ref macroDraft.Group);

        Vector4 color = ImGui.ColorConvertU32ToFloat4(macroDraft.IconColor);
        if (ImGui.ColorEdit4("##IconColor", ref color, ImGuiColorEditFlags.NoInputs)) {
            macroDraft.IconColor = ImGui.ColorConvertFloat4ToU32(color);
        }
        ImguiHelpers.Tooltip(Lang.MacroShelf.Tooltip_IconColor);
        ImGui.SameLine();
        string fakeComboText = $"{ResolveMacroIcon(macroDraft.Icon)} {macroDraft.Icon}";
        ImGui.BeginDisabled();
        ImGui.SetNextItemAllowOverlap();
        var itemW = ImGui.CalcItemWidth();
        ImGui.SetNextItemWidth(itemW - (ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X));
        ImGui.InputText("##fakeComboInputBox", ref fakeComboText, 64, ImGuiInputTextFlags.ReadOnly);
        ImGui.EndDisabled();
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMax().X - ImGui.GetFrameHeight(), ImGui.GetItemRectMin().Y));
        ImGui.SetNextItemAllowOverlap();
        if (ImGui.ArrowButton("##fakeComboDropdown", ImGuiDir.Down)) {
            ImGui.OpenPopup("IconPickerPopup");
        }
        ImGui.SameLine();
        ImGui.Text(Lang.MacroShelf.Label_MacroIcon);

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
        ImGui.Checkbox(Lang.MacroShelf.Label_MacroType, ref macroDraft.IsGameSpecific);
        ImguiHelpers.Tooltip(Lang.MacroShelf.Tooltip_MacroType);
        ImGui.Separator();
        using (var _ = ImguiHelpers.Disabled(string.IsNullOrWhiteSpace(macroDraft.Name) || isShowNewGroupMenu || !isValidLuaPath)) {
            if (ImGui.Button(isEditMacroDataMode ? Lang.MacroShelf.Button_SaveMacro : Lang.MacroShelf.Button_AddMacro, new Vector2(-1, 0))) {
                SaveMacro(macroDraft.ToEntry());
                macroDraft = new();
                isEditMacroDataMode = false;
            }
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator(ImguiHelpers.GetColor(ImGuiCol.Separator), 2, 0, ImGui.GetContentRegionAvail().Y);
        ImGui.SameLine();
    }
    private bool ShowMacroGroupsCombo(ref string group)
    {
        bool changed = false;
        ImguiHelpers.ToggleButtonMultiColor(AppIcons.SIC_AddGroup, ref isShowNewGroupMenu, new[] { Colors.IconSecondary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary }, Colors.IconActive);
        ImguiHelpers.Tooltip(Lang.MacroShelf.Tooltip_NewGroup);
        ImGui.SameLine();
        var itemW = ImGui.CalcItemWidth();
        var comboW = ImGui.CalcTextSize($"{AppIcons.SI_GenericClose}").X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X;
        ImGui.PushItemWidth(itemW - comboW);
        if (ImGui.BeginCombo(Lang.MacroShelf.Label_MacroGroup, group)) {
            foreach (var tab in tabs) {
                bool isSelected = tab.Name == group;
                if (ImGui.Selectable(tab.Name, isSelected)) {
                    group = tab.Name;
                    changed = true;
                }

                if (isSelected) {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }
        if (isShowNewGroupMenu) {
            using (var _ = ImguiHelpers.Disabled(string.IsNullOrWhiteSpace(newGroupName))) {
                if (ImGui.Button($"{AppIcons.SI_GenericAdd}")) {
                    string groupName = newGroupName.Trim();
                    if (!tabs.Any(x => x.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase))) {
                        tabs.Add(new MacroTabItem { Name = groupName });
                        SaveMacrosMasterList();
                    }
                    newGroupName = string.Empty;
                    isShowNewGroupMenu = false;
                    group = groupName;
                    changed = true;
                }
                ImguiHelpers.Tooltip(Lang.MacroShelf.Tooltip_AddGroup);
            }
            ImGui.SameLine();
            ImGui.InputTextWithHint("##NewGroup", Lang.MacroShelf.Hint_NewGroup, ref newGroupName, 64);
            ImGui.Separator();
        }
        ImGui.PopItemWidth();
        return changed;
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
            ImGui.TextDisabled(Lang.MacroShelf.Label_NoMacros);
            return;
        }

        float macroCardW = 250f;
        float macroCardH = 70f;
        int rowIDX = 0;
        int rows = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / (macroCardW + ImGui.GetStyle().ItemSpacing.X)));

        foreach (var macro in filteredMacros) {
            ImGui.PushID(macro.Name + macro.JsonFilePath);
            if (rowIDX > 0) {
                ImGui.SameLine();
            }
            var pos = ImGui.GetCursorScreenPos();
            ImGui.PushStyleColor(ImGuiCol.Button, ImGui.ColorConvertU32ToFloat4(macro.IconColor) * 0.25f);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.ColorConvertU32ToFloat4(macro.IconColor) * 0.45f);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGui.ColorConvertU32ToFloat4(macro.IconColor) * 0.65f);
            ImGui.PushStyleColor(ImGuiCol.Border, ImGui.GetColorU32(macro.IconColor));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f);
            if (ImGui.Button($"##macroCard_{macro.Name}", new Vector2(macroCardW, macroCardH))){
                RunMacro(macro);
            }
            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar();
            ShowMacroCardContextMenu(macro);

            if (!string.IsNullOrEmpty(macro.Description)) {
                ImguiHelpers.Tooltip(macro.Description);
            }

            var drawList = ImGui.GetWindowDrawList();
            float iconSize = UI.FontSize * 2;
            float iconPadding = 10;
            ImGui.PushFont(null, iconSize);
            drawList.AddText(pos + new Vector2(iconPadding, (macroCardH - iconSize) * 0.5f), macro.IconColor, ResolveMacroIcon(macro.Icon!));
            ImGui.PopFont();

            float textX = pos.X + iconPadding + iconSize + 8f;
            float textWidth = macroCardW - (iconPadding + iconSize + 15f);
            string displayName =  AppImguiHelpers.Ellipsize(macro.Name!, textWidth);

            drawList.AddText(new Vector2(textX, pos.Y + 10), ImGui.GetColorU32(ImGuiCol.Text), displayName);
            drawList.AddText(new Vector2(textX, pos.Y + 30), ImGui.GetColorU32(ImGuiCol.TextDisabled), macro.IsGameSpecific ? Lang.MacroShelf.Label_MacroTypeA : Lang.MacroShelf.Label_MacroTypeB);

            rowIDX++;
            if (rowIDX >= rows) {
                rowIDX = 0;
            }
            ImGui.PopID();
        }
    }
    private void ShowMacroCardContextMenu(MacroEntry macro)
    {
        if (ImGui.BeginPopupContextItem("macroCardPopup")) {
            if (ImGui.MenuItem(Lang.MacroShelf.MenuItem_RunMacro)) {
                RunMacro(macro);
            }
            ImGui.Spacing();
            if (ImGui.MenuItem(Lang.MacroShelf.MenuItem_EditMacro)) {
                activeSidebarMenu = SidebarMenu.NewMacro;
                isEditMacroDataMode = true;
                macroDraft.Path = Path.Combine(AppConfig.Instance.LuaUserPath, macro.Path!);
                macroDraft.Name = macro.Name!;
                macroDraft.Description = macro.Description!;
                macroDraft.Group = macro.Group!;
                macroDraft.Icon = macro.Icon!;
                macroDraft.IconColor = macro.IconColor;
                macroDraft.IsGameSpecific = macro.IsGameSpecific;
            }
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
            if (ImGui.MenuItem(Lang.MacroShelf.MenuItem_DeleteMacro)) {
                pendingDeleteMacro = macro;
            }
            ImGui.PopStyleColor();
            ImGui.EndPopup();
        }
    }
    private void ScanForMacros()
    {
        try {
            tabs.Clear();
            macros.Clear();
            master = new MacroMasterList();
            foreach (string folder in new[] { MacroGlobalFolderPath, MacroGameFolderPath }) {
                Directory.CreateDirectory(folder);
                LoadMacrosFromFolder(folder, master);
            }
            foreach (string groupName in master.Groups.Distinct()) {
                tabs.Add(new MacroTabItem { Name = groupName });
                selectedTabIDX = Math.Clamp(selectedTabIDX, 0, tabs.Count - 1);
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
                var macro = JsonSerializer.Deserialize<MacroEntry>(File.ReadAllText(filePath));
                if (macro != null) {
                    macro.JsonFilePath = filePath;
                    macros.Add(macro);
                    string relativePath = Path.GetRelativePath(AppConfig.Instance.LuaUserPath, filePath);
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
        string filePath = Path.Combine(macro.IsGameSpecific ? MacroGameFolderPath : MacroGlobalFolderPath, $"{macro.Name}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(macro, JsonConfig.configJsonOptions));
        ScanForMacros();
    }
    private void DeleteMacro(MacroEntry macro)
    {
        macros.Remove(macro);
        if (File.Exists(macro.JsonFilePath)) {
            File.Delete(macro.JsonFilePath);
            master.MacroFiles.Remove(Path.GetRelativePath(AppConfig.Instance.LuaUserPath, macro.JsonFilePath));
        }
        ScanForMacros();
    }
    private void SaveMacrosMasterList()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MacroMasterListPath)!);
        File.WriteAllText(MacroMasterListPath, JsonSerializer.Serialize(master, JsonConfig.jsonOptions));
    }
    private void RunMacro(MacroEntry macro)
    {
        try {
            var fullPath = Path.Combine(AppConfig.Instance.LuaUserPath, macro.Path!);
            if (!File.Exists(fullPath)) {
                Logger.Error($"Macro script not found at: {fullPath}");
                return;
            }
            var scriptText = File.ReadAllText(fullPath);
            var lua = LuaWrapper.Create(EditorWindow.CurrentWindow!.Workspace, EditorWindow.CurrentWindow);
            lua.Run(scriptText);
        } catch (Exception e) {
            Logger.Error($"Failed to run macro: {e}");
        }
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
    private static bool IsPathInsideLuaFolder(string path)
    {
        string luaRoot = Path.GetFullPath(AppConfig.Instance.LuaUserPath);
        string fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(luaRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || fullPath.Equals(luaRoot, StringComparison.OrdinalIgnoreCase);
    }
    private static string GetLuaScriptRelativePath(string path)
    {
        return Path.GetRelativePath(AppConfig.Instance.LuaUserPath, Path.GetFullPath(path));
    }
    public bool RequestClose()
    {
        return false;
    }
}
