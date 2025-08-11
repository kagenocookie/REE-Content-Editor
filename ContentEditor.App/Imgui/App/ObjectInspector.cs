using ContentEditor.App.Windowing;
using ImGuiNET;

namespace ContentEditor.App.ImguiHandling;

public class ObjectInspector : IWindowHandler, IUIContextEventHandler, IObjectUIHandler
{
    public string HandlerName => "Inspector";

    public bool HasUnsavedChanges => throw new NotImplementedException();
    public event Action? Closed;

    private UIContext context = null!;
    public UIContext Context => context;
    public void Init(UIContext context)
    {
        this.context = context;
        context.uiHandler = this;
    }

    private object? _target;
    private IWindowHandler parentWindow;

    public ObjectInspector(IWindowHandler parentWindow)
    {
        this.parentWindow = parentWindow;
    }

    public object? Target
    {
        get => _target;
        set {
            _target = value;
            context?.ClearChildren();
        }
    }

    public void OnIMGUI()
    {
        if (context.children.Count == 0) {
            context.AddChild("Target", Target).AddDefaultHandler();
        }
        if (_target == null) {
            ImGui.TextColored(Colors.Faded, "No object selected");
            return;
        }
        if (Target is IPathedObject pathed) {
            ImGui.TextColored(Colors.Faded, $"Target: [{Target?.GetType().Name}] {Target}: {pathed.Path}");
        } else {
            ImGui.TextColored(Colors.Faded, $"Target: [{Target?.GetType().Name}] {Target}");
        }
        if (ImGui.Button("Duplicate window")) {
            if (parentWindow is IInspectorController inspectorController) {
                var newInspector = inspectorController.AddInspector(_target);
            } else {
                EditorWindow.CurrentWindow?.AddSubwindow(new ObjectInspector(parentWindow) { _target = _target });
            }
        }
        context.ShowChildrenUI();
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    public bool RequestClose() => false;

    void IWindowHandler.OnClosed()
    {
        Closed?.Invoke();
    }

    public bool HandleEvent(UIContext context, EditorUIEvent eventData)
    {
        (parentWindow as IUIContextEventHandler)?.HandleEvent(eventData.origin, eventData);
        if (eventData.type is UIContextEvent.Saved or UIContextEvent.Reverting) {
            return true;
        }
        return false;
    }

    public void OnIMGUI(UIContext context)
    {
        OnWindow();
    }
}
