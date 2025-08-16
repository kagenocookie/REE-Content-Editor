using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using ContentEditor.Core;
using ReeLib;
using ReeLib.Common;
using ReeLib.Pfb;
using ReeLib.Scn;
using ReeLib.UVar;

namespace ContentEditor.App;

public class NotifiableObject
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public delegate void PropertyChangedEventHandler(object? sender, string propertyName);

    protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, propertyName);
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

    public Folder? Folder { get; internal set; }
    private List<GameObject> _BaseChildren => base.Children;
    public new IEnumerable<GameObject> Children => base.Children;

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

    private GameObject(RszInstance instance)
    {
        this.instance = instance;
        ImportInstanceFields();
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
        Components.Add(new Transform(this, workspace));
        Scene = scene;
    }

    public GameObject(PfbGameObject source, Scene? scene = null)
    {
        instance = source.Instance!;
        ImportInstanceFields();
        Scene = scene;

        foreach (var comp in source.Components) {
            Components.Add(new Component(this, comp));
        }

        foreach (var sourceChild in source.Children) {
            var child = new GameObject(sourceChild, scene) {
                _parent = this
            };
            base.Children.Add(child);
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
            Components.Add(new Component(this, comp));
        }

        foreach (var sourceChild in source.Children) {
            var child = new GameObject(sourceChild, folder, prefabs, scene) {
                _parent = this
            };
            base.Children.Add(child);
        }
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
            if (Parent != null && Parent.Scene != newScene) {
                Parent.RemoveChild(this);
            }
        }
        Scene = newScene;
        foreach (var child in Children) {
            child.MoveToScene(newScene);
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
        var newObj = new GameObject(instance.Clone()) {
            Scene = parent?.Scene ?? Scene,
            _parent = parent,
        };
        foreach (var comp in Components) {
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

public class Component(GameObject gameObject, RszInstance data)
{
    public RszInstance Data { get; } = data;
    public GameObject GameObject { get; } = gameObject;

    public string Classname => Data.RszClass.name;
    public override string ToString() => Data.ToString();

    public virtual void CloneTo(GameObject target)
    {
        target.Components.Add(new Component(target, Data.Clone()));
    }
}
