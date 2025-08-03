using System.Runtime.CompilerServices;

namespace ContentEditor.App;

public class NodeObject : NotifiableObject { }

public class NodeObject<TNode> : NodeObject
    where TNode : NodeObject<TNode>
{
    public event NodeObjectEventHandler? ParentChanged;
    public event NodeObjectEventHandler? ChildAdded;
    public event NodeObjectEventHandler? ChildRemoved;

    public Scene? Scene { get; protected set; }

    public string Name { get; set; } = string.Empty;

    public bool IsInTree => Scene?.Find(Name) != null || Parent?.IsInTree == true;

    private string? _cachedPath;
    public string Path {
        get {
            if (_cachedPath != null) return _cachedPath;
            if (Parent != null) {
                _cachedPath = Parent.Path + "/" + _cachedPath;
            } else {
                _cachedPath = Name;
            }
            return _cachedPath;
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
        if (Parent == null) return;

        var basename = Name;
        int index = 0;
        var newName = basename;
        while (Parent.Children.Any(c => c.Name == newName && c != this)) {
            newName = $"{basename}_copy" + (index == 0 ? "" : index.ToString());
            index++;
        }
        Name = newName;
    }

    protected virtual void DeferAction(Action action) { }
    protected virtual void OnParentChanged() { }
}
