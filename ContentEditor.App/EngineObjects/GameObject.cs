using System.Runtime.CompilerServices;
using ContentPatcher;
using ReeLib;
using ReeLib.Pfb;
using ReeLib.Scn;
using Silk.NET.Maths;

namespace ContentEditor.App;

public class NotifiableObject
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public delegate void PropertyChangedEventHandler(object? sender, string propertyName);

    protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, propertyName);
}

[Flags]
public enum SceneFlags
{
    Draw = (1 << 0),
    Selectable = (1 << 1),
    Update = (1 << 2),
    All = Draw|Selectable|Update,
}

public sealed class GameObject : NodeObject<GameObject>, IDisposable, IGameObject, INodeObject<GameObject>
{
    public string Tags = string.Empty;
    public bool Update;
    public bool Draw;
    public float TimeScale;
    public Guid guid;
    public string? PrefabPath;
    public readonly List<Component> Components = new();

    public Transform Transform { get; }

    public Folder? Folder { get; internal set; }
    private List<GameObject> _BaseChildren => base.Children;
    public new IEnumerable<GameObject> Children => base.Children;

    public Matrix4X4<float> WorldTransform => Transform.WorldTransform;

    public SceneFlags SceneFlags { get; set; } = SceneFlags.All;

    public bool ShouldDraw => (SceneFlags & SceneFlags.Draw) != 0 && (Parent == null ? Folder?.ShouldDraw != false : Parent?.ShouldDraw != false);
    public bool ShouldDrawSelf
    {
        get => (SceneFlags & SceneFlags.Draw) != 0;
        set => SceneFlags = (value ? SceneFlags|SceneFlags.Draw : SceneFlags&~SceneFlags.Draw);
    }

    protected override string GetPath()
    {
        if (_parent != null) {
            return _parent.Path + "/" + Name;
        }
        if (Folder != null) {
            return Folder.Path + "//" + Name;
        }
        return Name;
    }

    private RszInstance instance;

    string? IGameObject.Name => Name;
    public RszInstance? Instance => instance;
    IList<RszInstance> IGameObject.Components => Components.Select(c => c.Data).ToList();
    IEnumerable<IGameObject> IGameObject.GetChildren() => Children;

    private GameObject(RszInstance instance, RszInstance transformInstance)
    {
        this.instance = instance;
        ImportInstanceFields();
        Components.Add(Transform = new Transform(this, transformInstance));
    }

    public GameObject(string name, Workspace workspace, Folder? folder = null, Scene? scene = null)
    {
        instance = RszInstance.CreateInstance(workspace.RszParser, workspace.Classes.GameObject);
        Name = name;
        Update = true;
        Folder = folder;
        Draw = true;
        TimeScale = -1;
        guid = Guid.NewGuid();
        Components.Add(Transform = new Transform(this, workspace));
        Transform.ComponentInit();
        Scene = scene;
    }

    public GameObject(PfbGameObject source, Scene? scene = null)
    {
        instance = source.Instance!;
        ImportInstanceFields();
        Scene = scene;

        foreach (var comp in source.Components) {
            if (comp.RszClass.name == "via.Transform") {
                Components.Add(Transform = new Transform(this, comp));
            } else {
                Component.Create(this, comp, false);
            }
        }
        if (Transform == null) {
            Transform = CreateTransformComponent();
        }

        foreach (var sourceChild in source.Children) {
            var child = new GameObject(sourceChild, scene) {
                _parent = this
            };
            base.Children.Add(child);
        }

        if (scene?.IsActive == true) {
            ActivateComponents();
        }
    }

    public GameObject(ScnGameObject source, Folder folder, IList<ScnPrefabInfo>? prefabs = null, Scene? scene = null)
    {
        instance = source.Instance!;
        ImportInstanceFields();
        Scene = scene;
        guid = source.Guid;
        this.Folder = folder;
        PrefabPath = prefabs == null || !(source.Info?.prefabId >= 0) ? null : prefabs[source.Info.prefabId].Path;

        foreach (var comp in source.Components) {
            if (comp.RszClass.name == "via.Transform") {
                Components.Add(Transform = new Transform(this, comp));
            } else {
                Component.Create(this, comp, false);
            }
        }
        if (Transform == null) {
            Transform = CreateTransformComponent();
        }

        foreach (var sourceChild in source.Children) {
            var child = new GameObject(sourceChild, folder, prefabs, scene) {
                _parent = this
            };
            base.Children.Add(child);
        }

        if (scene?.IsActive == true) {
            ActivateComponents();
        }
    }

    private Transform CreateTransformComponent()
    {
        var workspace = Scene?.Workspace ?? Folder?.Scene?.Workspace;
        if (workspace == null) {
            throw new Exception("Could not create GameObject - no transform component was given and no root workspace is accessible");
        }
        var transform = new Transform(this, RszInstance.CreateInstance(workspace.Env.RszParser, workspace.Env.Classes.Transform));
        Components.Insert(0, transform);
        return transform;
    }

    public bool HasComponent<TComponent>() where TComponent : Component
    {
        return Components.OfType<TComponent>().Any();
    }

    public bool HasComponent(string classname)
    {
        foreach (var comp in Components) {
            if (comp.Classname == classname) return true;
        }

        return false;
    }

    public void AddComponent<TComponent>(TComponent component) where TComponent : Component
    {
        Components.Add(component);
    }

    public TComponent AddComponent<TComponent>() where TComponent : Component, IFixedClassnameComponent
    {
        var workspace = Scene?.Workspace ?? Folder?.Scene?.Workspace;
        if (workspace == null) {
            throw new Exception("Could not create Component - workspace is not accessible");
        }
        return (TComponent)Component.Create(this, workspace.Env, TComponent.Classname);
    }

