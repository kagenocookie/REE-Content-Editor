using ContentPatcher;
using ReeLib;
using ReeLib.Scn;
using Silk.NET.Maths;

namespace ContentEditor.App;

public sealed class Folder : NodeObject<Folder>, IDisposable, INodeObject<Folder>, INodeObject<GameObject>
{
    public readonly List<GameObject> GameObjects = new();

    public string Tags = string.Empty;
    public bool Update = true;
    public bool Draw = true;
    public bool Standby = true;
    public string? ScenePath;
    private ReeLib.via.Position _offset;
    public ReeLib.via.Position Offset
    {
        get => _offset;
        set {
            if (value != _offset) {
                foreach (var go in GameObjects) go.Transform.InvalidateTransform();
                _offset = value;
            }
        }
    }

    public Vector3D<float> OffsetSilk => new Vector3D<float>((float)_offset.x, (float)_offset.y, (float)_offset.z);

    private RszInstance instance;

    public SceneFlags SceneFlags { get; set; } = SceneFlags.All;

    public bool ShouldDraw => (SceneFlags & SceneFlags.Draw) != 0 && Parent?.ShouldDraw != false;
    public bool ShouldDrawSelf
    {
        get => (SceneFlags & SceneFlags.Draw) != 0;
        set => SceneFlags = (value ? SceneFlags|SceneFlags.Draw : SceneFlags&~SceneFlags.Draw);
    }

    IEnumerable<GameObject> INodeObject<GameObject>.Children => GameObjects;

    private Folder(RszInstance instance)
    {
        this.instance = instance;
        ImportInstanceFields();
    }

    public Folder(string name, Workspace workspace, Scene? scene = null)
    {
        instance = RszInstance.CreateInstance(workspace.RszParser, workspace.Classes.Folder);
        Name = name;
        Scene = scene;
        ExportInstanceFields();
    }

    public Folder(string name,
        Workspace workspace,
        IEnumerable<ScnFolderData> folders,
        IEnumerable<ScnGameObject> gameObjects,
        IList<ScnPrefabInfo> prefabs,
        Scene? scene = null)
    {
        instance = RszInstance.CreateInstance(workspace.RszParser, workspace.Classes.Folder);
        Name = name;
        Scene = scene;
        ExportInstanceFields();

        foreach (var sub in folders) {
            var folder = new Folder(sub, workspace, prefabs, scene) {
                _parent = this,
            };
            base.Children.Add(folder);
        }

        foreach (var gobj in gameObjects) {
            var obj = new GameObject(gobj, this, prefabs, scene);
            GameObjects.Add(obj);
        }
    }

    public Folder(ScnFolderData source, Workspace workspace, IList<ScnPrefabInfo> prefabs, Scene? scene = null)
    {
        instance = source.Instance!;
        ImportInstanceFields();
        Scene = scene;

        foreach (var sub in source.Children) {
            var child = new Folder(sub, workspace, prefabs, scene) {
                _parent = this,
            };
            base.Children.Add(child);
        }

        foreach (var gobj in source.GameObjects) {
            var obj = new GameObject(gobj, this, null, scene);
            GameObjects.Add(obj);
        }
    }

    public IEnumerable<GameObject> GetAllGameObjects()
    {
        foreach (var folder in Children) {
            foreach (var sub in folder.GetAllGameObjects()) {
                yield return sub;
            }
        }
        foreach (var child in GameObjects) {
            yield return child;
            foreach (var sub in child.GetAllChildren()) {
                yield return sub;
            }
        }
    }

    public GameObject? Find(ReadOnlySpan<char> path)
    {
        var part = path.IndexOf('/');
        if (part == -1) {
            return FindGameObjectByName(path);
        }
        var child = FindGameObjectByName(path.Slice(0, part));
        if (child == null) {
            var folder = GetChild(path);
            return folder?.Find(path.Slice(part + 1));
        }

        return child?.Find(path.Slice(part + 1));
    }

