
using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Editor;
using ContentPatcher;
using ImGuiNET;

namespace ContentEditor.App;

public class SaveFileConfirmation : IWindowHandler
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => "Confirmation";

    public FileHandle File { get; }

    private readonly string title;
    private readonly string text;
    private readonly IRectWindow parent;
    public Action OnConfirmed;
    public Action? OnCancelled;

    private WindowData data = null!;
    protected UIContext context = null!;

    public SaveFileConfirmation(string title, string text, FileHandle file, IRectWindow parent, Action onConfirmed, Action? onCancelled = null)
    {
        this.title = title;
        this.text = text;
        File = file;
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
        if (ImGui.Button("Save", new Vector2(modalSize.X / 3 - 12, 28))) {
            File.Save(context.GetWorkspace()!);
            OnConfirmed.Invoke();
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }
        ImGui.SameLine();
        if (ImGui.Button("Don't save (discard changes)", new Vector2(modalSize.X / 3 - 12, 28))) {
            OnConfirmed?.Invoke();
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(modalSize.X / 3 - 12, 28))) {
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