using ContentEditor.App.Windowing;

namespace ContentEditor.App.ImguiHandling;

public class InspectorContainer(IWindowHandler windowHandler, UIContext uiContext)
{
    private ObjectInspector? primaryInspector;
    private readonly List<ObjectInspector> inspectors = new();
    public bool IsEmbedded { get; init; }

    public ObjectInspector? PrimaryInspector => primaryInspector;
    public object? PrimaryTarget {
        get => primaryInspector?.Target;
        set => SetPrimaryInspector(value);
    }

    public IEnumerable<ObjectInspector> Inspectors => inspectors.AsReadOnly();

    public void Reset()
    {
        if (primaryInspector != null) primaryInspector.Target = null!;
        CloseAll(true);
    }

    public void SetPrimaryInspector(object? target)
    {
        if (primaryInspector == null) {
            primaryInspector = AddInspector(target);
        } else {
            primaryInspector.Target = target;
        }
    }

    public ObjectInspector AddInspector(object? target)
    {
        var inspector = new ObjectInspector(windowHandler);
        WindowData window;
        if (IsEmbedded && primaryInspector == null) {
            window = WindowData.CreateEmbeddedWindow(uiContext, uiContext.GetWindow()!, inspector, "Inspector");
        } else {
            window = EditorWindow.CurrentWindow!.AddSubwindow(inspector);
        }
        var child = uiContext.AddChild("Inspector", window, NullUIHandler.Instance);
        inspectors.Add(inspector);
        inspector.Target = target;
        inspector.Closed += () => OnInspectorClosed(inspector);
        return inspector;
    }

    public void CloseAll(bool keepPrimary = false)
    {
        for (int i = inspectors.Count - 1; i >= 0; i--) {
            var inspector = inspectors[i];
            if (keepPrimary && inspector == primaryInspector) continue;
            EditorWindow.CurrentWindow?.CloseSubwindow(inspector);
        }
    }

    public void EmitSave()
    {
        foreach (var inspector in inspectors) inspector.Context.Save();
    }

    private void OnInspectorClosed(ObjectInspector inspector)
    {
        inspectors.Remove(inspector);
        if (primaryInspector == inspector) {
            primaryInspector = null;
        }
    }

    public void Add(ObjectInspector inspector)
    {
        inspectors.Add(inspector);
        inspector.Closed += () => OnInspectorClosed(inspector);
    }

    public ObjectInspector Add(object? target)
    {
        var inspector = new ObjectInspector(windowHandler);
        var window = EditorWindow.CurrentWindow!.AddSubwindow(inspector);
        var child = uiContext.AddChild("Inspector", window, NullUIHandler.Instance);
        inspectors.Add(inspector);
        inspector.Target = target;
        inspector.Closed += () => OnInspectorClosed(inspector);
        return inspector;
    }
}
