using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Themes;
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
    private static string filterKey1 = "", filterKey2 = "", filterKey3 = "", filterKey4 = "";
    private string customGameNameInput = "", customGameFilepath = "";
    private static HashSet<string>? fullSupportedGames;
    private enum SubGroupID
    {
        Preferences_General,
        Preferences_Editing,
        Preferences_Bundles,
        Display_General,
        Display_Theme,
        Hotkeys_Global,
        Hotkeys_PakBrowser,
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
                new SettingSubGroup { Name = "Editing", ID = SubGroupID.Preferences_Editing},
                new SettingSubGroup { Name = "Bundles", ID = SubGroupID.Preferences_Bundles},
            }
        },
        new SettingGroup { Name = "Display", SubGroups = {
                new SettingSubGroup { Name = "General", ID = SubGroupID.Display_General},
                new SettingSubGroup { Name = "Theme", ID = SubGroupID.Display_Theme},
            }
        },
        new SettingGroup { Name = "Hotkeys", SubGroups = {
                new SettingSubGroup { Name = "Global", ID = SubGroupID.Hotkeys_Global},
                new SettingSubGroup { Name = "Pak Browser", ID = SubGroupID.Hotkeys_PakBrowser},
                // SILVER: More of a TODO thing but there should be contextual hotkeys for the editors
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
    public void OnWindow() => this.ShowDefaultWindow(context);
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
        ImGui.BeginChild("GroupList", new Vector2(200 * UI.UIScale, 0), ImGuiChildFlags.Borders);
        ShowGroupList();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("SubGroupContent", new Vector2(0, 0), ImGuiChildFlags.Borders);
        if (selectedGroup >= 0) {
            ShowSubGroupContent(groups[selectedGroup]);
        }
        ImGui.EndChild();
    }
    private void ShowGroupList()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 20);

        for (int i = 0; i < groups.Count; i++) {
            var group = groups[i];
            bool isGroupSelected = selectedGroup == i;

            if (isGroupSelected) {
                ImGui.PushStyleColor(ImGuiCol.Text, Colors.TextActive);
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
                        ImGui.PushStyleColor(ImGuiCol.Text, Colors.TextActive);
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
                        case SubGroupID.Preferences_Editing:
                            ShowPreferencesEditingTab();
                            break;
                        case SubGroupID.Preferences_Bundles:
                            ShowBundlesEditingTab();
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
                        case SubGroupID.Hotkeys_PakBrowser:
                            ShowHotkeysPakBrowserTab();
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

        ShowFolderSetting(config.CacheFilepath, "Cache file path", "The folder to use for general file caching. Must not be empty.");
        ShowFolderSetting(config.ThumbnailCacheFilepath, "Thumbnail cache file path", "The folder that cached thumbnails should be stored in. Must not be empty.");
        ShowSlider(config.UnpackMaxThreads, "Max unpack threads", 1, 64, "The maximum number of threads to be used when unpacking.\nThe actual thread count is determined automatically by the .NET runtime.");

        var navchanged = ShowSetting(config.EnableKeyboardNavigation, "Enable keyboard navigation", "Whether to enable navigating between fields using arrow keys.");
        if (navchanged) {
            if (config.EnableKeyboardNavigation) {
                ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            } else {
                ImGui.GetIO().ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;
            }
        }
        ShowSetting(config.EnableGpuTexCompression, "Enable GPU texture compression", "Whether to enable using the much faster GPU-based compression method.\nCurrently only available on Windows.\nCan be disabled in case of issues, so that CPU-based compression is used instead.");
        ShowSetting(config.LogToFile, "Output logs to file", $"If checked, any logging will also be output to file {FileLogger.DefaultLogFilePath}.\nChanging this setting requires a restart of the app.");
        ShowSetting(config.LoadFromNatives, "Load files from natives/ folder", $"If checked, the app will prefer to load loose files from the active game's natives folder instead of packed files, similar to how the game would.");
        var logLevel = config.LogLevel.Get();
        if (ImGui.Combo("Minimum logging level", ref logLevel, LogLevels, LogLevels.Length)) {
            config.LogLevel.Set(logLevel);
        }
    }
    private static void ShowPreferencesEditingTab()
    {
        ImGui.Spacing();

        var maxUndo = config.MaxUndoSteps.Get();
        if (ImGui.DragInt("Max undo steps", ref maxUndo, 0.25f, 0)) {
            config.MaxUndoSteps.Set(maxUndo);
        }
        ImguiHelpers.Tooltip("The maximum number of steps you can undo. Higher number means a bit higher memory usage after longer sessions.");
    }

    private static void ShowBundlesEditingTab()
    {
        ImGui.Spacing();
        ShowSetting(config.BundleDefaultSaveFullPath, "Save bundle files with full path", "When checked, will always default to saving with the full relative path instead of the root bundle folder when adding new files to the active bundle.");

        var defaults = config.JsonSettings.BundleDefaults;

        var str = defaults.Author ?? "";
        if (ImGui.InputText("Author", ref str, 100)) {
            defaults.Author = str;
            config.SaveJsonConfig();
        }

        str = defaults.Description ?? "";
        if (ImGui.InputText("Description", ref str, 100)) {
            defaults.Description = str;
            config.SaveJsonConfig();
        }

        str = defaults.Homepage ?? "";
        if (ImGui.InputText("Homepage", ref str, 100)) {
            defaults.Homepage = str;
            config.SaveJsonConfig();
        }
    }
    private static void ShowDisplayGeneralTab()
    {
        ImGui.Spacing();

        ShowSlider(config.FontSize, "UI Font Size", 10, 128, "The default font size for drawing text.");
        if (UI.FontSize != config.FontSize.Get()) {
            if (ImGui.Button("Update UI")) {
                UI.FontSize = config.FontSize.Get();
                UI.FontSizeLarge = UI.FontSize * UI.FontSizeLargeMultiplier;
            }
        }
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

        if (ImGui.Button("Open Theme Editor")) {
            EditorWindow.CurrentWindow?.AddUniqueSubwindow(new ThemeEditor());
        }

        ImGui.Spacing();
        var theme = config.Theme.Get();
        if (ImguiHelpers.ValueCombo("Theme", DefaultThemes.AvailableThemes, DefaultThemes.AvailableThemes, ref theme)) {
            UI.ApplyTheme(theme!);
            config.Theme.Set(theme);
        }

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
    private void ShowHotkeysGlobalTab()
    {
        ImGui.Spacing();

        ImguiKeybinding("Undo", config.Key_Undo);
        if (config.Key_Undo.Get().Key != ImGuiKey.Z) ImGui.TextColored(Colors.Warning, "While focused, text inputs will not correctly take this setting into account and still use the default layout keys for undo/redo");

        ImguiKeybinding("Redo", config.Key_Redo);
        if (config.Key_Redo.Get().Key != ImGuiKey.Y) ImGui.TextColored(Colors.Warning, "While focused, text inputs will not correctly take this setting into account and still use the default layout keys for undo/redo");

        ImguiKeybinding("Save Open Files", config.Key_Save);
        ImguiKeybinding("Back", config.Key_Back);
        ImguiKeybinding("Close Current Window", config.Key_Close);
    }
    private void ShowHotkeysPakBrowserTab()
    {
        ImGui.Spacing();
        ImguiKeybinding("Open Bookmarks", config.Key_PakBrowser_OpenBookmarks);
        ImguiKeybinding("Bookmark Current Path", config.Key_PakBrowser_Bookmark);
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

            if (AppImguiHelpers.InputFilepath("File List", ref filelist, FileFilters.ListFile)) {
                config.SetGameFilelist(game, filelist);
            }
            ImguiHelpers.Tooltip("Defining a custom path here may not be required if it's at least a partially supported game.\nCan also be used in case of issues with automatic downloads.");

            var isFullySupported = fullSupportedGames?.Contains(game.name) == true;
            if (AppImguiHelpers.InputFilepath(isFullySupported ? "Custom RSZ JSON Path" : "RSZ Template JSON Path", ref rszPath, FileFilters.JsonFile)) {
                config.SetGameRszJsonPath(game, rszPath);
                WindowHandlerFactory.ResetGameTypes(game);
                Component.ResetGameTypes(game);
            }
            ImguiHelpers.Tooltip(isFullySupported
                ? "The default RSZ json file is fetched automatically.\nChange this only if you know what you're doing - mainly for accessing files from older game versions"
                : "For not yet fully supported games, you may need to manually provide the path to a valid RSZ JSON template before some files can be opened.");
            if (isCustom) {
                ImGui.PushStyleColor(ImGuiCol.Text, Colors.Warning);
                ImGui.TextWrapped("*This is a custom defined game. The app may need an upgrade to fully support all files, some files may not load correctly.");
                ImGui.PopStyleColor();
            } else if (isFullySupported) {
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
    private static bool ShowSetting(AppConfig.SettingWrapper<bool> setting, string label, string? tooltip)
    {
        var value = setting.Get();
        var changed = false;
        if (ImGui.Checkbox(label, ref value)) {
            setting.Set(value);
            changed = true;
        }
        if (tooltip != null) { ImguiHelpers.Tooltip(tooltip); }
        return changed;
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
    private Dictionary<AppConfig.SettingWrapper<KeyBinding>, string> keyfilters = new();

    private bool ImguiKeybinding(string label, AppConfig.SettingWrapper<KeyBinding> setting)
    {
        var key = setting.Get();
        var filter = keyfilters.GetValueOrDefault(setting) ?? "";
        ImGui.PushID(label);
        var changed = false;
        ImGui.PushItemWidth(50);
        changed = ImGui.Checkbox("Ctrl", ref key.ctrl);
        ImGui.SameLine();
        changed = ImGui.Checkbox("Shift", ref key.shift) || changed;
        ImGui.SameLine();
        changed = ImGui.Checkbox("Alt", ref key.alt) || changed;
        ImGui.SameLine();
        ImGui.PopItemWidth();
        ImGui.SetNextItemWidth(ImGui.CalcItemWidth() - 150);
        changed = ImguiHelpers.FilterableCSharpEnumCombo<ImGuiKey>(label, ref key.Key, ref filter) || changed;
        ImGui.PopID();
        keyfilters[setting] = filter;
        if (changed) {
            setting.Set(key);
        }
        return changed;
    }
    public bool RequestClose()
    {
        return false;
    }
}
