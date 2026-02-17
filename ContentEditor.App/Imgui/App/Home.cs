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
    private static string[] gameNames = null!;
    private static string[] gameNameCodes = null!;
    private string chosenGame = "";
    private bool customGame;
    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
        gameNameCodes = AppConfig.Instance.GetGamelist().Select(gs => gs.name).ToArray();
        gameNames = gameNameCodes.Select(code => Languages.TranslateGame(code)).ToArray();
    }
    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        ShowLogo();
        ImGui.SameLine();
        ImGui.BeginChild("WelcomeText", new Vector2(0, 250 * UI.UIScale));
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
        // SILVER: Maybe even a notification when a new version is up?
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
                    // TODO SILVER: I of course totally know how to do this 100% ref: https://github.com/WolvenKit/WolvenKit?tab=readme-ov-file#screenshots
                    ImGui.Text("In a grid with the app logo being used as the image/icon.\nCould be color coded by game.");
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
        ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericMatchCase}", ref isRecentFileFilterMatchCase, Colors.IconActive);
        ImguiHelpers.Tooltip("Match Case");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
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
        // TODO SILVER: Add filters based on game name and maybe file type copy style from Pak Browser
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (recents == null || recents.Count == 0) {
            ImGui.TextDisabled("There are no recent files.");
        } else {
            ImGui.BeginChild("RecentFileList");
            foreach (var file in recents) {
                if (!string.IsNullOrEmpty(recentFileFilter) && !file.Contains(recentFileFilter, isRecentFileFilterMatchCase ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase)) {
                    continue;
                }
                if (ImGui.Selectable(file)) {
                    var sep = file.IndexOf('|');
                    var fileToOpen = sep == -1 ? file : file.Substring(sep + 1);
                    EditorWindow.CurrentWindow?.OpenFiles(new[] { fileToOpen });
                    break;
                }
            }
            ImGui.EndChild();
        }
    }
    public bool RequestClose()
    {
        return false;
    }
}
