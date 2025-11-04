using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;

namespace ContentEditor.App;

public class SceneRoot : IDisposable
{
    public readonly SceneComponentsList<IGizmoComponent> Gizmos = new();
    private readonly Scene scene;
    private readonly Camera editorCamera;

    public EditModeHandler? ActiveEditMode { get; private set; }
    private readonly Dictionary<string, (EditModeHandler? handler, HashSet<IEditableComponent> list)> _editableComponents = new();

    private GizmoManager? gizmoManager;
    internal GizmoManager? GizmoManager => gizmoManager;

    public Camera Camera => editorCamera;
    public Scene Scene => scene;

    public SceneMouseHandler MouseHandler { get; }
    public SceneController Controller { get; }

    public SceneRoot(Scene scene, Camera camera)
    {
        this.scene = scene;
        this.editorCamera = camera;
        MouseHandler = new(scene);
        Controller = new(scene);
    }

    public void RegisterEditableComponent(IEditableComponent component)
    {
        if (!_editableComponents.TryGetValue(component.EditTypeID, out var list)) {
            _editableComponents[component.EditTypeID] = list = (null, new HashSet<IEditableComponent>());
        }

        list.list.Add(component);
    }

    public void UnregisterEditableComponent(IEditableComponent component)
    {
        if (_editableComponents.TryGetValue(component.EditTypeID, out var list)) {
            list.list.Remove(component);
            if (ActiveEditMode == list.handler && ActiveEditMode?.Target == component) {
                ActiveEditMode.SetTarget(null);
            }
        }
    }

    public IEnumerable<IEditableComponent> GetEditableComponents(string editModeId)
    {
        if (_editableComponents.TryGetValue(editModeId, out var list)) {
            return list.list;
        }
        return Array.Empty<IEditableComponent>();
    }

    public EditModeHandler? SetEditMode<TComponent>(TComponent component) where TComponent : Component, IEditableComponent
    {
        var typeId = component.EditTypeID;
        if (_editableComponents?.TryGetValue(typeId, out var list) != true || !list.list.Contains(component)) {
            Logger.Error($"Attempted to enter {typeId} edit mode of unregistered component " + component);
            return null;
        }

        if (ActiveEditMode != null) {
            if (ActiveEditMode.Target == component) return ActiveEditMode;

            if (ActiveEditMode.EditTypeID != typeId) {
                ActiveEditMode.SetTarget(component);
                return null;
            }

            ActiveEditMode.SetTarget(null);
        }

        if (list.handler == null) {
            list.handler = (EditModeHandler)Activator.CreateInstance(component.EditHandlerType)!;
            list.handler.Init(scene);
            _editableComponents[typeId] = list;
        }

        ActiveEditMode = list.handler;
        ActiveEditMode.SetTarget(component);
        return ActiveEditMode;
    }

    internal void Update(float deltaTime)
    {
        if (!Gizmos.IsEmpty || Controller != null) {
            gizmoManager ??= new(scene);
            ActiveEditMode?.Update();
            gizmoManager.Update();
            Controller?.UpdateGizmo(EditorWindow.CurrentWindow!, gizmoManager);
        }
    }

    internal void Render()
    {
        gizmoManager?.Render();
    }

    internal void RenderUI()
    {
        ActiveEditMode?.OnIMGUI();
        gizmoManager?.RenderUI();
    }

    public void Dispose()
    {
        gizmoManager?.Dispose();
        foreach (var ee in _editableComponents) {
            (ee.Value.handler as IDisposable)?.Dispose();
        }
    }
}
