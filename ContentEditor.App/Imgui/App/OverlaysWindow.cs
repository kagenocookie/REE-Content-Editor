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
    private List<ToastData> Toasts { get; } = new();

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

    public void ShowToast(float duration, string message, params (string? label, Action action)[] buttons)
    {
        Toasts.Add(new ToastData(NextToastMsgId++) {
            message = message,
            disappearAt = DateTime.Now.AddSeconds(duration),
            Buttons = buttons ?? [],
        });
    }

    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        var size = ImGui.GetWindowViewport().Size - ImGui.GetStyle().WindowPadding;
        ShowHelpText(size);
        ShowTimedTooltip();
        ShowBackgroundTasks(size);
        ShowToasts(size);
    }

    private void ShowHelpText(Vector2 size)
    {
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
            ImGui.Begin("Guide", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus);
            ImguiHelpers.TextCentered(helptext);
            if (editorWindow != null && ImGui.IsItemClicked()) {
                PlatformUtils.ShowFileDialog((files) => {
                    Logger.Info(string.Join("\n", files));
                    editorWindow.OpenFiles(files);
                });
            }
            ImGui.End();
        }
    }

    private void ShowTimedTooltip()
    {
        if (tooltipTime > 0) {
            ImGui.BeginTooltip();
            ImGui.Text(tooltipMsg);
            ImGui.EndTooltip();
            tooltipTime -= Time.Delta;
            if (tooltipTime <= 0) {
                tooltipMsg = null;
            }
        }
    }

    private static void ShowBackgroundTasks(Vector2 size)
    {
        var bg = MainLoop.Instance.BackgroundTasks;
        var runningTasks = bg.PendingTasks;
        if (runningTasks > 0) {
            var taskWindowSize = new Vector2(400, 240);
            ImGui.SetNextWindowPos(size - taskWindowSize - ImGui.GetStyle().WindowPadding, ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(taskWindowSize, ImGuiCond.Appearing);
            ImGui.SetNextWindowCollapsed(false, ImGuiCond.Appearing);
            if (ImGui.Begin("BackgroundTasks", ImGuiWindowFlags.NoSavedSettings)) {
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

    private void ShowToasts(Vector2 size)
    {
        var toastOffsetY = 0f;
        var toastBottom = size.Y - ImGui.GetStyle().WindowPadding.Y;
        for (int i = Toasts.Count - 1; i >= 0; i--) {
            var toast = Toasts[i];
            if (toast.disappearAt <= DateTime.Now) {
                Toasts.RemoveAt(i);
                continue;
            }

            var textSize = ImGui.CalcTextSize(toast.message);
            var windowPadding = ImGui.GetStyle().WindowPadding;

            var height = textSize.Y + windowPadding.Y * 2;
            if (toast.Buttons.Length > 0) height += ImGui.GetFrameHeightWithSpacing() + 4;

            var toastWidth = textSize.X + windowPadding.X * 2 + ImGui.GetFrameHeight();
            var toastLeft = size.X - toastWidth - windowPadding.X * 2;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 3f);
            var pos = new Vector2(toastLeft, toastBottom - height - toastOffsetY);
            ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(toastWidth, height), ImGuiCond.Always);
            ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
            var closePos = new Vector2(toastLeft + toastWidth - ImGui.GetFrameHeight(), pos.Y + windowPadding.Y);
            var close = false;
            if (ImGui.Begin($"###Toast{toast.ID}", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoNavInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoMove)) {
                ImGui.Text(toast.message);
                if (toast.Buttons.Length > 0) {
                    ImGui.Separator();
                    int c = 0;
                    foreach (var (label, act) in toast.Buttons) {
                        if (c++ > 0) ImGui.SameLine();
                        if (ImGui.Button(string.IsNullOrEmpty(label) ? "Confirm" : label)) {
                            act.Invoke();
                            close = true;
                        }
                    }
                }
            }
            close |= ImGuiP.CloseButton(ImGui.GetID("CloseButton"), closePos);
            if (close) {
                Toasts.RemoveAt(i);
            }
            ImGui.End();
            ImGui.PopStyleVar();
            toastOffsetY += height + ImGui.GetStyle().ItemSpacing.Y * 2;
        }
    }

    private static int NextToastMsgId = 1;

    private sealed class ToastData(int id)
    {
        public string message = "";
        public DateTime disappearAt;
        public (string? label, Action action)[] Buttons = [];

        public int ID { get; } = id;
    }

    public bool RequestClose()
    {
        return false;
    }
}
