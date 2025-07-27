
using System.Numerics;
using ContentEditor.App.Windowing;
using ContentEditor.Editor;
using ImGuiNET;

namespace ContentEditor.App;

public class ConfirmationDialog : IWindowHandler
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => "Confirmation";

    private readonly string title;
    private readonly string text;
    private readonly IRectWindow parent;
    public Action OnConfirmed;
    public Action? OnCancelled;

    private WindowData data = null!;
    protected UIContext context = null!;

    public ConfirmationDialog(string title, string text, IRectWindow parent, Action onConfirmed, Action? onCancelled = null)
    {
        this.title = title;
        this.text = text;
        this.parent = parent;
        OnConfirmed = onConfirmed;
        OnCancelled = onCancelled;
    }

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow()
    {
        var size = parent.Size;
        var modalSize = new Vector2(Math.Max(size.X / 2, 600), Math.Max(size.Y / 4, 200));
        ImGui.SetNextWindowFocus();
        ImGui.SetNextWindowPos(parent.Position + new Vector2(size.X / 2 - modalSize.X / 2, size.Y / 2 - modalSize.Y / 2));
        ImGui.SetNextWindowSize(modalSize);
        ImGui.Begin(title, ImGuiWindowFlags.Modal|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoDocking|ImGuiWindowFlags.NoResize);
        ImGui.Spacing();

        var ts = ImGui.CalcTextSize(title).X;
        ImGui.Indent(ts / 2);
        ImGui.Text(text);
        ImGui.Unindent(ts / 2);

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        if (ImGui.Button("Confirm", new Vector2(modalSize.X / 2 - 12, 28))) {
            OnConfirmed.Invoke();
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(modalSize.X / 2 - 12, 28))) {
            OnCancelled?.Invoke();
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }

        ImGui.End();
    }

    void IWindowHandler.OnIMGUI() => OnWindow();

    public bool RequestClose()
    {
        OnCancelled?.Invoke();
        return false;
    }
}