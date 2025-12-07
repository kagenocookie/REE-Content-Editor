
using System.Numerics;
using ContentEditor.App.Windowing;
using ContentEditor.Editor;

namespace ContentEditor.App;

public class ErrorModal : IWindowHandler, IDisposable
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => "Error";

    private readonly string title;
    private readonly string text;
    private readonly IRectWindow? parent;
    public Action? OnClosed;

    private WindowData data = null!;
    protected UIContext context = null!;

    public ErrorModal(string title, string text, IRectWindow? parent = null, Action? onClosed = null)
    {
        this.title = title;
        this.text = text;
        this.parent = parent;
        OnClosed = onClosed;
    }

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }
    void IWindowHandler.OnIMGUI() => OnWindow();

    public void OnWindow()
    {
        // var size = window._window.Size;
        var p = ImGui.GetStyle().WindowPadding;
        var size = parent?.Size ?? ImGui.GetIO().DisplaySize;
        var modalSize = new Vector2(Math.Max(size.X / 2, 600), Math.Max(size.Y / 4, 200));
        var btnHeight = UI.FontSize + ImGui.GetStyle().FramePadding.X * 2;

        ImGui.SetNextWindowFocus();
        if (parent != null) {
            ImGui.SetNextWindowPos(parent.Position + new Vector2(size.X / 2 - modalSize.X / 2, size.Y / 2 - modalSize.Y / 2));
        } else {
            ImGui.SetNextWindowPos(new Vector2(size.X / 2 - modalSize.X / 2, size.Y / 2 - modalSize.Y / 2));
        }
        ImGui.SetNextWindowSize(modalSize);
        // ImGui.BeginPopupModal(title, ImGuiWindowFlags.Modal|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoDocking|ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoCollapse|ImGuiWindowFlags.Popup);
        ImGui.Begin(title, ImGuiWindowFlags.Modal|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoDocking|ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoCollapse);
        ImGui.Spacing();

        var ts = ImGui.CalcTextSize(title).X;
        ImGui.Indent(ts / 2);
        ImGui.Text(text);
        ImGui.Unindent(ts / 2);

        ImGui.SetCursorPosY(modalSize.Y - 48);
        if (ImGui.Button("OK", new Vector2(modalSize.X - 16, btnHeight))) {
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }

        // ImGui.EndPopup();
        ImGui.End();
    }

    public bool RequestClose()
    {
        return false;
    }

    public void Dispose()
    {
        OnClosed?.Invoke();
    }
}