    public Component AddComponent(string classname)
    {
        var workspace = Scene?.Workspace ?? Folder?.Scene?.Workspace;
        if (workspace == null) {
            throw new Exception("Could not create Component - workspace is not accessible");
        }
        return Component.Create(this, workspace.Env, classname);
    }

    public TComponent? GetComponent<TComponent>() where TComponent : Component
    {
        return Components.OfType<TComponent>().FirstOrDefault();
    }

    public TComponent RequireComponent<TComponent>() where TComponent : Component
    {
        return Components.OfType<TComponent>().First();
    }

    public GameObject? Find(ReadOnlySpan<char> path)
    {
        var part = path.IndexOf('/');
        if (part == -1) {
            return GetChild(path);
        }

        return GetChild(path.Slice(0, part))?.Find(path.Slice(part + 1));
    }

    public PfbGameObject ToPfbGameObject()
    {
        ExportInstanceFields();

        var obj = new PfbGameObject() {
            Instance = instance,
        };
        foreach (var comp in Components) {
            obj.Components.Add(comp.Data);
        }

        foreach (var child in Children) {
            var pfb = child.ToPfbGameObject();
            pfb.Parent = obj;
            obj.Children.Add(pfb);
        }

        return obj;
    }

    public ScnGameObject ToScnGameObject(List<ScnPrefabInfo>? prefabInfos)
    {
        ExportInstanceFields();

        var obj = new ScnGameObject() {
            Instance = instance,
        };
        obj.Info ??= new();
        obj.Info.guid = this.guid;
        if (prefabInfos != null && !string.IsNullOrEmpty(PrefabPath)) {
            var pfbId = prefabInfos.FindIndex(info => info.Path == PrefabPath);
            if (pfbId == -1) {
                pfbId = prefabInfos.Count;
                prefabInfos.Add(new ScnPrefabInfo() { Path = PrefabPath });
            }
            obj.Info.prefabId = pfbId;
            obj.Prefab = prefabInfos[pfbId];
        } else {
            obj.Info.prefabId = -1;
        }

        foreach (var comp in Components) {
            obj.Components.Add(comp.Data);
        }

        foreach (var child in Children) {
            var pfb = child.ToScnGameObject(prefabInfos);
            pfb.Parent = obj;
            obj.Children.Add(pfb);
        }

        return obj;
    }

    private void ImportInstanceFields()
    {
        Name = (string)instance.Values[0];
        Tags = (string)instance.Values[1];
        Draw = (bool)instance.Values[2];
        Update = (bool)instance.Values[3];
        TimeScale = instance.Values.Length > 4 ? (float)instance.Values[4] : -1;
    }
    private void ExportInstanceFields()
    {
        instance.Values[0] = Name;
        instance.Values[1] = Tags;
        instance.Values[2] = Draw;
        instance.Values[3] = Update;
        if (instance.Values.Length > 4) {
            instance.Values[4] = TimeScale;
        }
    }

    internal void MoveToScene(Scene? newScene)
    {
        if (Scene != null) {
            // we probably need to handle some more edge cases here
            if (Parent == null) {
                DeactivateComponents();
            } else if (Parent.Scene != newScene) {
                DeactivateComponents();
                Parent.RemoveChild(this);
            }
        }
        Scene = newScene;
        foreach (var child in Children) {
            child.MoveToScene(newScene);
        }
        if (newScene?.IsActive == true) {
            ActivateComponents();
        }
    }

    internal void MoveToFolder(Folder? folder)
    {
        if (Folder != null) {
            // we probably need to handle some more edge cases here
            Folder.GameObjects.Remove(this);
        }
        Folder = folder;
        foreach (var child in Children) {
            child.MoveToFolder(folder);
        }

        if (folder != null && folder.Scene != Scene) {
            MoveToScene(folder.Scene);
        }
    }

    public GameObject Clone(GameObject? parent = null)
    {
        ExportInstanceFields();
        var newObj = new GameObject(instance.Clone(), Transform.Data.Clone()) {
            Scene = parent?.Scene ?? Scene,
            _parent = parent,
        };
        foreach (var comp in Components) {
            if (comp is Transform) continue;

            comp.CloneTo(newObj);
        }
        foreach (var child in Children) {
            child.Clone(newObj);
        }
        if (parent != null) {
            parent._BaseChildren.Add(newObj);
        }

        return newObj;
    }

    public void SetActive(bool active)
    {
        if (active) {
            ActivateComponents();
        } else {
            DeactivateComponents();
        }
        foreach (var child in Children) {
            child.SetActive(active);
        }
    }

    private void ActivateComponents()
    {
        foreach (var comp in Components) {
            comp.OnActivate();
        }

        foreach (var child in GetAllChildren()) {
            foreach (var comp in child.Components) {
                comp.OnActivate();
            }
        }
    }

    private void DeactivateComponents()
    {
        foreach (var comp in Components) {
            comp.OnDeactivate();
        }

        foreach (var child in GetAllChildren()) {
            foreach (var comp in child.Components) {
                comp.OnDeactivate();
            }
        }
    }

    public void Dispose()
    {
        foreach (var child in Children) {
            child.Dispose();
        }

        foreach (var comp in Components) {
            (comp as IDisposable)?.Dispose();
        }
    }

    INodeObject<GameObject>? INodeObject<GameObject>.GetParent() => (INodeObject<GameObject>?)_parent ?? Folder;

    public override string ToString() => Name;
}

public class MaterialResource : IResourceFile
{
    public void WriteTo(string filepath)
    {
        throw new NotImplementedException();
    }
}
