using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Themes;
using ImGuiNET;
using ReeLib;
using System.Numerics;

namespace ContentEditor.App;

public class SettingsWindowHandler : IWindowHandler, IKeepEnabledWhileSaving
{
    public string HandlerName => "Settings";
    public bool HasUnsavedChanges => false;
    public int FixedID => -10002;
    private WindowData data = null!;
    protected UIContext context = null!;
    private static readonly AppConfig config = AppConfig.Instance;
    private bool isShow = true;
    private int selectedGroup = -1;
    private int selectedSubGroup = -1;
    private static bool? _wasOriginallyAlphaBg;
    private static readonly string[] LogLevels = ["Debug", "Info", "Error"];
    private static string? filterKey1, filterKey2;
    private string customGameNameInput = "", customGameFilepath = "";
    private static HashSet<string>? fullSupportedGames;
    private enum SubGroupID
    {
        Preferences_General,
        Display_General,
        Display_Theme,
        Hotkeys_Global,
        Games_ResidentEvil,
        Games_MonsterHunter,
        Games_Other,
        Games_Custom
    }
    private class SettingGroup
    {
        public required string Name { get; set; }
        public List<SettingSubGroup> SubGroups { get; set; } = new();
    }
    private class SettingSubGroup
    {
        public required string Name { get; set; }
        public required SubGroupID ID { get; set; }
    }

