using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;

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
            //helptext = "Drag & drop a supported RE Engine resource file here or use the menu to open one.\nYou can access the game files directly using the Windows > PAK File Browser option.";
        }
        var editorWindow = data.ParentWindow as EditorWindow;
        if (helptext != null) {
            var linecount = helptext.Count(c => c == '\n') + 1;
            var wndSize = new Vector2(Math.Min(600, size.X), Math.Min(20 + linecount * 20, size.Y)) * UI.UIScale;
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

        var bg = MainLoop.Instance.BackgroundTasks;
        var runningTasks = bg.PendingTasks;
        if (runningTasks > 0) {
            var taskWindowSize = new Vector2(400, 240);
            ImGui.SetNextWindowPos(size - taskWindowSize - ImGui.GetStyle().WindowPadding, ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(taskWindowSize, ImGuiCond.Appearing);
            ImGui.SetNextWindowCollapsed(false, ImGuiCond.Appearing);
            if (ImGui.Begin("BackgroundTasks")) {
                ImGui.Text("Pending background tasks: " + runningTasks);

                var jobSize = new Vector2(ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X * 2, 30);
                foreach (var (job, progress) in bg.CurrentJobs) {
                    ImGui.Separator();
                    ImGui.ProgressBar(progress >= 0 && progress <= 1 ? progress : -1 * (float)ImGui.GetTime(), jobSize);
                    ImGui.TextWrapped(job);
                }
            }
            ImGui.End();
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}
