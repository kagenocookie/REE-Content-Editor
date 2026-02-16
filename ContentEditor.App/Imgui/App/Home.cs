using ContentEditor.App.Windowing;
using ContentEditor.Core;
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
    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
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
        // TODO SILVER: Add overlayed pak browser button for active game, push it to the right
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
            if (fullySupported) ImGui.Separator();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        float footerHeight = 0;
        float remainingSpace = ImGui.GetContentRegionAvail().Y;
        footerHeight += ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y + ImGui.GetTextLineHeightWithSpacing() * 3;
        if (remainingSpace > footerHeight) ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (remainingSpace - footerHeight));
        if (ImGui.Button("Support development", new Vector2(ImGui.GetContentRegionAvail().X, 0))) {
            FileSystemUtils.OpenURL("https://ko-fi.com/shadowcookie");
        }
        var availSpace = ImGui.GetContentRegionAvail().X;
        ImGui.Button("Github Link");
        ImGui.SameLine();
        ImGui.Button("Wiki Link");
        ImGui.SameLine();
        ImGui.Button("Discord Link");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconActive);
        // SILVER: Maybe even a notification when a new version is up?
        ImGui.Text("[AppVersion/UpdateTime]");
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

    private static void ShowTabs()
    {
        if (ImGui.BeginTabBar("HomeTabs")) {
            if (ImGui.BeginTabItem("Bundles")) {
                // TODO SILVER: I of course totally know how to do this 100% ref: https://github.com/WolvenKit/WolvenKit?tab=readme-ov-file#screenshots
                ImGui.Text("In a grid with the app logo being used as the image/icon.\nCould be color coded by game.");
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Update Notes")) {
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Lorem Ipsum")) {
                ImGui.EndTabItem();
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
