using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Themes;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App;

public class SettingsWindowHandler : IWindowHandler, IKeepEnabledWhileSaving
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => "Settings";

    private static string[]? tabs;
    private int currentTab;
    public int FixedID => -10002;

    private static readonly string[] LogLevels = ["Debug", "Info", "Error"];

    private WindowData data = null!;
    protected UIContext context = null!;

    private static bool? _wasOriginallyAlphaBg;
    private string? filterKey1, filterKey2;
    private string customGameNameInput = "", customGameFilepath = "";

    private static HashSet<string>? fullSupportedGames;

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    void IWindowHandler.OnIMGUI() => OnWindow();
    public void OnWindow()
    {
        if (tabs == null) {
            string[] list = ["General", "Keys"];
            tabs = list.Concat(AppConfig.Instance.GetGamelist().Select(gs => gs.name)).Append("Add custom game").ToArray();
        }

        fullSupportedGames ??= ResourceRepository.RemoteInfo.Resources
            .Where(kv => kv.Value.IsFullySupported)
            .Select(kv => kv.Key)
            .ToHashSet();

        if (!ImguiHelpers.BeginWindow(data, "Settings")) {
            EditorWindow.CurrentWindow?.CloseSubwindow(data);
            return;
        }
        ImguiHelpers.Tabs(tabs, ref currentTab);
        var selectedTab = tabs[currentTab];

        var config = AppConfig.Instance;
        if (currentTab == 0) {
            ImGui.SeparatorText("Main settings");
            var blender = config.BlenderPath.Get() ?? "";
            if (AppImguiHelpers.InputFilepath("Blender path", ref blender, "blender.exe|blender.exe")) {
                config.BlenderPath.Set(blender);
            }

            var configPath = config.GameConfigBaseFilepath.Get();
            if (AppImguiHelpers.InputFolder("Game config base path", ref configPath)) {
                if (configPath.EndsWith(".exe")) configPath = Path.GetDirectoryName(configPath)!;
                config.GameConfigBaseFilepath.Set(configPath);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The folder path that contains the game specific entity configurations. Will use relative path config/ by default if unspecified.");

            var theme = config.Theme.Get();
            if (ImguiHelpers.ValueCombo("Theme", DefaultThemes.AvailableThemes, DefaultThemes.AvailableThemes, ref theme)) {
                UI.ApplyTheme(theme!);
                config.Theme.Set(theme);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Custom themes can be configured through Tools > Theme Editor.");

            var bgColor = config.BackgroundColor.Get().ToVector4();
            var isAlpha = bgColor.W < 1;
            if (_wasOriginallyAlphaBg == null) _wasOriginallyAlphaBg = isAlpha;
            if (ImGui.ColorEdit4("Background color", ref bgColor)) {
                var newColor = ReeLib.via.Color.FromVector4(bgColor);
                config.BackgroundColor.Set(newColor);
                foreach (var wnd in MainLoop.Instance.Windows) {
                    wnd.ClearColor = newColor;
                }
            }
            if (isAlpha && _wasOriginallyAlphaBg == false) {
                ImGui.TextColored(Colors.Warning, "Window transparency change will only be applied after restarting the app");
            }

            var prettyLabels = config.PrettyFieldLabels.Get();
            if (ImGui.Checkbox("Simplify field labels", ref prettyLabels)) {
                config.PrettyFieldLabels.Set(prettyLabels);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Whether to simplify field labels instead of showing the raw field names (e.g. \"Target Object\" instead of \"_TargetObject\").");

            var doUpdateCheck = config.EnableUpdateCheck.Get();
            if (ImGui.Checkbox("Automatically check for updates", ref doUpdateCheck)) {
                config.EnableUpdateCheck.Set(doUpdateCheck);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Will occasionally check GitHub for new releases.");

            var maxUndo = config.MaxUndoSteps.Get();
            if (ImGui.DragInt("Max undo steps", ref maxUndo, 0.25f, 0)) {
                config.MaxUndoSteps.Set(maxUndo);
            }

            ImGui.SeparatorText("Advanced");
            var remoteSource = config.RemoteDataSource.Get() ?? "";
            if (ImGui.InputText("Resource data source", ref remoteSource, 280)) {
                config.RemoteDataSource.Set(remoteSource);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The source from which to check for updates and download game-specific resource cache files.\nWill use the default GitHub repository if unspecified.");

            var unpackThreads = config.UnpackMaxThreads.Get();
            if (ImGui.SliderInt("Max unpack threads", ref unpackThreads, 1, 64)) {
                config.UnpackMaxThreads.Set(unpackThreads);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The maximum number of threads to be used when unpacking.\nThe actual thread count is determined automatically by the .NET runtime.");

            var expandFields = config.AutoExpandFieldsCount.Get();
            if (ImGui.SliderInt("Auto-expand field count", ref expandFields, 0, 16)) {
                config.AutoExpandFieldsCount.Set(expandFields);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The number of fields below which an RSZ object tree node should auto-expand.");

            var logLevel = config.LogLevel.Get();
            if (ImGui.Combo("Logging level", ref logLevel, LogLevels, LogLevels.Length)) {
                config.LogLevel.Set(logLevel);
            }

            var maxFps = config.MaxFps.Get();
            if (ImGui.SliderInt("Max FPS", ref maxFps, 10, 240)) {
                config.MaxFps.Set(maxFps);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The maximum FPS for rendering.");

            var inactiveMaxFps = config.BackgroundMaxFps.Get();
            if (ImGui.SliderInt("Max FPS in background", ref inactiveMaxFps, 5, maxFps)) {
                config.BackgroundMaxFps.Set(inactiveMaxFps);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The maximum FPS when the editor window is not focused.");

            var showFps = config.ShowFps.Get();
            if (ImGui.Checkbox("Show FPS", ref showFps)) {
                config.ShowFps.Set(showFps);
            }
        } else if (currentTab == 1) {
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
        } else if (currentTab == tabs.Length - 1) {
            // add custom game
            ImGui.InputText("Short name", ref customGameNameInput, 20);
            AppImguiHelpers.InputFolder("Game path", ref customGameFilepath);
            if (!string.IsNullOrEmpty(customGameNameInput) && !string.IsNullOrEmpty(customGameFilepath) && Directory.Exists(customGameFilepath)) {
                if (ImGui.Button("Add")) {
                    config.SetGamePath(customGameNameInput, customGameFilepath);
                    tabs = null;
                }
            }
        } else {
            GameIdentifier game = selectedTab;
            var gamepath = config.GetGamePath(game);
            if (AppImguiHelpers.InputFolder("Game path", ref gamepath)) {
                if (gamepath.EndsWith(".exe")) gamepath = Path.GetDirectoryName(gamepath)!;
                config.SetGamePath(game, gamepath);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The full path to the game. Should point to the folder containing the .exe and .pak files");

            var extractPath = config.GetGameExtractPath(game);
            if (AppImguiHelpers.InputFolder("Game extract path", ref extractPath)) {
                extractPath = PathUtils.RemoveNativesFolder(extractPath);
                config.SetGameExtractPath(game, extractPath);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The default path to preselect when extracting files.");

            if (!fullSupportedGames.Contains(game.name)) {
                var rszPath = config.GetGameRszJsonPath(game);
                var filelist = config.GetGameFilelist(game);
                var isCustomGame = !Enum.TryParse<GameName>(game.name, out _);
                var tooltip = "Defining a custom path here may not be required if it's at least a partially supported game";
                if (AppImguiHelpers.InputFilepath("File list", ref filelist, "List file|*.list;*.txt|Any|*.*")) {
                    config.SetGameFilelist(game, filelist);
                }
                if (!isCustomGame && ImGui.IsItemHovered()) ImGui.SetItemTooltip(tooltip);
                if (AppImguiHelpers.InputFilepath("RSZ template JSON path", ref rszPath, "JSON file|*.json")) {
                    config.SetGameRszJsonPath(game, rszPath);
                }
                if (!isCustomGame && ImGui.IsItemHovered()) ImGui.SetItemTooltip(tooltip);
                if (isCustomGame) {
                    ImGui.TextColored(Colors.Info, "*This is a custom defined game. The app may need an upgrade to fully support all files, some files may not load correctly.");
                }
            } else {
                ImGui.TextColored(Colors.Info, "*This is a fully supported game, other game specific data is fetched automatically.");
            }

            ImGui.TextColored(Colors.Info, "*Changes to these settings may require a restart of the app before they get applied");
        }
        ImGui.End();
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