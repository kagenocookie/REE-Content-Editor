using System.Numerics;
using System.Runtime.CompilerServices;
using ContentEditor.Core;
using ReeLib;
using ReeLib.Pfb;

namespace ContentEditor.App;

public class NotifiableObject
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public delegate void PropertyChangedEventHandler(object? sender, string propertyName);

    protected void NotifyPropertyChanged([CallerMemberName] string propertyName = "") => PropertyChanged?.Invoke(this, propertyName);
}

public class NodeObject : NotifiableObject {}

public class NodeObject<TNode> : NodeObject where TNode : NodeObject<TNode>
{
    public event NodeObjectEventHandler? ParentChanged;
    public event NodeObjectEventHandler? ChildAdded;
    public event NodeObjectEventHandler? ChildRemoved;

    public string Name { get; set; } = string.Empty;

    public delegate void NodeObjectEventHandler(TNode? sender, TNode? related);

    internal LinkedList<TNode> Children { get; } = new();

    protected TNode? _parent;
    public TNode? Parent
    {
        get => _parent;
        set {
            if (value != _parent) {
                if (_parent != null) {
                    _parent.Children.Remove(Unsafe.As<TNode>(this));
                    _parent.ChildRemoved?.Invoke(_parent, Unsafe.As<TNode>(this));
                }
                _parent = value;
                _parent?.ChildAdded?.Invoke(_parent, Unsafe.As<TNode>(this));
                ParentChanged?.Invoke(Unsafe.As<TNode>(this), value);
            }
        }
    }
}

public sealed class GameObject : NodeObject<GameObject>, IDisposable
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

    public GameObject(string name, Workspace workspace, GameObject? parent = null)
    {
        Name = name;
        Update = true;
        Draw = true;
        TimeScale = -1;
        guid = Guid.NewGuid();
        Parent = parent;
        Components.Add(new Transform(workspace));
    }

    public GameObject(PfbGameObject source, GameObject? parent = null)
    {
        var data = source.Instance!;
        Name = (string)data.Values[0];
        Tags = (string)data.Values[1];
        Draw = (bool)data.Values[2];
        Update = (bool)data.Values[3];
        TimeScale = data.Values.Length > 4 ? (float)data.Values[4] : -1;
        Parent = parent;

        foreach (var comp in source.Components) {
            Components.Add(new Component(comp));
        }

        foreach (var child in source.Children) {
            base.Children.AddLast(new GameObject(child, this));
        }
    }

    public void Dispose()
    {
        foreach (var comp in Components) {
            (comp as IDisposable)?.Dispose();
        }
    }
}

public class Component(RszInstance data)
{
    public RszInstance Data { get; } = data;

    public string Classname => Data.RszClass.name;
}

public class Folder : NodeObject<Folder>
{
    public readonly List<GameObject> GameObjects = new();
}

public class Scene
{
    public readonly List<Folder> Folders = new();
}
