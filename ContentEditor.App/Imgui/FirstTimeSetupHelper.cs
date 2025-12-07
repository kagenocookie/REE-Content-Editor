using System.Numerics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Themes;
using ReeLib;

namespace ContentEditor.App;

public class FirstTimeSetupHelper : IWindowHandler, IKeepEnabledWhileSaving
{
    public string HandlerName => " First Time Setup";

    int IWindowHandler.FixedID => -4432341;

    public bool HasUnsavedChanges => false;

    UIContext context = null!;

    private static string[] gameNames = null!;
    private static string[] gameNameCodes = null!;

    public void Init(UIContext context)
    {
        this.context = context;
        gameNameCodes = AppConfig.Instance.GetGamelist().Select(gs => gs.name).ToArray();
        gameNames = gameNameCodes.Select(code => Languages.TranslateGame(code)).ToArray();
    }

    private string chosenGame = "";
    private bool customGame;

    public void OnIMGUI()
    {
        var config = AppConfig.Instance;

        ImGui.Separator();
        ImGui.Text("Choose a theme and color");
        var theme = config.Theme.Get();
        if (ImguiHelpers.ValueCombo("##Theme", DefaultThemes.AvailableThemes, DefaultThemes.AvailableThemes, ref theme)) {
            UI.ApplyTheme(theme!);
            config.Theme.Set(theme);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("You can modify or create new custom themes through the menu option Tools > Theme Editor.");

        var color = config.BackgroundColor.Get().ToVector4();
        if (ImGui.ColorEdit4("##color", ref color)) {
            var newColor = ReeLib.via.Color.FromVector4(color);
            config.BackgroundColor.Set(newColor);
            foreach (var wnd in MainLoop.Instance.Windows) {
                wnd.ClearColor = newColor;
            }
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("You can modify or create new custom themes through the menu option Tools > Theme Editor.");

        ImGui.Spacing();
        ImGui.Text("Choose the game you wish to mod");
        if (customGame) {
            ImGui.InputText("Game short name", ref chosenGame, 20);
            chosenGame = chosenGame.Replace(" ", "");
        } else {
            ImguiHelpers.ValueCombo("##Game", gameNames, gameNameCodes, ref chosenGame);
        }
        ImGui.Checkbox("Custom game", ref customGame);
        ImguiHelpers.Tooltip("Select this if you wish to configure a game outside of the predefined list.\nCustom games may not fully work.");
        if (!string.IsNullOrEmpty(chosenGame)) {
            var gamepath = config.GetGamePath(chosenGame);
            var rszPath = config.GetGameRszJsonPath(chosenGame);
            var filelist = config.GetGameFilelist(chosenGame);
            var extractPath = config.GetGameExtractPath(chosenGame);
            var isCustomGame = !Enum.TryParse<GameName>(chosenGame, out _);

            if (AppImguiHelpers.InputFolder("Game Path", ref gamepath) && Directory.Exists(gamepath)) {
                AppConfig.Instance.SetGamePath(chosenGame, gamepath);
            }
            ImguiHelpers.Tooltip("This is the path to the game (where the .exe file is located).");
            if (isCustomGame) {
                if (AppImguiHelpers.InputFilepath("RSZ JSON File Path", ref rszPath, "*.json") && File.Exists(gamepath)) {
                    AppConfig.Instance.SetGameRszJsonPath(chosenGame, rszPath);
                }
                ImguiHelpers.Tooltip("This setting should point to the correct rsz*.json for the chosen game.");

                if (AppImguiHelpers.InputFilepath("File List Path", ref filelist) && File.Exists(gamepath)) {
                    AppConfig.Instance.SetGameFilelist(chosenGame, filelist);
                }
                ImguiHelpers.Tooltip("This setting should point to a filepath containing a list of all files used by the game.");
            }

            if (isCustomGame && AppImguiHelpers.InputFilepath("File Extraction Path", ref extractPath) && File.Exists(gamepath)) {
                AppConfig.Instance.SetGameExtractPath(chosenGame, extractPath);
            }
            ImguiHelpers.Tooltip("This is the default path used when extracting files. Can be left empty.");
        }

        ImGui.Separator();
        if (!string.IsNullOrEmpty(chosenGame) && !Enum.TryParse<GameName>(chosenGame, out _)) {
            ImGui.TextColored(Colors.Note, "This is a custom defined game. The app may need an upgrade to fully support all files, some files may not load correctly.");
        }
        ImGui.TextColored(Colors.Note, "All settings, including some additional settings, can be configured at any time later through the Tools > Settings menu option.");
        if (ImGui.Button("Close")) {
            if (string.IsNullOrEmpty(chosenGame)) {
                EditorWindow.CurrentWindow?.CloseSubwindow(this);
                return;
            }

            if (string.IsNullOrEmpty(config.GetGamePath(chosenGame))) {
                EditorWindow.CurrentWindow?.Overlays.ShowTooltip("The game path is required!", 2);
            } else {
                EditorWindow.CurrentWindow?.CloseSubwindow(this);
                EditorWindow.CurrentWindow?.SetWorkspace(chosenGame, null);
            }
        }
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    public bool RequestClose()
    {
        return false;
    }
}