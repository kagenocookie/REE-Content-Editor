using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Themes;
using ReeLib;
using Silk.NET.Maths;
using System.Numerics;

namespace ContentEditor.App;

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
    private static string[] gameNames = null!;
    private static string[] gameNameCodes = null!;
    private string chosenGame = "";
    private bool customGame;
    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
        var games = AppConfig.Instance.GetGamelist().Select(gs => gs.name).Select(code => new { Code = code, Name = Languages.TranslateGame(code)}).OrderBy(x => x.Name).ToArray();
        gameNameCodes = games.Select(g => g.Code).ToArray();
        gameNames = games.Select(g => g.Name).ToArray();
    }
    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        ShowLogo();
        ImGui.SameLine();
        float availSpace2 = ImGui.GetContentRegionAvail().X - ((250 - ImGui.GetStyle().FramePadding.X * 2) - ImGui.GetStyle().ItemSpacing.X);
        ImGui.BeginChild("WelcomeText", new Vector2(availSpace2, 250 * UI.UIScale));
        ShowWelcomeText();
        ImGui.EndChild();

        ImGui.BeginChild("GameList", new Vector2(250 * UI.UIScale, 0), ImGuiChildFlags.Borders);
        ShowGameList();
        ImGui.EndChild();
        ImGui.SameLine();
        float availSpace = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X;
        ImGui.BeginChild("Tabs", new Vector2(availSpace / 3 * 2, 0), ImGuiChildFlags.Borders);
        ShowTabs();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("RecentFiles", new Vector2(availSpace / 3 * 1, 0), ImGuiChildFlags.Borders);
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
    private static void ShowGameList()
    {
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

        fullSupportedGames ??= ResourceRepository.RemoteInfo.Resources.Where(kv => kv.Value.IsFullySupported).Select(kv => kv.Key).ToHashSet();
        var games = AppConfig.Instance.GetGamelist();
        var currentActiveGame = EditorWindow.CurrentWindow?.Workspace.Env.Config.Game.name;
        foreach (var fullySupported in new[] { true, false }) {
            foreach (var (game, configured) in games) {
                if (!configured || fullSupportedGames.Contains(game) != fullySupported)
                    continue;

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

        float remainingSpace = ImGui.GetContentRegionAvail().Y;
        float footerHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y + ImGui.GetTextLineHeightWithSpacing() * 3;
        if (remainingSpace > footerHeight) ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (remainingSpace - footerHeight));
        if (ImGui.Button($"{AppIcons.SI_GenericHeart} Support Development", new Vector2(ImGui.GetContentRegionAvail().X, 0))) {
            FileSystemUtils.OpenURL("https://ko-fi.com/shadowcookie");
        }
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
        // SILVER: Maybe even a notification when a new version is up or should that be its own tab?
        ImGui.Text("Version: " + AppConfig.Version);
        ImGui.PopStyleColor();
    }
    private static void ShowWelcomeText()
    {
        ImGui.PushFont(null, UI.FontSizeLarge + 100);
        string text = "Welcome to Content Editor";
        var textSize = ImGui.CalcTextSize(text);
        var availSpace = ImGui.GetContentRegionAvail();
        var posX = (availSpace.X - textSize.X) * 0.5f;
        var posY = (availSpace.Y - textSize.Y) * 0.5f;

        if (posX > 0) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + posX);
        if (posY > 0) ImGui.SetCursorPosY(ImGui.GetCursorPosY() + posY);

        ImGui.Text(text);
        ImGui.PopFont();
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
                    var recents = AppConfig.Settings.RecentBundles;
                    foreach (var file in recents) {
                        ImGui.Selectable(file);
                    }
                     // TODO SILVER: I of course totally know how to do this 100% ref: https://github.com/WolvenKit/WolvenKit?tab=readme-ov-file#screenshots
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Updates")) {
                    ImGui.EndTabItem();
                }
            }
            ImGui.EndTabBar();
        }
    }
    private void ShowRecentFilesList()
    {
        var recents = AppConfig.Settings.RecentFiles;
        string filterLabelDisplayText = _activeRecentFileGameFilters.Count == 0 ? $"{AppIcons.SI_Filter} : " + "All Games" : $"{AppIcons.SI_Filter} : " + $"{_activeRecentFileGameFilters.Count} Selected";
        Vector2 filterLabelSize = ImGui.CalcTextSize(filterLabelDisplayText);
        float filterComboWidth = filterLabelSize.X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X + ImGui.GetFontSize();
        ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericMatchCase}", ref isRecentFileFilterMatchCase, Colors.IconActive);
        ImguiHelpers.Tooltip("Match Case");
        ImGui.SameLine();
        // SILVER: UI designers go to a special kind of hell, this one to be specific...
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
    public bool RequestClose()
    {
        return false;
    }
}
