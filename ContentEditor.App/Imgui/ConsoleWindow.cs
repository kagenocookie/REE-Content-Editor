using System.Numerics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;

namespace ContentEditor.App;

public class ConsoleWindow : IWindowHandler, IKeepEnabledWhileSaving
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => "Log";

    private static string[] tabs = ["All", "Debug", $"{AppIcons.SI_GenericInfo} Info", $"{AppIcons.SI_GenericWarning} Warning", $"{AppIcons.SI_GenericError} Error"];
    private int currentTab;
    private bool compactMultiline;

    internal static EventLogger? EventLogger { get; set; }
    private bool isOpen = false;

    private sealed record LogEntry(string message, LogSeverity level);
    private readonly List<LogEntry> all = new();
    private readonly List<LogEntry> debug = new();
    private readonly List<LogEntry> info = new();
    private readonly List<LogEntry> warn = new();
    private readonly List<LogEntry> error = new();

    private static Vector4 GetColor(LogSeverity severity) => severity switch {
        LogSeverity.Debug => Colors.Faded,
        LogSeverity.Warning => Colors.Warning,
        LogSeverity.Error => Colors.Error,
        _ => Colors.Default,
    };

    private WindowData? window;

    private List<LogEntry> GetListForTab(int level) => level switch {
        0 => all,
        1 => debug,
        2 => info,
        3 => warn,
        4 => error,
        _ => info,
    };
    private List<LogEntry> GetListForLevel(LogSeverity level) => level switch {
        LogSeverity.Debug => debug,
        LogSeverity.Info => info,
        LogSeverity.Warning => warn,
        LogSeverity.Error => error,
        _ => info,
    };

    private WindowData data = null!;
    protected UIContext context = null!;

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void AddMessage(string message, LogSeverity level)
    {
        var entry = new LogEntry(string.Format("[{0:HH:mm:ss}] [{1}] {2}", DateTime.Now, level, message), level);
        all.Add(entry);
        GetListForLevel(level).Add(entry);
    }

    void IWindowHandler.OnIMGUI() => OnWindow();
    public void OnWindow()
    {
        this.window = data;

        if (all.Count > 0) isOpen = true;
        if (!isOpen) return;

        var windowSize = ImGui.GetWindowSize() - new Vector2(ImGui.GetStyle().WindowPadding.X * 2, 0);
        var height = windowSize.Y / 4;
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(windowSize.X, height), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(0, windowSize.Y - height), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Log", ref isOpen)) {
            data.Size = ImGui.GetWindowSize();
            data.Position = ImGui.GetWindowPos();
            ImguiHelpers.Tabs(tabs, ref currentTab, true);
            ImGui.SameLine();
            if (ImGui.Button("Clear")) {
                Clear();
            }
            ImGui.SameLine();
            var list = GetListForTab(currentTab);
            if (ImGui.Button("Copy all")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(string.Join("\n", list.Select(l => l.message)), "Copied!");
            }
            ImGui.SameLine();
            ImguiHelpers.ToggleButton($"{AppIcons.SI_LogCompact}", ref compactMultiline, Colors.IconActive);
            ImguiHelpers.Tooltip("Compact multiline messages");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.BeginChild("Content");
            var isMaxScrolled = ImGui.GetScrollY() >= ImGui.GetScrollMaxY();
            for (int i = 0; i < list.Count; i++) {
                var item = list[i];
                var col = GetColor(item.level);
                if (compactMultiline) {
                    var br = item.message.IndexOf('\n');
                    if (br != -1) {
                        ImguiHelpers.TextColoredWrapped(col, item.message[..br]);
                    } else {
                        ImguiHelpers.TextColoredWrapped(col, item.message);
                    }
                } else {
                    ImguiHelpers.TextColoredWrapped(col, item.message);
                }
                if (ImGui.IsItemHovered()) {
                    var pos = ImGui.GetCursorScreenPos();
                    var lineY = pos.Y - ImGui.GetStyle().FramePadding.Y;
                    ImGui.GetWindowDrawList()
                        .AddRectFilled(new Vector2(pos.X, lineY), new Vector2(data.Size.X, lineY - ImGui.GetItemRectSize().Y), 0x88fffff);
                }
                if (ImGui.IsItemClicked()) {
                    EditorWindow.CurrentWindow?.CopyToClipboard(item.message, "Copied!");
                }
            }

            if (isMaxScrolled) {
                ImGui.SetScrollHereY();
            }
            ImGui.EndChild();
        }

        ImGui.End();
        if (!isOpen) {
            Clear();
        }
    }

    public void Clear()
    {
        all.Clear();
        debug.Clear();
        info.Clear();
        warn.Clear();
        error.Clear();
    }

    private void OnMesageReceived(string msg, LogSeverity severity)
    {
        if (EditorWindow.CurrentWindow != null && window?.ParentWindow != EditorWindow.CurrentWindow) {
            return;
        }
        AddMessage(msg, severity);
    }

    public void OnOpen()
    {
        if (EventLogger != null) {
            EventLogger.MessageReceived += OnMesageReceived;
        }
    }

    public bool RequestClose()
    {
        if (EventLogger != null) {
            EventLogger.MessageReceived -= OnMesageReceived;
        }
        return false;
    }
}
