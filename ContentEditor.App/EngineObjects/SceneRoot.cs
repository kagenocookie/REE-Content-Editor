using System.Diagnostics;
using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;

namespace ContentEditor.App;

public class SceneRoot : IDisposable
{
    public readonly SceneComponentsList<IGizmoComponent> Gizmos = new();
    public Scene Scene { get; }

    public EditModeHandler? ActiveEditMode { get; private set; }

    private GizmoManager? gizmoManager;
    internal GizmoManager? GizmoManager => gizmoManager;

    public Camera Camera { get; }

    /// <summary>
    /// Root GameObject for editor-only objects and components that aren't supposed to be part of the scene.
    /// </summary>
    public GameObject EditorRoot { get; }

    public SceneMouseHandler MouseHandler { get; }
    public SceneController Controller { get; }

    private readonly Dictionary<Type, (EditModeHandler? handler, HashSet<IEditableComponent> list)> _editableComponents = new();

    public SceneRoot(Scene scene)
    {
        Scene = scene;
        MouseHandler = new(scene);
        Controller = new(scene);
        EditorRoot = new GameObject("__editorRoot", scene.Workspace.Env);
        EditorRoot.ForceSetScene(scene);

        var camGo = new GameObject("__editorCamera", scene.Workspace.Env);
        Camera = Component.Create<Camera>(camGo, scene.Workspace.Env);
        EditorRoot.AddChild(camGo);
    }

    public void RegisterEditableComponent(IEditableComponent component)
    {
        if (!_editableComponents.TryGetValue(component.EditHandlerType, out var list)) {
            AddEditMode(component.EditHandlerType);
            list = _editableComponents[component.EditHandlerType];
        }

        list.list.Add(component);
    }

    public void UnregisterEditableComponent(IEditableComponent component)
    {
        if (_editableComponents.TryGetValue(component.EditHandlerType, out var list)) {
            list.list.Remove(component);
            if (ActiveEditMode == list.handler && ActiveEditMode?.Target == component) {
                ActiveEditMode.SetTarget(null);
            }
        }
    }

    public IEnumerable<EditModeHandler> GetAvailableEditModes()
    {
        return _editableComponents
            .Where(kv => kv.Value.list.Count > 0 && kv.Value.handler != null)
            .Select(kv => kv.Value.handler!);
    }

    public EditModeHandler GetOrAddEditMode<TEditMode>() where TEditMode : EditModeHandler, new()
    {
        if (!_editableComponents.TryGetValue(typeof(TEditMode), out var data) || data.handler == null) {
            return AddEditMode(typeof(TEditMode));
        }

        return data.handler;
    }

    public IEnumerable<IEditableComponent> GetEditableComponents(EditModeHandler handler)
    {
        if (_editableComponents.TryGetValue(handler.GetType(), out var list)) {
            return list.list;
        }
        return Array.Empty<IEditableComponent>();
    }

    public void DisableEditMode()
    {
        if (ActiveEditMode != null) {
            ActiveEditMode.SetTarget(null);
        }
        ActiveEditMode = null;
    }

    public EditModeHandler? SetEditMode<TComponent>(TComponent component) where TComponent : class, IEditableComponent
    {
        Debug.Assert(component is Component);
        var type = component.EditHandlerType;
        if (!_editableComponents.TryGetValue(type, out var list) || !list.list.Contains(component)) {
            Logger.Error($"Attempted to enter {type} edit mode of unregistered component " + component);
            return null;
        }

        if (ActiveEditMode != null) {
            if (ActiveEditMode.Target == component) return ActiveEditMode;

            if (ActiveEditMode.GetType() == type) {
                ActiveEditMode.SetTarget(component as Component);
                return ActiveEditMode;
            }

            ActiveEditMode.SetTarget(null);
        }

        ActiveEditMode = AddEditMode(component.EditHandlerType);
        ActiveEditMode.SetTarget(component as Component);
        return ActiveEditMode;
    }

    private EditModeHandler AddEditMode(Type editMode)
    {
        _editableComponents.TryGetValue(editMode, out var list);

        if (list.handler == null) {
            list.handler = (EditModeHandler)Activator.CreateInstance(editMode)!;
            list.handler.Init(Scene);
            list.list ??= new();
            _editableComponents[editMode] = list;
        }

        return list.handler;
    }

    internal void Update(float deltaTime)
    {
        if (!Gizmos.IsEmpty || Controller != null) {
            gizmoManager ??= new(Scene);
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
