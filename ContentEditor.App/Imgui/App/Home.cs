using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Themes;
using ReeLib;
using Silk.NET.Maths;
using System.Numerics;

namespace ContentEditor.App;
public enum BundleDisplayMode
{
    Grid,
    List
}
public class HomeWindow : IWindowHandler
{
    public string HandlerName => "Home";
    public bool HasUnsavedChanges => false;
    public int FixedID => -123164;
    private WindowData data = null!;
    protected UIContext context = null!;

    private static HashSet<string>? fullSupportedGames;
    private string recentFileFilter = string.Empty;
    private bool isRecentFileFilterMatchCase = false;
    private readonly HashSet<string> _activeRecentFileGameFilters = new();
    private string bundleFilter = string.Empty;
    private bool isBundleFilterMatchCase = false;
    private readonly HashSet<string> _activeBundleGameFilters = new();

    private static string[] gameNames = null!;
    private static string[] gameNameCodes = null!;
    private string chosenGame = "";
    private bool customGame;
    public BundleDisplayMode DisplayMode { get; set; } = AppConfig.Instance.BundleDisplayMode;

    private static Dictionary<string, Func<Vector4[]>> GameColors = new() // TODO SILVER: Add the rest of the games
    {
        { "re2", () => new[] { Colors.Game_RE2Primary, Colors.Game_RE2Secondary, Colors.Game_RE2Secondary }},
        { "re2rt", () => new[] { Colors.Game_RE2RTPrimary, Colors.Game_RE2RTSecondary, Colors.Game_RE2RTSecondary }},
        { "re3", () => new[] { Colors.Game_RE3Primary, Colors.Game_RE3Secondary, Colors.Game_RE3Secondary }},
        { "re3rt", () => new[] { Colors.Game_RE3RTPrimary, Colors.Game_RE3RTSecondary, Colors.Game_RE3RTSecondary }},
        { "re4", () => new[] { Colors.Game_RE4Primary, Colors.Game_RE4Secondary, Colors.Game_RE4Secondary }},
        { "re7", () => new[] { Colors.Game_RE7Primary, Colors.Game_RE7Secondary, Colors.Game_RE7Secondary }},
        { "re7rt", () => new[] { Colors.Game_RE7RTPrimary, Colors.Game_RE7RTSecondary, Colors.Game_RE7RTSecondary }},
        { "re8", () => new[] { Colors.Game_RE8Primary, Colors.Game_RE8Secondary, Colors.Game_RE8Secondary }},
        { "re9", () => new[] { Colors.Game_RE9Primary, Colors.Game_RE9Secondary, Colors.Game_RE9Secondary }},
        { "mhsto3", () => new[] { Colors.Game_MHSTO3Primary, Colors.Game_MHSTO3Secondary, Colors.Game_MHSTO3Secondary }},
        { "mhrise", () => new[] { Colors.Game_MHRISEPrimary, Colors.Game_MHRISESecondary, Colors.Game_MHRISESecondary }},
        { "mhwilds", () => new[] { Colors.Game_MHWILDSPrimary, Colors.Game_MHWILDSSecondary, Colors.Game_MHWILDSSecondary }},
    };

    public void Init(UIContext context)
    {
        this.context = context;
        var games = AppConfig.Instance.GetGamelist().Select(gs => gs.name).Select(code => new { Code = code, Name = Languages.TranslateGame(code)}).OrderBy(x => x.Name).ToArray();
        gameNameCodes = games.Select(g => g.Code).ToArray();
        gameNames = games.Select(g => g.Name).ToArray();
    }
    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        ImGui.BeginChild("Logo", new Vector2(250, 250 * UI.UIScale));
        ShowLogo();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("WelcomeText", new Vector2(0, 250 * UI.UIScale));
        ShowWelcomeText();
        ImGui.EndChild();

