using System.Runtime.CompilerServices;

namespace ContentEditor.App;

public class NodeObject : NotifiableObject
{
    public string Name { get; set; } = string.Empty;
}

public interface IPathedObject
{
    string Path { get; }
}

public interface INodeObject
{
    public string Name { get; set; }
    IEnumerable<NodeObject> Children { get; }
    NodeObject? Parent { get; }
    Scene? Scene { get; }
}

public interface INodeObject<T> : INodeObject
{
    new IEnumerable<T> Children { get; }

    void AddChild(T child, int index);
    void AddChild(T child) => AddChild(child, -1);
    void RemoveChild(T child);
    INodeObject<T>? GetParent();
    int GetChildIndex(T child);

    /// <summary>
    /// Get all children within the hierarchy, including children's children.
    /// </summary>
    IEnumerable<T> GetAllChildren();

    public bool IsParentOf(INodeObject<T> child)
    {
        var parent = child.GetParent();
        while (parent != null) {
            if (ReferenceEquals(parent, this)) return true;
            parent = parent.GetParent();
        }
        return false;
    }
}

public class NodeObject<TNode> : NodeObject, INodeObject<TNode>, IPathedObject
    where TNode : NodeObject<TNode>
{
    public event NodeObjectEventHandler? ParentChanged;
    public event NodeObjectEventHandler? ChildAdded;
    public event NodeObjectEventHandler? ChildRemoved;

    public Scene? Scene { get; protected set; }

    public bool IsInTree => Scene?.Find(Name) != null || Parent?.IsInTree == true;

    protected string? _cachedPath;
    public string Path => _cachedPath ??= GetPath();

    IEnumerable<NodeObject> INodeObject.Children => Children;
    IEnumerable<TNode> INodeObject<TNode>.Children => Children;
    NodeObject? INodeObject.Parent => Parent;

    protected virtual string GetPath()
    {
        if (Parent != null) {
            return Parent.Path + "/" + Name;
        } else {
            return Name;
        }
    }

    internal List<TNode> Children { get; } = new();

    public delegate void NodeObjectEventHandler(TNode? sender, TNode? related);

    public TNode? GetChild(ReadOnlySpan<char> name)
    {
        foreach (var go in Children) {
            if (name.SequenceEqual(go.Name)) {
                return go;
            }
        }
        return null;
    }

    public IEnumerable<TNode> GetAllChildren()
    {
        foreach (var child in Children) {
            yield return child;
            foreach (var sub in child.GetAllChildren()) {
                yield return sub;
            }
        }
    }

    protected TNode? _parent;
    public TNode? Parent {
        get => _parent;
    }

    /// <summary>
    /// Updates the parent immediately. Not safe to use during updates.
    /// </summary>
    /// <param name="parent"></param>
    protected void SetParent(TNode? parent, bool alreadyAddedToChildren)
    {
        if (parent == _parent) return;
        if (_parent != null) {
            _parent.Children.Remove(Unsafe.As<TNode>(this));
            _parent.ChildRemoved?.Invoke(_parent, Unsafe.As<TNode>(this));
        }
        _parent = parent;
        if (parent != null) {
            Scene = parent.Scene;
            if (!alreadyAddedToChildren) {
                parent.Children.Add(Unsafe.As<TNode>(this));
            }
            parent.ChildAdded?.Invoke(parent, Unsafe.As<TNode>(this));
        }
        _cachedPath = null;
        OnParentChanged();
        ParentChanged?.Invoke(Unsafe.As<TNode>(this), parent);
    }

    protected void SetParentDeferred(TNode? parent, bool alreadyAddedToChildren)
    {
        if (parent != _parent) {
            if (Scene == null) {
                SetParent(parent, alreadyAddedToChildren);
            } else {
                Scene.DeferAction(() => SetParent(parent, alreadyAddedToChildren));
            }
        }
    }

    public void AddChild(TNode node)
    {
        node.SetParent(Unsafe.As<TNode>(this), false);
    }

    public void AddChildDeferred(TNode node)
    {
        if (Scene == null) {
            node.SetParent(Unsafe.As<TNode>(this), false);
        } else {
            Scene.DeferAction(() => node.SetParent(Unsafe.As<TNode>(this), false));
        }
    }

    public void AddChild(TNode node, int index)
    {
        if (index < 0 || index >= Children.Count) {
            AddChild(node);
            return;
        }

        Children.Insert(index, node);
        node.SetParent(Unsafe.As<TNode>(this), true);
    }

    public void RemoveChild(TNode child)
    {
        if (Children.Remove(child)) {
            child.SetParent(null, false);
        } else {
            Logger.Error($"Node {child} is not a child of {this}");
        }
    }

    public void RemoveChildDefer(TNode child)
    {
        DeferAction(() => RemoveChild(child));
    }

    public void AddChildDeferred(TNode node, int index)
    {
        if (index == -1) {
            AddChildDeferred(node);
            return;
        }

        if (index < 0 || index >= Children.Count) {
            Logger.Error($"Node.AddChild: Index {index} out of range");
            return;
        }

        if (Scene == null) {
            Children.Insert(index, node);
            node.SetParent(Unsafe.As<TNode>(this), true);
        } else {
            Scene.DeferAction(() => {
                Children.Insert(index, node);
                node.SetParent(Unsafe.As<TNode>(this), true);
            });
        }
    }

    public TNode? GetChildAtIndex(int index)
    {
        if (index < 0 || index >= Children.Count) return null;
        return Children[index];
    }

    public int GetChildIndex(TNode node)
    {
        if (Logger.ErrorIf(node.Parent != this, "Node is not a child")) return -1;
        return Children.IndexOf(node);
        // int i = 0;
        // foreach (var child in Children) {
        //     if (child == node) return i;
        //     i++;
        // }

        // throw new Exception("Node parents are broken, please report a bug");
    }

    public void MakeNameUnique()
    {
        var parent = ((INodeObject<TNode>)this).GetParent();
        if (parent == null) return;

        var basename = Name;
        int index = 1;
        var newName = basename;
        while (parent.Children.Any(c => c.Name == newName && c != this)) {
            newName = $"{basename}_" + index.ToString();
            index++;
        }
        Name = newName;
    }

    protected virtual void DeferAction(Action action) { }
    protected virtual void OnParentChanged() { }

    INodeObject<TNode>? INodeObject<TNode>.GetParent() => _parent;

    public bool IsParentOf<TNodeHolder>(TNodeHolder nodeHolder) where TNodeHolder : NodeObject<TNodeHolder>
    {
        var parent = nodeHolder.Parent;
        while (parent != null) {
            if (ReferenceEquals(parent, this)) return true;
            parent = parent.Parent;
        }
        return false;
    }

    /// <summary>
    /// Set this object's scene without "properly" adding it to the scene tree. Intended for editor-only GameObjects that we don't want saved with the scene.
    /// </summary>
    internal void ForceSetScene(Scene scene)
    {
        Scene = scene;
    }
}
