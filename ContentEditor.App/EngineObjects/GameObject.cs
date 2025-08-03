using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using ContentEditor.Core;
using ReeLib;
using ReeLib.Common;
using ReeLib.Pfb;
using ReeLib.UVar;

namespace ContentEditor.App;

public class NotifiableObject
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public delegate void PropertyChangedEventHandler(object? sender, string propertyName);

    protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, propertyName);
}

public sealed class GameObject : NodeObject<GameObject>, IDisposable, IGameObject
{
    public string Tags = string.Empty;
    public bool Update;
    public bool Draw;
    public float TimeScale;
    public Guid guid;
    public string? PrefabPath;
    public readonly List<Component> Components = new();
    public Folder? folder;
    public new IEnumerable<GameObject> Children => base.Children;

    private RszInstance instance;

    string? IGameObject.Name => Name;
    RszInstance? IGameObject.Instance => instance;
    IList<RszInstance> IGameObject.Components => Components.Select(c => c.Data).ToList();
    IEnumerable<IGameObject> IGameObject.GetChildren() => Children;

    private GameObject(RszInstance instance)
    {
        this.instance = instance;
        ImportInstanceFields();
    }

    public GameObject(string name, Workspace workspace, Scene? scene = null)
    {
        instance = RszInstance.CreateInstance(workspace.RszParser, workspace.Classes.GameObject);
        Name = name;
        Update = true;
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

    public void Dispose()
    {
        foreach (var child in Children) {
            child.Dispose();
        }

        foreach (var comp in Components) {
            (comp as IDisposable)?.Dispose();
        }
    }

    internal void MoveToScene(Scene newScene)
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

    public GameObject Clone(GameObject? parent = null)
    {
        ExportInstanceFields();
        var newObj = new GameObject(instance.Clone()) {
            Scene = parent?.Scene ?? Scene,
        };
        foreach (var comp in Components) {
            comp.CloneTo(newObj);
        }
        foreach (var child in Children) {
            child.Clone(this);
        }
        parent?.AddChild(newObj);

        return newObj;
    }

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