        ImGui.BeginChild("GameList", new Vector2(250 * UI.UIScale, 0), ImGuiChildFlags.Borders);
        ShowGameList();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("Tabs", new Vector2((ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 3 * 2, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX);
        ShowTabs();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("RecentFiles", new Vector2(ImGui.GetContentRegionAvail().X, 0), ImGuiChildFlags.Borders);
        ShowRecentFilesList();
        ImGui.EndChild();
    }
    private static void ShowLogo()
    {
        ImGui.PushFont(null, UI.FontSizeLarge + 150);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
        ImguiHelpers.ButtonMultiColor(AppIcons.REECE_LogoFull, new[] { Colors.IconSecondary, Colors.IconSecondary, Colors.IconSecondary, Colors.IconPrimary, Colors.IconSecondary });
        ImGui.PopFont();
        ImGui.PopStyleColor(3);
    }
    private static void ShowWelcomeText()
    {
        ImGui.PushFont(null, UI.FontSizeLarge + 100);
        string text = "Welcome to Content Editor";
        var textSize = ImGui.CalcTextSize(text);
        var availSpace = ImGui.GetContentRegionAvail();
        var posX = (availSpace.X - textSize.X - 250 - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        var posY = (availSpace.Y - textSize.Y) * 0.5f;

        if (posX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + posX);
        if (posY > 0) ImGui.SetCursorPosY(ImGui.GetCursorPosY() + posY);

        ImGui.Text(text);
        ImGui.PopFont();
    }
    private static void ShowGameList()
    {
        using (var _ = ImguiHelpers.Disabled(AppConfig.Instance.IsFirstTime)) {
            if (ImGui.Button($"{AppIcons.SI_FileType_PAK}") && EditorWindow.CurrentWindow?.Workspace != null) {
                EditorWindow.CurrentWindow?.AddSubwindow(new PakBrowser(EditorWindow.CurrentWindow.Workspace, null));
            }
            ImguiHelpers.Tooltip("Browse Game Files");
            ImGui.SameLine();
            if (ImGui.Button("Open File")) {
                PlatformUtils.ShowFileDialog((files) => {
                    MainLoop.Instance.MainWindow.InvokeFromUIThread(() => {
                        Logger.Info(string.Join("\n", files));
                        EditorWindow.CurrentWindow?.OpenFiles(files);
                    });
                });
            }
            ImGui.SameLine();
            ImguiHelpers.AlignElementRight((ImGui.CalcTextSize($"{AppIcons.SI_Settings}").X + ImGui.GetStyle().FramePadding.X * 2) * 2 + ImGui.GetStyle().ItemSpacing.X);
            if (ImGui.Button($"{AppIcons.Pencil}")) {
                EditorWindow.CurrentWindow?.AddUniqueSubwindow(new ThemeEditor());
            }
            ImguiHelpers.Tooltip("Theme Editor");
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_Settings}")) {
                EditorWindow.CurrentWindow?.AddUniqueSubwindow(new SettingsWindowHandler());
            }
            ImguiHelpers.Tooltip("Settings");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            if (!AppConfig.Instance.IsFirstTime) {
                fullSupportedGames ??= ResourceRepository.RemoteInfo.Resources.Where(kv => kv.Value.IsFullySupported).Select(kv => kv.Key).ToHashSet();
                var games = AppConfig.Instance.GetGamelist();
                var currentActiveGame = EditorWindow.CurrentWindow?.Workspace?.Env.Config.Game.name;
                foreach (var fullySupported in new[] { true, false }) {
                    foreach (var (game, configured) in games) {
                        if (!configured || fullSupportedGames.Contains(game) != fullySupported) {
                            continue;
                        }
                        var color = currentActiveGame == game ? Colors.TextActive : ImguiHelpers.GetColor(ImGuiCol.Text);
                        ImGui.PushStyleColor(ImGuiCol.Text, color);
                        if (ImGui.Selectable(Languages.TranslateGame(game))) {
                            EditorWindow.CurrentWindow?.SetWorkspace(game, null);
                        }
                        ImGui.PopStyleColor();
                    }
                    if (fullySupported) {
                        ImGui.Spacing();
                        ImGui.Separator();
                    }
                }
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            } else {
                ImGui.TextWrapped("Complete the First Time Setup to select a game.");
            }

            float remainingSpace = ImGui.GetContentRegionAvail().Y;
            float footerHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y + ImGui.GetTextLineHeightWithSpacing() * 3;
            if (remainingSpace > footerHeight) ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (remainingSpace - footerHeight));
            if (ImGui.Button($"{AppIcons.SI_GenericHeart} Support Development", new Vector2(ImGui.GetContentRegionAvail().X, 0))) {
                FileSystemUtils.OpenURL("https://ko-fi.com/shadowcookie");
            }
            ImGui.Spacing();
            var availSpace = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2;
            if (ImGui.Button($"{AppIcons.SI_Github}", new Vector2(availSpace / 3, 0))) {
                FileSystemUtils.OpenURL("https://github.com/kagenocookie/REE-Content-Editor");
            }
            ImguiHelpers.Tooltip("GitHub");
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_GenericWiki}", new Vector2(availSpace / 3, 0))) {
                FileSystemUtils.OpenURL("https://github.com/kagenocookie/REE-Content-Editor/wiki");
            }
            ImguiHelpers.Tooltip("Wiki");
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_Discord}", new Vector2(availSpace / 3, 0))) {
                FileSystemUtils.OpenURL("https://discord.gg/9Vr2SJ3");
            }
            ImguiHelpers.Tooltip("Discord");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.TextActive);
            ImGui.Text("Version: " + AppConfig.Version);
            ImGui.PopStyleColor();
        }
    }

    private void ShowTabs()
    {
        if (ImGui.BeginTabBar("HomeTabs")) {
            if (AppConfig.Instance.IsFirstTime && ImGui.BeginTabItem("First Time Setup")) {
                ImGui.SeparatorText("Choose a theme and color");
                var theme = AppConfig.Instance.Theme.Get();
                if (ImguiHelpers.ValueCombo("Theme", DefaultThemes.AvailableThemes, DefaultThemes.AvailableThemes, ref theme)) {
                    UI.ApplyTheme(theme!);
                    AppConfig.Instance.Theme.Set(theme);
                }
                ImguiHelpers.Tooltip("You can modify or create new custom themes through Edit > Theme Editor.");

                var color = AppConfig.Instance.BackgroundColor.Get().ToVector4();
                if (ImGui.ColorEdit4("Scene Background Color", ref color)) {
                    var newColor = ReeLib.via.Color.FromVector4(color);
                    AppConfig.Instance.BackgroundColor.Set(newColor);
                    foreach (var wnd in MainLoop.Instance.Windows) {
                        wnd.ClearColor = newColor;
                    }
                }
                ImguiHelpers.Tooltip("You can change this color at any time in Settings > Display > Theme.");

                ImGui.SeparatorText("Choose the game you wish to mod");

                ImGui.Checkbox("Custom Game", ref customGame);
                ImguiHelpers.Tooltip("Select this if you wish to configure a game outside of the predefined list.\nCustom games may not fully work.");
                if (customGame) {

                    if (!string.IsNullOrEmpty(chosenGame) && !Enum.TryParse<GameName>(chosenGame, out _)) {
                        ImGui.SameLine();
                        ImGui.Button($"{AppIcons.SI_GenericInfo}");
                        ImguiHelpers.TooltipColored("This is a custom defined game. The app may need an upgrade to fully support all files, some files may not load correctly.", Colors.Note);
                    }
                    ImGui.InputText("Game Short Name", ref chosenGame, 20);
                    ImGui.SameLine();
                    ImGui.TextColored(Colors.TextActive, "*");
                    chosenGame = chosenGame.Replace(" ", "");
                } else {
                    ImguiHelpers.ValueCombo("##Game", gameNames, gameNameCodes, ref chosenGame);
                    ImGui.SameLine();
                    ImGui.TextColored(Colors.TextActive, "*");
                }
                if (!string.IsNullOrEmpty(chosenGame)) {
                    var gamepath = AppConfig.Instance.GetGamePath(chosenGame);
                    var rszPath = AppConfig.Instance.GetGameRszJsonPath(chosenGame);
                    var filelist = AppConfig.Instance.GetGameFilelist(chosenGame);
                    var extractPath = AppConfig.Instance.GetGameExtractPath(chosenGame);
                    var isCustomGame = !Enum.TryParse<GameName>(chosenGame, out _);

                    if (AppImguiHelpers.InputFolder("Game Path", ref gamepath) && Directory.Exists(gamepath)) {
                        AppConfig.Instance.SetGamePath(chosenGame, gamepath);
                    }
                    ImguiHelpers.Tooltip("This is the path to the game (where the .exe file is located).");
                    ImGui.SameLine();
                    ImGui.TextColored(Colors.TextActive, "*");

                    if (isCustomGame) {
                        if (AppImguiHelpers.InputFilepath("RSZ JSON File Path", ref rszPath, FileFilters.JsonFile) && File.Exists(gamepath)) {
                            AppConfig.Instance.SetGameRszJsonPath(chosenGame, rszPath);
                        }
                        ImguiHelpers.Tooltip("This setting should point to the correct rsz*.json for the chosen game.");

                        if (AppImguiHelpers.InputFilepath("File List Path", ref filelist) && File.Exists(gamepath)) {
                            AppConfig.Instance.SetGameFilelist(chosenGame, filelist);
                        }
                        ImguiHelpers.Tooltip("This setting should point to a filepath containing a list of all files used by the game.");

                        if (AppImguiHelpers.InputFilepath("File Extraction Path", ref extractPath) && File.Exists(gamepath)) {
                            AppConfig.Instance.SetGameExtractPath(chosenGame, extractPath);
                        }
                        ImguiHelpers.Tooltip("This is the default path used when extracting files. Can be left empty.");
                    }
                }
                ImGui.SameLine();
                string finishText = "Finish Setup";
                ImguiHelpers.AlignElementRight(ImGui.CalcTextSize(finishText).X + ImGui.GetStyle().ItemSpacing.X + ImGui.GetStyle().FramePadding.X);
                using (var _ = ImguiHelpers.Disabled((string.IsNullOrEmpty(chosenGame) || string.IsNullOrEmpty(AppConfig.Instance.GetGamePath(chosenGame))))) {
                    if (ImGui.Button(finishText)) {
                        AppConfig.Instance.IsFirstTime.Set(false);
                        EditorWindow.CurrentWindow?.SetWorkspace(chosenGame, null);
                    }
                }
                ImGui.SeparatorText("##LoremIpsum");
                ImGui.EndTabItem();
            }
            if (!AppConfig.Instance.IsFirstTime) {
                if (ImGui.BeginTabItem("Bundles")) {
                    ImGui.Spacing();
                    if (ImGui.Button($"{AppIcons.SI_Bundle} Bundle Manager")) {
                        EditorWindow.CurrentWindow?.ShowBundleManagement();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"{AppIcons.SI_BundleLoadOrder} Load Order") && EditorWindow.CurrentWindow?.Workspace != null) {
                        EditorWindow.CurrentWindow?.AddUniqueSubwindow(new LoadOrderUI(EditorWindow.CurrentWindow.Workspace.BundleManager));
                    }
                    ImGui.SameLine();
                    ImguiHelpers.VerticalSeparator();
                    ImGui.SameLine();
                    if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FolderContain, new[] { Colors.IconPrimary, Colors.IconSecondary })) {
                        FileSystemUtils.ShowFileInExplorer(EditorWindow.CurrentWindow?.Workspace.BundleManager.AppBundlePath);
                    }
                    ImguiHelpers.Tooltip("Open Bundles folder in File Explorer");
                    ImGui.SameLine();
                    if (ImGui.Button($"{AppIcons.SI_GenericClear}")) {
                        AppConfig.Settings.RecentBundles.Clear();
                        AppConfig.Instance.SaveJsonConfig();
                    }
                    ImguiHelpers.Tooltip("Clear recent bundles list");
                    ImGui.SameLine();
                    ImguiHelpers.VerticalSeparator();
                    ImGui.SameLine();
                    ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericMatchCase}", ref isBundleFilterMatchCase, Colors.IconActive);
                    ImguiHelpers.Tooltip("Match Case");
                    ImGui.SameLine();
                    string filterLabelDisplayText = _activeBundleGameFilters.Count == 0 ? $"{AppIcons.SI_Filter} " + "All Games" : $"{AppIcons.SI_Filter} " + $"{_activeBundleGameFilters.Count} Selected";
                    float filterComboWidth = ImGui.CalcTextSize(filterLabelDisplayText).X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X + ImGui.GetFontSize();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (((filterComboWidth + ImGui.GetStyle().ItemSpacing.X) + (ImGui.GetStyle().FramePadding.X + ImGui.GetStyle().ItemSpacing.X) * 3) + (ImGui.GetStyle().ItemSpacing.X) * 6));
                    ImGui.SetNextItemAllowOverlap();
                    ImGui.InputTextWithHint("##BundleFilter", $"{AppIcons.SI_GenericMagnifyingGlass} Search Bundles", ref bundleFilter, 128);
                    if (!string.IsNullOrEmpty(bundleFilter)) {
                        ImGui.SameLine();
                        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMax().X - ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.X, ImGui.GetItemRectMin().Y));
                        ImGui.SetNextItemAllowOverlap();
                        if (ImGui.Button($"{AppIcons.SI_GenericClose}")) {
                            bundleFilter = string.Empty;
                        }
                    }
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(filterComboWidth);
                    if (ImGui.BeginCombo("##BundleGameFilterCombo", filterLabelDisplayText, ImGuiComboFlags.HeightLargest)) {
                        for (int i = 0; i < gameNameCodes.Length; i++) {
                            var code = gameNameCodes[i];
                            var displayName = gameNames[i];
                            bool isSelected = _activeBundleGameFilters.Contains(code);

                            if (ImGui.Checkbox(displayName, ref isSelected)) {
                                if (isSelected) {
                                    _activeBundleGameFilters.Add(code);
                                } else {
                                    _activeBundleGameFilters.Remove(code);
                                }
                            }
                        }
                        ImGui.EndCombo();
                    }
                    ImGui.SameLine();
                    using (var _ = ImguiHelpers.Disabled(_activeBundleGameFilters.Count == 0)) {
                        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FilterClear, new[] { Colors.IconTertiary, Colors.IconPrimary })) {
                            _activeBundleGameFilters.Clear();
                        }
                        ImguiHelpers.Tooltip("Clear Game Filters");
                    }
                    ImGui.SameLine();
                    ImguiHelpers.VerticalSeparator();
                    ImGui.SameLine();
                    if (ImGui.Button(DisplayMode == BundleDisplayMode.Grid ? $"{AppIcons.SI_ViewGridSmall}" : $"{AppIcons.List}")) {
                        AppConfig.Instance.BundleDisplayMode = DisplayMode = DisplayMode == BundleDisplayMode.Grid ? BundleDisplayMode.List : BundleDisplayMode.Grid;
                    }
                    ImguiHelpers.Tooltip(DisplayMode == BundleDisplayMode.Grid ? "Grid View"u8 : "List View"u8);
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();
                    ImGui.BeginChild("BundleList");
                    string? gameToSet = null;
                    string? bundleToOpen = null;
                    var availSpace = ImGui.GetContentRegionAvail();
                    var buttonSize = new Vector2(((availSpace.X - (ImGui.GetStyle().ItemSpacing.X * 2 + ImGui.GetStyle().FramePadding.X)) / 3) * UI.UIScale, 175 * UI.UIScale);
                    var curX = 0f;
                    foreach (var bundle in AppConfig.Settings.RecentBundles.ToList()) {
                        var sep = bundle.IndexOf('|');
                        var gamePrefix = sep == -1 ? null : bundle.Substring(0, sep);
                        var bundleName = sep == -1 ? bundle : bundle.Substring(sep + 1);
                        string gameDisplay = gamePrefix != null ? Languages.TranslateGame(gamePrefix) : "";
                        if (_activeBundleGameFilters.Count > 0 && (gamePrefix == null || !_activeBundleGameFilters.Contains(gamePrefix))) {
                            continue;
                        }
                        if (!string.IsNullOrEmpty(bundleFilter) && !bundle.Contains(bundleFilter, isBundleFilterMatchCase ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase)) {
                            continue;
                        }
                        if (DisplayMode == BundleDisplayMode.Grid) {
                            if (curX > 0) {
                                if (curX > availSpace.X - buttonSize.X) {
                                    ImGui.Spacing();
                                    curX = 0;
                                } else {
                                    ImGui.SameLine();
                                }
                            }
                            curX += buttonSize.X + ImGui.GetStyle().ItemSpacing.X;
                            bool clicked = ImGui.InvisibleButton(bundle, buttonSize);
                            bool isHovered = ImGui.IsItemHovered();
                            var min = ImGui.GetItemRectMin();
                            var max = ImGui.GetItemRectMax();
                            var size = max - min;
                            var drawList = ImGui.GetWindowDrawList();

                            drawList.AddRect(min, max, ImGui.GetColorU32(Colors.TextActive), 0f, ImDrawFlags.None, 2f);

                            ImGui.PushFont(null, UI.FontSizeLarge + 75);
                            var iconChars = AppIcons.SIC_BundleContain;
                            var iconSize = ImGui.CalcTextSize(iconChars[0].ToString());
                            var iconPos = new Vector2(min.X + ImGui.GetStyle().ItemSpacing.X, min.Y + ((size.Y - iconSize.Y) + ImGui.GetStyle().ItemSpacing.Y * 2) * 0.5f);
                            var colors = GetGameColors(gamePrefix, isHovered);
                            for (int i = 0; i < iconChars.Length; i++) {
                                var c = colors[i];
                                drawList.AddText(iconPos, ImGui.ColorConvertFloat4ToU32(c), iconChars[i].ToString());
                            }
                            ImGui.PopFont();

                            var textSize = ImGui.CalcTextSize(bundleName);
                            float textOffset = iconSize.X + ImGui.GetStyle().ItemSpacing.X * 2;
                            float availableTextWidth = max.X - (min.X + textOffset) - ImGui.GetStyle().ItemSpacing.X * 2;
                            string bundleDisplayName = bundleName;
                            if (availableTextWidth <= 0) {
                                bundleDisplayName = string.Empty;
                            } else if (ImGui.CalcTextSize(bundleDisplayName).X > availableTextWidth) {
                                const string ellipsis = "...";
                                float ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;

                                if (ellipsisWidth > availableTextWidth) {
                                    bundleDisplayName = string.Empty;
                                } else {
                                    for (int i = bundleDisplayName.Length - 1; i >= 0; i--) {
                                        string adjusted = string.Concat(bundleDisplayName.AsSpan(0, i), ellipsis);
                                        if (ImGui.CalcTextSize(adjusted).X <= availableTextWidth) {
                                            bundleDisplayName = adjusted;
                                            break;
                                        }
                                    }
                                }
                            }
                            var textPos = new Vector2(min.X + textOffset, iconPos.Y);
                            ImGui.PushStyleColor(ImGuiCol.Text, isHovered ? Colors.TextActive : ImguiHelpers.GetColor(ImGuiCol.Text));
                            drawList.AddText(textPos, ImGui.GetColorU32(ImGuiCol.Text), bundleDisplayName);
                            ImGui.PopStyleColor();
                            if (availableTextWidth <= 0) {
                                gameDisplay = string.Empty;
                            } else if (ImGui.CalcTextSize(gameDisplay).X > availableTextWidth && gamePrefix != null) {
                                float gamePrefixWidth = ImGui.CalcTextSize(gamePrefix.ToUpper()).X;

                                if (gamePrefixWidth > availableTextWidth) {
                                    gameDisplay = string.Empty;
                                } else {
                                    for (int i = gameDisplay.Length - 1; i > 0; i--) {
                                        string adjustedGameDisplayName = gamePrefix.ToUpper();

                                        if (ImGui.CalcTextSize(adjustedGameDisplayName).X <= availableTextWidth) {
                                            gameDisplay = adjustedGameDisplayName;
                                            break;
                                        }
                                    }
                                }
                            }
                            var gameTextPos = new Vector2(min.X + textOffset, iconPos.Y + ImGui.GetFrameHeight());
                            drawList.AddText(gameTextPos, ImGui.GetColorU32(ImGuiCol.TextDisabled), gameDisplay);

                            if (clicked && gamePrefix != null) {
                                gameToSet = gamePrefix;
                                bundleToOpen = bundleName;
                            }
                        } else {
                            if (ImguiHelpers.ContextMenuItem($"##{gamePrefix}{bundleName}", AppIcons.SIC_BundleContain, bundleName, GetGameColors(gamePrefix, false))) {
                                if (gamePrefix != null) {
                                    gameToSet = gamePrefix;
                                    bundleToOpen = bundleName;
                                }
                            }
                            ImGui.SameLine(ImGui.GetContentRegionAvail().X - (ImGui.CalcTextSize($"{gameDisplay}").X + ImGui.GetStyle().FramePadding.X));
                            ImGui.TextColored(ImguiHelpers.GetColor(ImGuiCol.TextDisabled), gameDisplay);
                        }
                    }
                    if (gameToSet != null && bundleToOpen != null) {
                        EditorWindow.CurrentWindow?.SetWorkspace(gameToSet, bundleToOpen);
                    }
                    ImGui.EndChild();
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Updates")) {
                    ImGui.Text("Some good update info here, fixed every bug or something.\nButtons to update the different resources?");
                    ImGui.Spacing();
                    ImGui.Text("The overlay help texts are currently drawn on top of the Home Page, but I don't think we should delete them.\nMaybe just move them to a Tips child window, kinda like 010's startup page?");
                    ImGui.EndTabItem();
                }
            }
            ImGui.EndTabBar();
        }
    }
    private void ShowRecentFilesList()
    {
        var recents = AppConfig.Settings.RecentFiles;
        if (ImGui.Button($"{AppIcons.SI_GenericClear}")) {
            AppConfig.Settings.RecentFiles.Clear();
            AppConfig.Instance.SaveJsonConfig();
        }
        ImguiHelpers.Tooltip("Clear recent files");
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();
        ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericMatchCase}", ref isRecentFileFilterMatchCase, Colors.IconActive);
        ImguiHelpers.Tooltip("Match Case");
        ImGui.SameLine();
        string filterLabelDisplayText = _activeRecentFileGameFilters.Count == 0 ? $"{AppIcons.SI_Filter} " + "All Games" : $"{AppIcons.SI_Filter} " + $"{_activeRecentFileGameFilters.Count} Selected";
        float filterComboWidth = ImGui.CalcTextSize(filterLabelDisplayText).X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X + ImGui.GetFontSize();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (((filterComboWidth + ImGui.GetStyle().ItemSpacing.X) + (ImGui.GetStyle().FramePadding.X + ImGui.GetStyle().ItemSpacing.X) * 3)));
        ImGui.SetNextItemAllowOverlap();
        ImGui.InputTextWithHint("##RecentFileFilter", $"{AppIcons.SI_GenericMagnifyingGlass} Search Recent Files", ref recentFileFilter, 128);
        if (!string.IsNullOrEmpty(recentFileFilter)) {
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMax().X - ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.X, ImGui.GetItemRectMin().Y));
            ImGui.SetNextItemAllowOverlap();
            if (ImGui.Button($"{AppIcons.SI_GenericClose}")) {
                recentFileFilter = string.Empty;
            }
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(filterComboWidth);
        if (ImGui.BeginCombo("##RecentGameFilterCombo", filterLabelDisplayText, ImGuiComboFlags.HeightLargest)) {
            for (int i = 0; i < gameNameCodes.Length; i++) {
                var code = gameNameCodes[i];
                var displayName = gameNames[i];
                bool isSelected = _activeRecentFileGameFilters.Contains(code);

                if (ImGui.Checkbox(displayName, ref isSelected)) {
                    if (isSelected) {
                        _activeRecentFileGameFilters.Add(code);
                    } else {
                        _activeRecentFileGameFilters.Remove(code);
                    }
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(_activeRecentFileGameFilters.Count == 0)) {
            if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FilterClear, new[] { Colors.IconTertiary, Colors.IconPrimary })) {
                _activeRecentFileGameFilters.Clear();
            }
            ImguiHelpers.Tooltip("Clear Game Filters");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        bool foundMatchingFile = false;
        ImGui.BeginChild("RecentFileList");
        foreach (var file in recents) {
            var sep = file.IndexOf('|');
            var game = sep == -1 ? null : file.Substring(0, sep);
            var fileToOpen = sep == -1 ? file : file.Substring(sep + 1);

            if (_activeRecentFileGameFilters.Count > 0 && (game == null || !_activeRecentFileGameFilters.Contains(game))) {
                continue;
            }
            if (!string.IsNullOrEmpty(recentFileFilter) && !file.Contains(recentFileFilter, isRecentFileFilterMatchCase ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase)) {
                continue;
            }
            foundMatchingFile = true;
            if (ImGui.Selectable(file)) {
                EditorWindow.CurrentWindow?.OpenFiles(new[] { fileToOpen });
                break;
            }
        }
        if (!foundMatchingFile) {
            ImGui.TextDisabled(!string.IsNullOrEmpty(recentFileFilter) || _activeRecentFileGameFilters.Count > 0 ? "No files match the current filters." : "There are no recent files.");
        }
        ImGui.EndChild();
    }
    private static Vector4[] GetGameColors(string? prefix, bool hovered)
    {
        if (hovered) {
            return new[] { Colors.IconActive, Colors.IconActive, Colors.IconActive };
        }
        if (string.IsNullOrEmpty(prefix)) {
            return new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary };
        }
        if (GameColors.TryGetValue($"{prefix}", out var colors)) {
            return colors();
        }
        return new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary };
    }

    public bool RequestClose()
    {
        return false;
    }
}