    public void SetActive(bool active)
    {
        foreach (var subfolder in Children) {
            subfolder.SetActive(active);
        }

        foreach (var go in GameObjects) {
            go.SetActive(active);
        }
    }

    private GameObject? FindGameObjectByName(ReadOnlySpan<char> name)
    {
        foreach (var go in GameObjects) {
            if (name.SequenceEqual(go.Name)) {
                return go;
            }
        }
        return null;
    }

    private void ImportInstanceFields()
    {
        Name = RszFieldCache.Folder.Name.Get(instance);
        Tags = RszFieldCache.Folder.Tags.Get(instance);
        Draw = RszFieldCache.Folder.Draw.Get(instance);
        Update = RszFieldCache.Folder.Update.Get(instance);
        Standby = RszFieldCache.Folder.Standby.Get(instance);
        ScenePath = RszFieldCache.Folder.ScenePath.Get(instance);
        _offset = RszFieldCache.Folder.UniversalOffset.Get(instance);
    }

    private void ExportInstanceFields()
    {
        RszFieldCache.Folder.Name.Set(instance, Name);
        RszFieldCache.Folder.Tags.Set(instance, Tags);
        RszFieldCache.Folder.Draw.Set(instance, Draw);
        RszFieldCache.Folder.Update.Set(instance, Update);
        RszFieldCache.Folder.Standby.Set(instance, Standby);
        RszFieldCache.Folder.ScenePath.Set(instance, ScenePath ?? string.Empty);
        RszFieldCache.Folder.UniversalOffset.Set(instance, _offset);
    }

    public ScnFolderData ToScnFolder(List<ScnPrefabInfo>? prefabInfos)
    {
        ExportInstanceFields();

        var scn = new ScnFolderData() {
            Instance = instance,
        };
        if (!string.IsNullOrEmpty(ScenePath)) {
            return scn;
        }
        foreach (var child in Children) {
            scn.Children.Add(child.ToScnFolder(prefabInfos));
        }
        foreach (var child in GameObjects) {
            var go = child.ToScnGameObject(prefabInfos);
            scn.GameObjects.Add(go);
            go.Folder = scn;
        }
        return scn;
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
        foreach (var obj in GameObjects) {
            obj.MoveToScene(newScene);
        }
    }

    public Folder Clone(Folder? parent = null)
    {
        ExportInstanceFields();
        var newObj = new Folder(instance.Clone()) {
            Scene = parent?.Scene ?? Scene,
        };
        foreach (var child in Children) {
            child.Clone(newObj);
        }
        foreach (var child in GameObjects) {
            var clone = child.Clone();
            clone.MoveToScene(Scene);
            GameObjects.Add(clone);
        }
        parent?.AddChild(newObj);

        return newObj;
    }

    public void Dispose()
    {
        foreach (var folder in Children) {
            folder.Dispose();
        }

        foreach (var go in GameObjects) {
            go.Dispose();
        }
    }

    public override string ToString() => Name;

    public void AddGameObject(GameObject gameObject, int index = -1)
    {
        if (index < 0 || index >= GameObjects.Count) {
            GameObjects.Add(gameObject);
        } else {
            GameObjects.Insert(index, gameObject);
        }
        gameObject.MoveToFolder(this);
    }

    void INodeObject<GameObject>.AddChild(GameObject child, int index) => AddGameObject(child, index);

    void INodeObject<GameObject>.RemoveChild(GameObject child)
    {
        if (Logger.ErrorIf(!GameObjects.Contains(child), "Removed GameObject must be a child of Folder")) return;

        child.MoveToFolder(null);
    }

    int INodeObject<GameObject>.GetChildIndex(GameObject child) => GameObjects.IndexOf(child);
    INodeObject<GameObject>? INodeObject<GameObject>.GetParent() => null;

    IEnumerable<GameObject> INodeObject<GameObject>.GetAllChildren() => GameObjects.SelectMany(go => go.GetAllChildren()).Concat(GetAllChildren().SelectMany(f => f.GameObjects));
}
