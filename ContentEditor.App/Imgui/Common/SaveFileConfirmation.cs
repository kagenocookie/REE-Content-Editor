
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher;
using Hexa.NET.ImGui;
using System.Numerics;

namespace ContentEditor.App;

public class SaveFileConfirmation : IWindowHandler
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => "Confirmation";

    public List<FileHandle> Files { get; }

    private readonly TranslatableBase title;
    private readonly TranslatableBase text;
    private readonly IRectWindow parent;
    public Action OnConfirmed;
    public Action? OnCancelled;

    private WindowData data = null!;
    protected UIContext context = null!;

    public SaveFileConfirmation(TranslatableBase title, TranslatableBase text, IEnumerable<FileHandle> files, IRectWindow parent, Action onConfirmed, Action? onCancelled = null)
    {
        this.title = title;
        this.text = text;
        Files = files.ToList();
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
        var style = ImGui.GetStyle();
        float headerH = 40f;
        var modalSize = new Vector2(Math.Max(ImGui.CalcTextSize(text).X + style.WindowPadding.X * 2, 600),
            headerH + ImGui.CalcTextSize(text, ImGui.GetContentRegionAvail().X).Y + ImGui.GetFrameHeight() + style.ItemSpacing.Y * 4 + style.WindowPadding.Y * 2);

        ImGui.SetNextWindowFocus();
        ImGui.SetNextWindowPos(parent.Position + (size - modalSize) * 0.5f);
        ImGui.SetNextWindowSize(modalSize);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2);
        ImGui.Begin(title, ImGuiWindowFlags.Modal | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoDecoration);
        var draw = ImGui.GetWindowDrawList();

        Vector2 headerMin = ImGui.GetCursorScreenPos() - style.WindowPadding;
        Vector2 headerMax = new(headerMin.X + ImGui.GetWindowSize().X, headerMin.Y + headerH);
        draw.AddRectFilled(headerMin, headerMax, ImGui.GetColorU32(ImGuiCol.Border), style.WindowRounding);

        ImGui.Text($"{AppIcons.SI_Save}");
        ImGui.SameLine();
        ImGui.Text("Unsaved Changes");
        ImGui.Dummy(new Vector2(0, style.ItemSpacing.Y * 2));
        ImGui.Text(text);
        ImGui.Dummy(new Vector2(0, style.ItemSpacing.Y * 2));

        ImGui.Separator();
        float buttonW = (ImGui.GetContentRegionAvail().X - style.ItemSpacing.X * 2) / 3;
        if (ImGui.Button("Save", new Vector2(buttonW, 0))) {
            var allSuccessful = true;
            foreach (var file in Files) {
                if (!file.Save(context.GetWorkspace()!)) {
                    allSuccessful = false;
                }
            }

            if (allSuccessful) {
                OnConfirmed.Invoke();
                EditorWindow.CurrentWindow?.CloseSubwindow(this);
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Discard Changes", new Vector2(buttonW, 0))) {
            OnConfirmed?.Invoke();
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(buttonW, 0))) {
            OnCancelled?.Invoke();
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }
        ImGui.End();
        ImGui.PopStyleVar();
    }

    void IWindowHandler.OnIMGUI() => OnWindow();

    public bool RequestClose()
    {
        OnCancelled?.Invoke();
        return false;
    }
}
