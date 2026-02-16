using ContentEditor.App.Windowing;
using ContentEditor.Core;
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
        ImGui.Selectable("Resident Evil 2");
        ImGui.Selectable("Resident Evil 3");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconActive);
        ImGui.Selectable($"{AppIcons.SI_GenericAdd}");
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.Text("Add Game");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("Ko-fi Link");
        ImGui.Text("Github Link");
        ImGui.Text("Wiki Link");
        ImGui.Text("Discord Link");
        ImGui.Text("^As icon buttons on the same line");
        ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconActive);
        ImGui.Text("[App Version/Update date Here]");
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
