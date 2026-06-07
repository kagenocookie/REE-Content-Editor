using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Internal;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Themes;
using ContentPatcher;
using ReeLib;
using ReeLib.Msg;

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
    private static readonly string[] DateFormats = ["DD/MM/YYYY [ EU ]", "MM/DD/YYYY [ US ]", "YYYY/MM/DD [ JP ]"];
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
        Hotkeys_MeshViewer,
        Hotkeys_TextureViewer,
        Hotkeys_Scene,
        Hotkeys_UVSEditor,
        Games_ResidentEvil,
        Games_MonsterHunter,
        Games_Other,
        Games_Custom
    }
    private class SettingGroup
    {
        public required FixedString Name { get; set; }
        public List<SettingSubGroup> SubGroups { get; set; } = new();
    }
    private class SettingSubGroup
    {
        public required FixedString Name { get; set; }
        public required SubGroupID ID { get; set; }
    }

    private readonly List<SettingGroup> groups = new()
    {
        new SettingGroup { Name = Lang.Settings.Group_Preferences, SubGroups = {
                new SettingSubGroup { Name = Lang.Settings.Group_General, ID = SubGroupID.Preferences_General},
                new SettingSubGroup { Name = Lang.Settings.Group_Editing, ID = SubGroupID.Preferences_Editing},
                new SettingSubGroup { Name = Lang.Settings.Group_Bundles, ID = SubGroupID.Preferences_Bundles},
            }
        },
        new SettingGroup { Name = Lang.Settings.Group_Display, SubGroups = {
                new SettingSubGroup { Name = Lang.Settings.Group_General, ID = SubGroupID.Display_General},
                new SettingSubGroup { Name = Lang.Settings.Group_Theme, ID = SubGroupID.Display_Theme},
            }
        },
        new SettingGroup { Name = Lang.Settings.Group_Hotkeys, SubGroups = {
                new SettingSubGroup { Name = Lang.Settings.Group_Global, ID = SubGroupID.Hotkeys_Global},
                new SettingSubGroup { Name = Lang.Settings.Group_Pak, ID = SubGroupID.Hotkeys_PakBrowser},
                new SettingSubGroup { Name = Lang.Settings.Group_Scene, ID = SubGroupID.Hotkeys_Scene},
                new SettingSubGroup { Name = Lang.Settings.Group_Mesh, ID = SubGroupID.Hotkeys_MeshViewer},
                new SettingSubGroup { Name = Lang.Settings.Group_Texture, ID = SubGroupID.Hotkeys_TextureViewer},
                new SettingSubGroup { Name = Lang.Settings.Group_UVS, ID = SubGroupID.Hotkeys_UVSEditor},
            }
        },
        new SettingGroup { Name = Lang.Settings.Group_Games, SubGroups = {
                new SettingSubGroup { Name = Lang.Settings.Group_Resident, ID = SubGroupID.Games_ResidentEvil},
                new SettingSubGroup { Name = Lang.Settings.Group_Monster, ID = SubGroupID.Games_MonsterHunter},
                new SettingSubGroup { Name = Lang.Settings.Group_Other, ID = SubGroupID.Games_Other},
                new SettingSubGroup { Name = Lang.Settings.Group_Custom, ID = SubGroupID.Games_Custom}
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
        fullSupportedGames ??= ResourceRepository.RemoteInfo.Resources.Where(kv => kv.Value.IsRSZFullySupported).Select(kv => kv.Key).ToHashSet();

        ShowSettingsMenu(ref isShow);
        if (!isShow) {
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }
    }

    private void ShowSettingsMenu(ref bool isShow)
    {
        ImGui.BeginChild("GroupList"u8, new Vector2(200 * UI.UIScale, 0), ImGuiChildFlags.Borders);
        ShowGroupList();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("SubGroupContent"u8, new Vector2(0, 0), ImGuiChildFlags.Borders);
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

            ImGui.PushStyleColor(ImGuiCol.Text, isGroupSelected ? Colors.TextActive : ImguiHelpers.GetColor(ImGuiCol.Text));
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

                    ImGui.PushStyleColor(ImGuiCol.Text, isSubSelected ? Colors.TextActive : ImguiHelpers.GetColor(ImGuiCol.Text));
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
        int tabsPerRow = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / 110));
        for (int row = 0; row < group.SubGroups.Count; row += tabsPerRow) {
            bool isOnThisRow = selectedSubGroup >= row && selectedSubGroup < row + tabsPerRow;
            if (ImGui.BeginTabBar($"##SubGroupTabs{row}", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton | ImGuiTabBarFlags.DrawSelectedOverline)) {
                ImGui.PushStyleColor(ImGuiCol.TabSelected, isOnThisRow ? ImguiHelpers.GetColor(ImGuiCol.TabSelected) : ImguiHelpers.GetColor(ImGuiCol.Tab));
                ImGui.PushStyleColor(ImGuiCol.TabSelectedOverline, isOnThisRow ? ImguiHelpers.GetColor(ImGuiCol.TabSelectedOverline) : ImguiHelpers.GetColor(ImGuiCol.Tab));
                for (int i = row; i < Math.Min(row + tabsPerRow, group.SubGroups.Count); i++) {
                    bool open = true;
                    var flags = (isOnThisRow && selectedSubGroup == i) ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;

                    ImGui.PushID(i);
                    if (ImGui.BeginTabItem(group.SubGroups[i].Name, ref open, flags)) {
                        ImGui.EndTabItem();
                    }
                    if (ImGui.IsItemClicked()) {
                        selectedSubGroup = i;
                    }
                    ImGui.PopID();
                }
                ImGui.PopStyleColor(2);
                ImGui.EndTabBar();
            }
        }

        if (selectedSubGroup >= 0 && selectedSubGroup < group.SubGroups.Count) {
            ShowSubGroupTabContent(group.SubGroups[selectedSubGroup].ID);
        }
    }
    private void ShowSubGroupTabContent(SubGroupID id)
    {
        switch (id) {
            case SubGroupID.Preferences_General: ShowPreferencesGeneralTab(); break;
            case SubGroupID.Preferences_Editing: ShowPreferencesEditingTab(); break;
            case SubGroupID.Preferences_Bundles: ShowBundlesEditingTab(); break;
            case SubGroupID.Display_General: ShowDisplayGeneralTab(); break;
            case SubGroupID.Display_Theme: ShowDisplayThemeTab(); break;
            case SubGroupID.Hotkeys_Global: ShowHotkeysGlobalTab(); break;
            case SubGroupID.Hotkeys_PakBrowser: ShowHotkeysPakBrowserTab(); break;
            case SubGroupID.Hotkeys_MeshViewer: ShowHotkeysMeshViewerTab(); break;
            case SubGroupID.Hotkeys_TextureViewer: ShowHotkeysTextureViewerTab(); break;
            case SubGroupID.Hotkeys_Scene: ShowHotkeysSceneTab(); break;
            case SubGroupID.Hotkeys_UVSEditor: ShowHotkeysUVSEditorTab(); break;
            case SubGroupID.Games_ResidentEvil: ShowGamesResidentEvilTab(); break;
            case SubGroupID.Games_MonsterHunter: ShowGamesMonsterHunterTab(); break;
            case SubGroupID.Games_Other: ShowGamesOtherTab(); break;
            case SubGroupID.Games_Custom: ShowGamesCustomTab(); break;
            default: ImGui.Text("Lorem Ipsum"); break;
        }
    }

    private static void ShowPreferencesGeneralTab()
    {
        ImGui.Spacing();
        var lang = config.PreferredLanguage.Get();
        if (ImGui.Combo(Lang.Settings.PreferredLanguage, ref lang, Lang.SupportableLanguageNames)) {
            var newLang = (Language)lang;
            Lang.ChangeLanguage(newLang);
            AppConfig.Instance.PreferredLanguage.Set(lang);
        }

        ShowSetting(config.EnableUpdateCheck, Lang.Settings.AutoUpdate);
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(AutoUpdater.UpdateCheckInProgress)) {
            if (ImGui.Button(Lang.Buttons.CheckUpdates)) {
                AutoUpdater.CheckForUpdateInBackground();
            }
        }
        ShowSetting(config.DisableFileCloseWarning, Lang.Settings.DisableFileCloseWarning);
        var navchanged = ShowSetting(config.EnableKeyboardNavigation, Lang.Settings.EnableKeyboardNav);
        if (navchanged) {
            if (config.EnableKeyboardNavigation) {
                ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            } else {
                ImGui.GetIO().ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;
            }
        }
        ShowSetting(config.LoadFromNatives, Lang.Settings.LoadFromNatives);
        ShowSetting(config.UseSubPakForLooseTextures, Lang.Settings.UseSubPakForLooseTextures);

        ImGui.SeparatorText(Lang.Settings.Section_Cache);
        var configPath = config.GameConfigBaseFilepath.Get();
        if (AppImguiHelpers.InputFolder(Lang.Settings.GameConfigBasePath.Text, ref configPath)) {
            if (configPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) configPath = Path.GetDirectoryName(configPath)!;
            config.GameConfigBaseFilepath.Set(configPath);
        }
        ImguiHelpers.Tooltip(Lang.Settings.GameConfigBasePath.Tooltip);
        ShowSetting(config.RemoteDataSource, Lang.Settings.RemoteDataSource);

        ShowFolderSetting(config.ResourcesFilepath, Lang.Settings.ResourcesFilepath);
        ShowFolderSetting(config.CacheFilepath, Lang.Settings.CacheFilepath);
        ShowFolderSetting(config.ThumbnailCacheFilepath, Lang.Settings.ThumbnailCacheFilepath);
        ShowFolderSetting(config.BookmarksFilepath, Lang.Settings.BookmarksFilepath);

        ImGui.SeparatorText(Lang.Settings.Section_Performance);
        ShowSlider(config.UnpackMaxThreads, Lang.Settings.MaxUnpackThreads, 1, 64);
        ShowSetting(config.EnableGpuTexCompression, Lang.Settings.EnableGpuTexCompression);

        ImGui.SeparatorText(Lang.Settings.Section_Debug);
        ShowSetting(config.LogToFile, Lang.Settings.LogToFile, Lang.Settings.LogToFile_Tooltip.UTF8);
        var logLevel = config.LogLevel.Get();
        if (ImGui.Combo(Lang.Settings.MinLogLevel.String, ref logLevel, LogLevels, LogLevels.Length)) {
            config.LogLevel.Set(logLevel);
        }
    }

    private static void ShowPreferencesEditingTab()
    {
        ImGui.Spacing();

        var maxUndo = config.MaxUndoSteps.Get();
        if (ImGui.DragInt(Lang.Settings.MaxUndoSteps.Text, ref maxUndo, 0.25f, 0)) {
            config.MaxUndoSteps.Set(maxUndo);
        }
        ImguiHelpers.Tooltip(Lang.Settings.MaxUndoSteps.Tooltip);

        ShowSetting(config.ShowQuaternionsAsEuler, Lang.Settings.ShowQuaternionsAsEuler);
        ShowSetting(config.QuaternionsDisableAutoNormalize, Lang.Settings.QuaternionsDisableAutoNormalize);
        ShowSetting(config.PauseAnimPlayerOnSeek, Lang.Settings.PauseAnimPlayerOnSeek);
    }

    private static void ShowBundlesEditingTab()
    {
        ImGui.Spacing();
        ShowSetting(config.BundleDefaultSaveFullPath, Lang.Settings.BundleDefaultSaveFullPath);

        ImGui.SeparatorText(Lang.Settings.Section_BundleDefaults);
        var defaults = config.JsonSettings.BundleDefaults;

        var str = defaults.Author ?? "";
        if (ImGui.InputText(Lang.Settings.Author, ref str, 100)) {
            defaults.Author = str;
            config.SaveJsonConfig();
        }

        str = defaults.Description ?? "";
        if (ImGui.InputText(Lang.Settings.Description, ref str, 100)) {
            defaults.Description = str;
            config.SaveJsonConfig();
        }

        str = defaults.Homepage ?? "";
        if (ImGui.InputText(Lang.Settings.Homepage, ref str, 100)) {
            defaults.Homepage = str;
            config.SaveJsonConfig();
        }
    }
    private static void ShowDisplayGeneralTab()
    {
        ImGui.Spacing();

        ShowSlider(config.FontSize, Lang.Settings.FontSize, 10, 128);
        if (UI.FontSize != config.FontSize.Get()) {
            if (ImGui.Button(Lang.Settings.Button_UpdateUI)) {
                UI.FontSize = config.FontSize.Get();
                UI.FontSizeLarge = UI.FontSize * UI.FontSizeLargeMultiplier;
            }
        }
        ShowSetting(config.UseFullscreenAnimPlayback, Lang.Settings.UseFullscreenAnimPlayback);

        ImGui.SeparatorText(Lang.Settings.Section_Fields);
        ShowSetting(config.PrettyFieldLabels, Lang.Settings.PrettyFieldLabels);
        ShowSlider(config.AutoExpandFieldsCount, Lang.Settings.AutoExpand, 0, 16);

        ImGui.SeparatorText(Lang.Settings.Section_FPS);
        var showFps = config.ShowFps.Get();
        if (ImGui.Checkbox(Lang.Settings.ShowFPS, ref showFps)) {
            config.ShowFps.Set(showFps);
        }
        ShowSlider(config.MaxFps, Lang.Settings.MaxFPS, 10, 240);
        ShowSlider(config.BackgroundMaxFps, Lang.Settings.MaxFPSBackground, 5, config.MaxFps.Get());

        ImGui.SeparatorText(Lang.Settings.Section_DateTime);
        var dateFormat = config.DateFormat.Get();
        if (ImGui.Combo(Lang.Settings.DateFormat.String, ref dateFormat, DateFormats, DateFormats.Length)) {
            config.DateFormat.Set(dateFormat);
        }
        ShowSetting(config.ClockFormat, Lang.Settings.ClockFormat);
    }
    private static void ShowDisplayThemeTab()
    {
        ImGui.Spacing();

        if (ImGui.Button(Lang.Settings.Button_OpenThemeEditor)) {
            EditorWindow.CurrentWindow?.AddUniqueSubwindow(new ThemeEditor());
        }

        ImGui.Spacing();
        var theme = config.Theme.Get();
        if (ImguiHelpers.ValueCombo(Lang.Settings.Theme.String, DefaultThemes.AvailableThemes, DefaultThemes.AvailableThemes, ref theme)) {
            UI.ApplyTheme(theme!);
            config.Theme.Set(theme);
        }

        var bgColor = config.BackgroundColor.Get().ToVector4();
        var isAlpha = bgColor.W < 1;
        if (_wasOriginallyAlphaBg == null) _wasOriginallyAlphaBg = isAlpha;
        if (ImGui.ColorEdit4(Lang.Settings.BackgroundColor.String, ref bgColor)) {
            var newColor = ReeLib.via.Color.FromVector4(bgColor);
            config.BackgroundColor.Set(newColor);
            foreach (var wnd in MainLoop.Instance.Windows) {
                wnd.ClearColor = newColor;
                foreach (var scn in (wnd as EditorWindow)?.SceneManager.RootScenes ?? []) {
                    scn.OwnRenderContext.ClearColor = newColor;
                }
            }
        }
        if (isAlpha && _wasOriginallyAlphaBg == false) {
            ImGui.TextColored(Colors.Warning, Lang.Settings.Warn_Transparency);
        }
    }
    private void ShowHotkeysGlobalTab()
    {
        ImGui.Spacing();

        ImguiKeybinding(Lang.Settings.Bind_Undo, config.Key_Undo);
        if (config.Key_Undo.Get().Key != ImGuiKey.Z) ImGui.TextColored(Colors.Warning, Lang.Settings.Warn_UndoRedoBinding);

        ImguiKeybinding(Lang.Settings.Bind_Redo, config.Key_Redo);
        if (config.Key_Redo.Get().Key != ImGuiKey.Y) ImGui.TextColored(Colors.Warning, Lang.Settings.Warn_UndoRedoBinding);

        ImguiKeybinding(Lang.Settings.Bind_Save, config.Key_Save);
        ImguiKeybinding(Lang.Settings.Bind_Open, config.Key_Open);
        ImguiKeybinding(Lang.Settings.Bind_Back, config.Key_Back);
        ImguiKeybinding(Lang.Settings.Bind_Close, config.Key_Close);
        ImguiKeybinding(Lang.Settings.Bind_HomePage, config.Key_HomePage);
        ImguiKeybinding(Lang.Settings.Bind_OpenPakBrowser, config.Key_OpenPakBrowser);
        ImguiKeybinding(Lang.Settings.Bind_OpenMacroShelf, config.Key_OpenMacroShelf);
    }
    private void ShowHotkeysPakBrowserTab()
    {
        ImGui.Spacing();
        ImguiKeybinding(Lang.Settings.Bind_PakBrowser_OpenBookmarks, config.Key_PakBrowser_OpenBookmarks);
        ImguiKeybinding(Lang.Settings.Bind_PakBrowser_Bookmark, config.Key_PakBrowser_Bookmark);
        ImguiKeybinding(Lang.Settings.Bind_PakBrowser_JumpToPageTop, config.Key_PakBrowser_JumpToPageTop);
    }
    private void ShowHotkeysMeshViewerTab()
    {
        ImGui.Spacing();
        ImGui.SeparatorText(Lang.Settings.Section_Animator);
        ImguiKeybinding(Lang.Settings.Bind_MeshViewer_PauseAnim, config.Key_MeshViewer_PauseAnim);
        ImguiKeybinding(Lang.Settings.Bind_MeshViewer_NextAnimFrame, config.Key_MeshViewer_NextAnimFrame);
        ImguiKeybinding(Lang.Settings.Bind_MeshViewer_PrevAnimFrame, config.Key_MeshViewer_PrevAnimFrame);
        ImguiKeybinding(Lang.Settings.Bind_MeshViewer_IncreaseAnimSpeed, config.Key_MeshViewer_IncreaseAnimSpeed);
        ImguiKeybinding(Lang.Settings.Bind_MeshViewer_DecreaseAnimSpeed, config.Key_MeshViewer_DecreaseAnimSpeed);
    }
    private void ShowHotkeysTextureViewerTab()
    {
        ImGui.Spacing();
        ImguiKeybinding(Lang.Settings.Bind_TextureViewer_ResetView, config.Key_TextureViewer_ResetView);
        ImguiKeybinding(Lang.Settings.Bind_TextureViewer_ZoomIn, config.Key_TextureViewer_ZoomIn);
        ImguiKeybinding(Lang.Settings.Bind_TextureViewer_ZoomOut, config.Key_TextureViewer_ZoomOut);
    }
    private void ShowHotkeysSceneTab()
    {
        ImGui.Spacing();
        ImguiKeybinding(Lang.Settings.Bind_Scene_Focus3D, config.Key_Scene_Focus3D);
        ImguiKeybinding(Lang.Settings.Bind_Scene_FocusUI, config.Key_Scene_FocusUI);
        ImguiKeybinding(Lang.Settings.Bind_Scene_Hide, config.Key_Scene_Hide);
        ImguiKeybinding(Lang.Settings.Bind_Scene_UnhideAll, config.Key_Scene_UnhideAll);
        ImguiKeybinding(Lang.Settings.Bind_Scene_Delete, config.Key_Scene_Delete);
    }
    private void ShowHotkeysUVSEditorTab()
    {
        ImGui.Spacing();
        ImguiKeybinding(Lang.Settings.Bind_UVS_Pause, config.Key_UVS_Pause);
        ImguiKeybinding(Lang.Settings.Bind_UVS_NextPattern, config.Key_UVS_NextPattern);
        ImguiKeybinding(Lang.Settings.Bind_UVS_PrevPattern, config.Key_UVS_PrevPattern);
        ImguiKeybinding(Lang.Settings.Bind_UVS_IncreaseSpeed, config.Key_UVS_IncreaseSpeed);
        ImguiKeybinding(Lang.Settings.Bind_UVS_DecreaseSpeed, config.Key_UVS_DecreaseSpeed);
    }

    private void ShowGamesResidentEvilTab()
    {
        ImGui.Spacing();
        var reGames = Enum.GetValues<GameName>().Where(g => g.ToString().StartsWith("re")).ToList();
        foreach (var game in reGames) {
            ShowGameSpecificMenu(game.ToString());
        }
        ImGui.Separator();
    }
    private void ShowGamesMonsterHunterTab()
    {
        ImGui.Spacing();
        var mhGames = Enum.GetValues<GameName>().Where(g => g.ToString().StartsWith("mh")).ToList();
        foreach (var game in mhGames) {
            ShowGameSpecificMenu(game.ToString());
        }
        ImGui.Separator();
    }
    private void ShowGamesOtherTab()
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
        ImGui.SeparatorText(Lang.Settings.Section_AddCustom);
        ImGui.InputText(Lang.Settings.Custom_ShortName, ref customGameNameInput, 20);
        AppImguiHelpers.InputFolder(Lang.Settings.GamePath.Text, ref customGameFilepath);
        if (!string.IsNullOrEmpty(customGameNameInput) && !string.IsNullOrEmpty(customGameFilepath) && Directory.Exists(customGameFilepath)) {
            ImGui.SameLine();
            if (ImGui.Button(Lang.Buttons.Add)) {
                config.SetGamePath(customGameNameInput, customGameFilepath);
            }
        }
        ImGui.SeparatorText(Lang.Settings.CustomGames);

        var customGames = AppConfig.Instance.GetGamelist().Where(g => !Enum.TryParse<GameName>(g.name, true, out _)).Select(g => g.name);
        foreach (var game in customGames) {
            ShowGameSpecificMenu(game, true);
        }
    }
    private void ShowGameSpecificMenu(string gameShortName, bool isCustom = false)
    {
        GameIdentifier game = gameShortName;
        if (ImGui.TreeNodeEx(Lang.TranslateGame(game.name), ImGuiTreeNodeFlags.Framed)) {
            ImGui.Spacing();
            var gamepath = config.GetGamePath(game);
            var gameExe = config.GetGameExecutablePath(game);
            var extractPath = config.GetGameExtractPath(game);
            var rszPath = config.GetGameRszJsonPath(game);
            var filelist = config.GetGameFilelist(game);
            var isFullySupported = fullSupportedGames?.Contains(game.name) == true;

            if (AppImguiHelpers.InputFolder(Lang.Settings.GamePath.Text, ref gamepath)) {
                if (gamepath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) gamepath = Path.GetDirectoryName(gamepath)!;
                config.SetGamePath(game, gamepath);
            }
            if (!ImGui.IsItemActive() && string.IsNullOrEmpty(gamepath) && !string.IsNullOrEmpty(gameExe)) {
                config.SetGamePath(game, Path.GetDirectoryName(gameExe)!);
            }
            ImguiHelpers.Tooltip(Lang.Settings.GamePath.Tooltip);

            if (AppImguiHelpers.InputFolder(Lang.Settings.ExtractPath.Text, ref extractPath)) {
                extractPath = PathUtils.RemoveNativesFolder(extractPath.NormalizeFilepath(), context.GetWorkspace()?.Platform ?? PlatformIdentifier.GetDefaultIdentifier(game)).ToString();
                config.SetGameExtractPath(game, extractPath);
            }
            ImguiHelpers.Tooltip(Lang.Settings.ExtractPath.Tooltip);

            if (AppImguiHelpers.InputFilepath(Lang.Settings.ExePath.Text, ref gameExe, FileFilters.Executable)) {
                config.SetGameExecutablePath(game, gameExe);
            }
            if (!ImGui.IsItemActive() && !string.IsNullOrEmpty(gamepath) && string.IsNullOrEmpty(gameExe)) {
                config.SetGameExecutablePath(game, AppUtils.FindGameExecutable(gamepath, game.name)!);
            }
            ImguiHelpers.Tooltip(Lang.Settings.ExePath.Tooltip);

            if (AppImguiHelpers.InputFilepath(Lang.Settings.FileList.Text, ref filelist, FileFilters.ListFile)) {
                config.SetGameFilelist(game, filelist);
            }
            ImguiHelpers.Tooltip(Lang.Settings.FileList.Tooltip);

            var rszLabel = isFullySupported ? Lang.Settings.RszPath_Custom : Lang.Settings.RszPath;
            if (AppImguiHelpers.InputFilepath(rszLabel.Text, ref rszPath, FileFilters.JsonFile)) {
                config.SetGameRszJsonPath(game, rszPath);
                WindowHandlerFactory.ResetGameTypes(game);
                Component.ResetGameTypes(game);
            }
            ImguiHelpers.Tooltip(rszLabel.Tooltip);
            if (isCustom) {
                ImGui.PushStyleColor(ImGuiCol.Text, Colors.Warning);
                ImGui.TextWrapped(Lang.Settings.Note_CustomGame);
                ImGui.PopStyleColor();
            } else if (isFullySupported) {
                ImGui.PushStyleColor(ImGuiCol.Text, Colors.Info);
                ImGui.TextWrapped(Lang.Settings.Note_FullySupported);
                ImGui.PopStyleColor();
            }
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Info);
            ImGui.TextWrapped(Lang.Settings.Note_ChangesNeedRestart);
            ImGui.PopStyleColor();

            ImGui.Spacing();
            ImGui.TreePop();
        }
    }
    private static void ShowSetting(AppConfig.ClassSettingWrapper<string> setting, TextTooltip text) => ShowSetting(setting, text.Text, text.Tooltip);
    private static void ShowSetting(AppConfig.ClassSettingWrapper<string> setting, ReadOnlySpan<byte> label, ReadOnlySpan<byte> tooltip)
    {
        var remoteSource = setting.Get() ?? "";
        if (ImGui.InputText(label, ref remoteSource, 280)) {
            setting.Set(remoteSource);
        }
        ImguiHelpers.Tooltip(tooltip);
    }
    private static bool ShowSetting(AppConfig.SettingWrapper<bool> setting, TextTooltip text) => ShowSetting(setting, text.Text, text.Tooltip);
    private static bool ShowSetting(AppConfig.SettingWrapper<bool> setting, ReadOnlySpan<byte> label, ReadOnlySpan<byte> tooltip)
    {
        var value = setting.Get();
        var changed = false;
        if (ImGui.Checkbox(label, ref value)) {
            setting.Set(value);
            changed = true;
        }
        ImguiHelpers.Tooltip(tooltip);
        return changed;
    }
    private static void ShowSlider(AppConfig.SettingWrapper<int> setting, TextTooltip text, int min, int max)
    {
        var value = setting.Get();
        if (ImGui.SliderInt(text.Text, ref value, min, max)) {
            setting.Set(value);
        }
        ImguiHelpers.Tooltip(text.Tooltip);
    }
    private static void ShowFolderSetting(AppConfig.ClassSettingWrapper<string> setting, TextTooltip text)
    {
        var configPath = setting.Get();
        if (AppImguiHelpers.InputFolder(text.Text, ref configPath)) {
            setting.Set(configPath);
        }
        ImguiHelpers.Tooltip(text.Tooltip);
    }
    private Dictionary<AppConfig.SettingWrapper<KeyBinding>, string> keyfilters = new();

    private bool ImguiKeybinding(ReadOnlySpan<byte> label, AppConfig.SettingWrapper<KeyBinding> setting)
    {
        var key = setting.Get();
        var filter = keyfilters.GetValueOrDefault(setting) ?? "";
        ImGui.PushID(label);
        var changed = false;
        ImGui.PushItemWidth(50);
        changed = ImGui.Checkbox(Lang.Settings.Key_Ctrl, ref key.ctrl);
        ImGui.SameLine();
        changed = ImGui.Checkbox(Lang.Settings.Key_Shift, ref key.shift) || changed;
        ImGui.SameLine();
        changed = ImGui.Checkbox(Lang.Settings.Key_Alt, ref key.alt) || changed;
        ImGui.SameLine();
        ImGui.PopItemWidth();
        ImGui.SetNextItemWidth(ImGui.CalcItemWidth() - 200);
        changed = ImguiHelpers.FilterableCSharpEnumCombo<ImGuiKey>(label, ref key.Key, ref filter) || changed;
        if (!setting.IsInitial) {
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_Reset}")) {
                setting.Reset();
                key = setting.Get();
                changed = true;
            }
        }
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