    private readonly List<SettingGroup> groups = new()
    {
        new SettingGroup { Name = "Preferences", SubGroups = {
                new SettingSubGroup { Name = "General", ID = SubGroupID.Preferences_General},
            }
        },
        new SettingGroup { Name = "Display", SubGroups = {
                new SettingSubGroup { Name = "General", ID = SubGroupID.Display_General},
                new SettingSubGroup { Name = "Theme", ID = SubGroupID.Display_Theme},
            }
        },
        new SettingGroup { Name = "Hotkeys", SubGroups = {
                new SettingSubGroup { Name = "Global", ID = SubGroupID.Hotkeys_Global},
                // SILVER: More of a TODO thing but there should be contextual hotkeys for the editors
                //new SettingSubGroup { Name = "Pak Browser"},
                //new SettingSubGroup { Name = "Mesh Viewer"},
                //new SettingSubGroup { Name = "Texture Viewer"},
            }
        },
        new SettingGroup { Name = "Games", SubGroups = {
                new SettingSubGroup { Name = "Resident Evil", ID = SubGroupID.Games_ResidentEvil},
                new SettingSubGroup { Name = "Monster Hunter", ID = SubGroupID.Games_MonsterHunter},
                new SettingSubGroup { Name = "Other", ID = SubGroupID.Games_Other},
                new SettingSubGroup { Name = "Custom", ID = SubGroupID.Games_Custom}
            }
        },
    };

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }
    public void OnWindow() => OnIMGUI();
    public void OnIMGUI()
    {
        fullSupportedGames ??= ResourceRepository.RemoteInfo.Resources.Where(kv => kv.Value.IsFullySupported).Select(kv => kv.Key).ToHashSet();

        ShowSettingsMenu(ref isShow);
        if (!isShow) {
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }
    }

    private void ShowSettingsMenu(ref bool isShow)
    {
        ImGui.SetNextWindowSize(new Vector2(800, 500), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Settings", ref isShow)) {
            ImGui.BeginChild("GroupList", new Vector2(200, 0), ImGuiChildFlags.Borders);
            ShowGroupList();
            ImGui.EndChild();

            ImGui.SameLine();
            ImGui.BeginChild("SubGroupContent", new Vector2(0, 0), ImGuiChildFlags.Borders);
            if (selectedGroup >= 0) {
                ShowSubGroupContent(groups[selectedGroup]);
            }
            ImGui.EndChild();
        }
        ImGui.End();
    }
    private void ShowGroupList()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 20);

        for (int i = 0; i < groups.Count; i++) {
            var group = groups[i];
            bool isGroupSelected = selectedGroup == i;

            if (isGroupSelected) {
                ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered));
            } else {
                ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.Text));
            }
            bool openSubGroup = ImGui.TreeNodeEx(group.Name, ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.FramePadding);
            ImGui.PopStyleColor();

            if (ImGui.IsItemClicked()) {
                selectedGroup = i;
                selectedSubGroup = 0;
            }

            if (openSubGroup) {
                for (int j = 0; j < group.SubGroups.Count; j++) {
                    var sub = group.SubGroups[j];
                    bool isSubSelected = isGroupSelected && selectedSubGroup == j;

                    if (isSubSelected) {
                        ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered));
                    } else {
                        ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.Text));
                    }
                    ImGui.Bullet();
                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                    if (ImGui.Selectable(sub.Name)) {
                        selectedGroup = i;
                        selectedSubGroup = j;
                    }
                }
                ImGui.TreePop();
            }
            ImGui.Separator();
        }
        ImGui.PopStyleVar(2);
    }
    private void ShowSubGroupContent(SettingGroup group)
    {
        if (ImGui.BeginTabBar("##SubGroupTabs")) {
            for (int i = 0; i < group.SubGroups.Count; i++) {
                bool open = true;
                var sub = group.SubGroups[i];
                var tabFlags = (selectedSubGroup == i) ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.NoCloseWithMiddleMouseButton;
                // This overload forces the close button onto tabs... annoying...
                if (ImGui.BeginTabItem(sub.Name, ref open, tabFlags)) {
                    switch (sub.ID) {
                        case SubGroupID.Preferences_General:
                            ShowPreferencesGeneralTab();
                            break;
                        case SubGroupID.Display_General:
                            ShowDisplayGeneralTab();
                            break;
                        case SubGroupID.Display_Theme:
                            ShowDisplayThemeTab();
                            break;
                        case SubGroupID.Hotkeys_Global:
                            ShowHotkeysGlobalTab();
                            break;
                        case SubGroupID.Games_ResidentEvil:
                            ShowGamesResidentEvilTab();
                            break;
                        case SubGroupID.Games_MonsterHunter:
                            ShowGamesMonsterHunterTab();
                            break;
                        case SubGroupID.Games_Other:
                            ShowGamesOtherTab();
                            break;
                        case SubGroupID.Games_Custom:
                            ShowGamesCustomTab();
                            break;
                        default:
                            ImGui.Text("Lorem Ipsum");
                            break;
                    }
                    ImGui.EndTabItem();
                }
                if (ImGui.IsItemClicked()) {
                    selectedSubGroup = i;
                }
            }
            ImGui.EndTabBar();
        }
    }
    private static void ShowPreferencesGeneralTab()
    {
        ImGui.Spacing();

        ShowSetting(config.EnableUpdateCheck, "Automatically check for Updates", "Will occasionally check GitHub for new releases.");

        var configPath = config.GameConfigBaseFilepath.Get();
        if (AppImguiHelpers.InputFolder("Game Config Base Path", ref configPath)) {
            if (configPath.EndsWith(".exe")) configPath = Path.GetDirectoryName(configPath)!;
            config.GameConfigBaseFilepath.Set(configPath);
        }
        ImguiHelpers.Tooltip("The folder path that contains the game specific entity configurations. Will use relative path config/ by default if unspecified.");
        ShowSetting(config.RemoteDataSource, "Resource data source", "The source from which to check for updates and download game-specific resource cache files.\nWill use the default GitHub repository if unspecified.");

        ShowFolderSetting(config.ThumbnailCacheFilepath, "Thumbnail cache file path", "The folder that cached thumbnails should be stored in. Must not be empty.");
        ShowSlider(config.UnpackMaxThreads, "Max unpack threads", 1, 64, "The maximum number of threads to be used when unpacking.\nThe actual thread count is determined automatically by the .NET runtime.");

        ShowSetting(config.LogToFile, "Output logs to file", $"If checked, any logging will also be output to file {FileLogger.DefaultLogFilePath}.\nChanging this setting requires a restart of the app.");
        var logLevel = config.LogLevel.Get();
        if (ImGui.Combo("Minimum logging level", ref logLevel, LogLevels, LogLevels.Length)) {
            config.LogLevel.Set(logLevel);
        }

        var maxUndo = config.MaxUndoSteps.Get();
        if (ImGui.DragInt("Max undo steps", ref maxUndo, 0.25f, 0)) {
            config.MaxUndoSteps.Set(maxUndo);
        }
        ImguiHelpers.Tooltip("The maximum number of steps you can undo. Higher number means a bit higher memory usage after longer sessions.");
    }
    private static void ShowDisplayGeneralTab()
    {
        ImGui.Spacing();

        ShowSetting(config.PrettyFieldLabels, "Simplify field labels", "Whether to simplify field labels instead of showing the raw field names (e.g. \"Target Object\" instead of \"_TargetObject\").");
        ShowSlider(config.AutoExpandFieldsCount, "Auto-expand field count", 0, 16, "RSZ object fields with less than the defined number of fields will initially auto expand.");

        var showFps = config.ShowFps.Get();
        if (ImGui.Checkbox("Show FPS", ref showFps)) {
            config.ShowFps.Set(showFps);
        }

        ShowSlider(config.MaxFps, "Max FPS", 10, 240, "The maximum FPS for rendering.");
        ShowSlider(config.BackgroundMaxFps, "Max FPS in background", 5, config.MaxFps.Get(), "The maximum FPS when the editor window is not focused.");
    }
    private static void ShowDisplayThemeTab()
    {
        ImGui.Spacing();

        var theme = config.Theme.Get();
        if (ImguiHelpers.ValueCombo("Theme", DefaultThemes.AvailableThemes, DefaultThemes.AvailableThemes, ref theme)) {
            UI.ApplyTheme(theme!);
            config.Theme.Set(theme);
        }
        ImguiHelpers.Tooltip("Custom themes can be configured through Tools > Theme Editor.");

        var bgColor = config.BackgroundColor.Get().ToVector4();
        var isAlpha = bgColor.W < 1;
        if (_wasOriginallyAlphaBg == null) _wasOriginallyAlphaBg = isAlpha;
        if (ImGui.ColorEdit4("Background Color", ref bgColor)) {
            var newColor = ReeLib.via.Color.FromVector4(bgColor);
            config.BackgroundColor.Set(newColor);
            foreach (var wnd in MainLoop.Instance.Windows) {
                wnd.ClearColor = newColor;
            }
        }
        if (isAlpha && _wasOriginallyAlphaBg == false) {
            ImGui.TextColored(Colors.Warning, "Window transparency change will only be applied after restarting the app");
        }
    }
    private static void ShowHotkeysGlobalTab()
    {
        ImGui.Spacing();

        var key = config.Key_Undo.Get();
        if (ImguiKeybinding("Undo", ref key, ref filterKey1)) {
            config.Key_Undo.Set(key);
        }
        if (key.Key != ImGuiKey.Z) ImGui.TextColored(Colors.Warning, "While focused, text inputs will not correctly take this setting into account and still use the default layout keys for undo/redo");

        key = config.Key_Redo.Get();
        if (ImguiKeybinding("Redo", ref key, ref filterKey2)) {
            config.Key_Redo.Set(key);
        }
        if (key.Key != ImGuiKey.Y) ImGui.TextColored(Colors.Warning, "While focused, text inputs will not correctly take this setting into account and still use the default layout keys for undo/redo");

        key = config.Key_Save.Get();
        if (ImguiKeybinding("Save", ref key, ref filterKey2)) {
            config.Key_Save.Set(key);
        }
    }
    private static void ShowGamesResidentEvilTab()
    {
        ImGui.Spacing();
        var reGames = Enum.GetValues<GameName>().Where(g => g.ToString().StartsWith("re")).ToList();
        foreach (var game in reGames) {
            ShowGameSpecificMenu(game.ToString());
        }
        ImGui.Separator();
    }
    private static void ShowGamesMonsterHunterTab()
    {
        ImGui.Spacing();
        var mhGames = Enum.GetValues<GameName>().Where(g => g.ToString().StartsWith("mh")).ToList();
        foreach (var game in mhGames) {
            ShowGameSpecificMenu(game.ToString());
        }
        ImGui.Separator();
    }
    private static void ShowGamesOtherTab()
    {
        ImGui.Spacing();
        var otherGames = Enum.GetValues<GameName>().Where(g => g != GameName.unknown && !g.ToString().StartsWith("re") && !g.ToString().StartsWith("mh")).OrderBy(g => g.ToString());
        foreach (var game in otherGames) {
            ShowGameSpecificMenu(game.ToString());
        }
        ImGui.Separator();
    }
    private void ShowGamesCustomTab()
    {
        ImGui.Spacing();
        ImGui.SeparatorText("Add Custom Game");
        ImGui.InputText("Short Name", ref customGameNameInput, 20);
        AppImguiHelpers.InputFolder("Game Path", ref customGameFilepath);
        if (!string.IsNullOrEmpty(customGameNameInput) && !string.IsNullOrEmpty(customGameFilepath) && Directory.Exists(customGameFilepath)) {
            ImGui.SameLine();
            if (ImGui.Button("Add")) {
                config.SetGamePath(customGameNameInput, customGameFilepath);
            }
        }
        ImGui.SeparatorText("Custom Games");

        var customGames = AppConfig.Instance.GetGamelist().Where(g => !Enum.TryParse<GameName>(g.name, true, out _)).Select(g => g.name);
        foreach (var game in customGames) {
            ShowGameSpecificMenu(game, true);
        }
    }
    private static void ShowGameSpecificMenu(string gameShortName, bool isCustom = false)
    {
        GameIdentifier game = gameShortName;
        if (ImGui.TreeNodeEx(Languages.TranslateGame(game.name), ImGuiTreeNodeFlags.Framed)) {
            ImGui.Spacing();
            var gamepath = config.GetGamePath(game);
            if (AppImguiHelpers.InputFolder("Game Path", ref gamepath)) {
                if (gamepath.EndsWith(".exe")) gamepath = Path.GetDirectoryName(gamepath)!;
                config.SetGamePath(game, gamepath);
            }
            ImguiHelpers.Tooltip("The full path to the game. Should point to the folder containing the .exe and .pak files");

            var extractPath = config.GetGameExtractPath(game);
            if (AppImguiHelpers.InputFolder("Game Extract Path", ref extractPath)) {
                extractPath = PathUtils.RemoveNativesFolder(extractPath);
                config.SetGameExtractPath(game, extractPath);
            }
            ImguiHelpers.Tooltip("The default path to preselect when extracting files.");

            ImGui.Spacing();

            var rszPath = config.GetGameRszJsonPath(game);
            var filelist = config.GetGameFilelist(game);

            if (AppImguiHelpers.InputFilepath("File List", ref filelist, "List file|*.list;*.txt|Any|*.*")) {
                config.SetGameFilelist(game, filelist);
            }
            ImguiHelpers.Tooltip("Defining a custom path here may not be required if it's at least a partially supported game.\nCan also be used in case of issues with automatic downloads.");

            if (AppImguiHelpers.InputFilepath("RSZ Template JSON Path", ref rszPath, "JSON file|*.json")) {
                config.SetGameRszJsonPath(game, rszPath);
            }
            if (isCustom) {
                ImGui.PushStyleColor(ImGuiCol.Text, Colors.Warning);
                ImGui.TextWrapped("*This is a custom defined game. The app may need an upgrade to fully support all files, some files may not load correctly.");
                ImGui.PopStyleColor();
            } else if (fullSupportedGames != null && fullSupportedGames.Contains(game.name)) {
                ImGui.PushStyleColor(ImGuiCol.Text, Colors.Info);
                ImGui.TextWrapped("*This is a fully supported game, game specific data can be fetched automatically.");
                ImGui.PopStyleColor();
            }
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Info);
            ImGui.TextWrapped("*Changes to these settings may require a restart of the app before they get applied");
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.TreePop();
        }
    }
    private static void ShowSetting(AppConfig.ClassSettingWrapper<string> setting, string label, string? tooltip)
    {
        var remoteSource = setting.Get() ?? "";
        if (ImGui.InputText(label, ref remoteSource, 280)) {
            setting.Set(remoteSource);
        }
        if (tooltip != null) { ImguiHelpers.Tooltip(tooltip); }
    }
    private static void ShowSetting(AppConfig.SettingWrapper<bool> setting, string label, string? tooltip)
    {
        var value = setting.Get();
        if (ImGui.Checkbox(label, ref value)) {
            setting.Set(value);
        }
        if (tooltip != null) { ImguiHelpers.Tooltip(tooltip); }
    }
    private static void ShowSlider(AppConfig.SettingWrapper<int> setting, string label, int min, int max, string? tooltip)
    {
        var value = setting.Get();
        if (ImGui.SliderInt(label, ref value, min, max)) {
            setting.Set(value);
        }
        if (tooltip != null) { ImguiHelpers.Tooltip(tooltip); }
    }
    private static void ShowFolderSetting(AppConfig.ClassSettingWrapper<string> setting, string label, string? tooltip)
    {
        var configPath = setting.Get();
        if (AppImguiHelpers.InputFolder(label, ref configPath)) {
            setting.Set(configPath);
        }
        if (tooltip != null) { ImguiHelpers.Tooltip(tooltip); }
    }
    private static bool ImguiKeybinding(string label, ref KeyBinding binding, ref string? filter)
    {
        ImGui.PushID(label);
        var changed = false;
        ImGui.PushItemWidth(50);
        changed = ImGui.Checkbox("Ctrl", ref binding.ctrl);
        ImGui.SameLine();
        changed = ImGui.Checkbox("Shift", ref binding.shift) || changed;
        ImGui.SameLine();
        changed = ImGui.Checkbox("Alt", ref binding.alt) || changed;
        ImGui.SameLine();
        ImGui.PopItemWidth();
        ImGui.SetNextItemWidth(ImGui.CalcItemWidth() - 150);
        changed = ImguiHelpers.FilterableCSharpEnumCombo<ImGuiKey>(label, ref binding.Key, ref filter) || changed;
        ImGui.PopID();
        return changed;
    }
    public bool RequestClose()
    {
        return false;
    }
}
