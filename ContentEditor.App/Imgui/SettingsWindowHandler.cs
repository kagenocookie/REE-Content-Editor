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
            // var blender = config.BlenderPath.Get() ?? "";
            // if (AppImguiHelpers.InputFilepath("Blender path", ref blender, "blender.exe|blender.exe")) {
            //     config.BlenderPath.Set(blender);
            // }

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

            ShowSetting(config.EnableUpdateCheck, "Automatically check for updates", "Will occasionally check GitHub for new releases.");

            var showFps = config.ShowFps.Get();
            if (ImGui.Checkbox("Show FPS", ref showFps)) {
                config.ShowFps.Set(showFps);
            }

            ImGui.SeparatorText("Advanced");
            var configPath = config.GameConfigBaseFilepath.Get();
            if (AppImguiHelpers.InputFolder("Game config base path", ref configPath)) {
                if (configPath.EndsWith(".exe")) configPath = Path.GetDirectoryName(configPath)!;
                config.GameConfigBaseFilepath.Set(configPath);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("The folder path that contains the game specific entity configurations. Will use relative path config/ by default if unspecified.");
            ShowSetting(config.RemoteDataSource, "Resource data source", "The source from which to check for updates and download game-specific resource cache files.\nWill use the default GitHub repository if unspecified.");
            ShowFolderSetting(config.ThumbnailCacheFilepath, "Thumbnail cache file path", "The folder that cached thumbnails should be stored in. Must not be empty.");

            ShowSlider(config.UnpackMaxThreads, "Max unpack threads", 1, 64, "The maximum number of threads to be used when unpacking.\nThe actual thread count is determined automatically by the .NET runtime.");
            ShowSlider(config.AutoExpandFieldsCount, "Auto-expand field count", 0, 16, "RSZ object fields with less than the defined number of fields will initially auto expand.");

            var logLevel = config.LogLevel.Get();
            if (ImGui.Combo("Minimum logging level", ref logLevel, LogLevels, LogLevels.Length)) {
                config.LogLevel.Set(logLevel);
            }

            var maxUndo = config.MaxUndoSteps.Get();
            if (ImGui.DragInt("Max undo steps", ref maxUndo, 0.25f, 0)) {
                config.MaxUndoSteps.Set(maxUndo);
            }
            ImguiHelpers.Tooltip("The maximum number of steps you can undo. Higher number means a bit higher memory usage after longer sessions.");
            ShowSlider(config.MaxFps, "Max FPS", 10, 240, "The maximum FPS for rendering.");
            ShowSlider(config.BackgroundMaxFps, "Max FPS in background", 5, config.MaxFps.Get(), "The maximum FPS when the editor window is not focused.");
            ShowSetting(config.PrettyFieldLabels, "Simplify field labels", "Whether to simplify field labels instead of showing the raw field names (e.g. \"Target Object\" instead of \"_TargetObject\").");
            ShowSetting(config.LogToFile, "Output logs to file", $"If checked, any logging will also be output to file {FileLogger.DefaultLogFilePath}.\nChanging this setting requires a restart of the app.");
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
            ImGui.SeparatorText(Languages.TranslateGame(game.name));

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

            ImGui.Spacing();
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
            } else if (fullSupportedGames.Contains(game.name)) {
                ImGui.TextColored(Colors.Info, "*This is a fully supported game, game specific data can be fetched automatically.");
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

    private static void ShowSetting(AppConfig.ClassSettingWrapper<string> setting, string label, string? tooltip)
    {
        var remoteSource = setting.Get() ?? "";
        if (ImGui.InputText(label, ref remoteSource, 280)) {
            setting.Set(remoteSource);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(tooltip);
    }

    private static void ShowSetting(AppConfig.SettingWrapper<bool> setting, string label, string? tooltip)
    {
        var value = setting.Get();
        if (ImGui.Checkbox(label, ref value)) {
            setting.Set(value);
        }
        if (tooltip != null && ImGui.IsItemHovered()) {
            ImGui.SetItemTooltip(tooltip);
        }
    }

    private static void ShowSlider(AppConfig.SettingWrapper<int> setting, string label, int min, int max, string? tooltip)
    {
        var value = setting.Get();
        if (ImGui.SliderInt(label, ref value, min, max)) {
            setting.Set(value);
        }
        if (tooltip != null && ImGui.IsItemHovered()) {
            ImGui.SetItemTooltip(tooltip);
        }
    }

    private static void ShowFolderSetting(AppConfig.ClassSettingWrapper<string> setting, string label, string? tooltip)
    {
        var configPath = setting.Get();
        if (AppImguiHelpers.InputFolder(label, ref configPath)) {
            setting.Set(configPath);
        }
        if (tooltip != null && ImGui.IsItemHovered()) {
            ImGui.SetTooltip(tooltip);
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}