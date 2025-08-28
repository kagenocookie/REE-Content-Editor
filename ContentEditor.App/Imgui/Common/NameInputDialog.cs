
using System.Numerics;
using System.Text.RegularExpressions;
using ContentEditor.App.Windowing;
using ContentEditor.Editor;
using ImGuiNET;

namespace ContentEditor.App;

public class NameInputDialog : IWindowHandler
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => "Name Input";

    private readonly string title;
    private readonly string text;
    private readonly IRectWindow parent;
    public Action<string> OnConfirmed;
    public Action? OnCancelled;

    private string input;
    private readonly Regex validationRegex;

    private WindowData data = null!;
    protected UIContext context = null!;

    public NameInputDialog(string title, string text, string initialName, Regex validationRegex, IRectWindow parent, Action<string> onConfirmed, Action? onCancelled = null)
    {
        this.title = title;
        this.text = text;
        this.input = initialName;
        this.validationRegex = validationRegex;
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
        var modalSize = new Vector2(Math.Max(size.X / 2, 800), Math.Max(size.Y / 4, 200));
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
        if (ImGui.InputText("Name", ref input, 300, ImGuiInputTextFlags.CharsNoBlank)) {
        }
        var valid = validationRegex == null || validationRegex.IsMatch(input);
        if (!valid) {
            ImGui.TextColored(Colors.Error, "Chosen name contains invalid characters");
        }


        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        if (!valid) ImGui.BeginDisabled();
        if (ImGui.Button("Confirm", new Vector2(modalSize.X / 2 - 12, 28))) {
            OnConfirmed.Invoke(input);
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }
        if (!valid) ImGui.EndDisabled();
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