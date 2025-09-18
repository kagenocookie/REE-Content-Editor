using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App;

public class OverlaysWindow : IWindowHandler
{
    public string HandlerName => "Overlays";

    public bool HasUnsavedChanges => false;
    public bool ShowHelp { get; set; }

    private WindowData data = null!;
    protected UIContext context = null!;

    public WindowData Window => data;

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    private string? tooltipMsg;
    private float tooltipTime = 0f;

    public void ShowTooltip(string message, float duration)
    {
        tooltipMsg = message;
        tooltipTime = duration;
    }

    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        var size = ImGui.GetWindowViewport().Size - ImGui.GetStyle().WindowPadding;
        string? helptext = null;
        if (!AppConfig.Instance.HasAnyGameConfigured) {
            helptext = "Go into the Tools > Settings menu and configure the game(s) you wish to edit";
        } else if (context.GetWorkspace() == null) {
            helptext = "Activate the game you wish to edit in the menu";
        } else if (ShowHelp) {
            helptext = "Drag & drop a supported file here or use the menu to open a file";
        }
        var editorWindow = data.ParentWindow as EditorWindow;
        if (helptext != null) {
            var wndSize = new Vector2(Math.Min(500, size.X), Math.Min(40, size.Y));
            ImGui.SetNextWindowPos(new Vector2((size.X - wndSize.X) / 2, (size.Y - wndSize.Y) / 2));
            ImGui.SetNextWindowSize(wndSize);
            ImGui.Begin("Guide", ImGuiWindowFlags.NoTitleBar|ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.NoCollapse);
            ImguiHelpers.TextCentered(helptext);
            if (editorWindow != null && ImGui.IsItemClicked()) {
                PlatformUtils.ShowFileDialog((files) => {
                    Logger.Info(string.Join("\n", files));
                    editorWindow.OpenFiles(files);
                });
            }
            ImGui.End();
        }

        if (tooltipTime > 0) {
            ImGui.BeginTooltip();
            ImGui.Text(tooltipMsg);
            ImGui.EndTooltip();
            tooltipTime -= Time.Delta;
            if (tooltipTime <= 0) {
                tooltipMsg = null;
            }
        }

        if (editorWindow != null && editorWindow.SceneManager.HasActiveMasterScene) {
            var style = ImGui.GetStyle();
            var btnSize = style.FramePadding * 2 + new Vector2(UI.FontSize, UI.FontSize);
            btnSize.X = 4 * (btnSize.X + style.FramePadding.X) + style.WindowPadding.X * 2;
            ImGui.SetNextWindowPos(new Vector2(size.X - btnSize.X - 8, 32));
            ImGui.SetNextWindowSize(new Vector2(btnSize.X, btnSize.Y + style.WindowPadding.Y * 2));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
            ImGui.Begin("##toolbar", ImGuiWindowFlags.NoBringToFrontOnFocus|ImGuiWindowFlags.NoCollapse|ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoTitleBar|ImGuiWindowFlags.NoDocking|ImGuiWindowFlags.NoScrollbar);
            var meshIcon = AppIcons.Mesh.ToString();

            // axis/grid display toggle
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Info with { W = AppConfig.Instance.RenderAxis.Get() ? 1 : 0.6f });
            if (ImGui.Button(meshIcon + "##axis")) {
                AppConfig.Instance.RenderAxis.Set(!AppConfig.Instance.RenderAxis);
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Show Axis / Grid");

            // meshes
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, AppConfig.Instance.RenderMeshes.Get() ? Colors.Default : Colors.Faded);
            if (ImGui.Button(meshIcon + "##mesh")) {
                AppConfig.Instance.RenderMeshes.Set(!AppConfig.Instance.RenderMeshes);
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Show Meshes");

            // colliders
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Colliders with { W = AppConfig.Instance.RenderColliders.Get() ? 1 : 0.6f });
            if (ImGui.Button(meshIcon + "##coll")) {
                AppConfig.Instance.RenderColliders.Set(!AppConfig.Instance.RenderColliders);
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Show Colliders");

            // rcol colliders
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.RequestSetColliders with { W = AppConfig.Instance.RenderRequestSetColliders.Get() ? 1 : 0.6f });
            if (ImGui.Button(meshIcon + "##rcol")) {
                AppConfig.Instance.RenderRequestSetColliders.Set(!AppConfig.Instance.RenderRequestSetColliders);
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Show Request Set Colliders (rcol)");

            ImGui.End();
            ImGui.PopStyleVar();
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}