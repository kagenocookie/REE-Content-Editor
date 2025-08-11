using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor.App;
using ContentEditor.Core;
using ReeLib;
using ReeLib.Pfb;

namespace ContentPatcher;

public class PrefabLoader : IFileLoader,
    IFileHandleContentProvider<PfbFile>,
    IFileHandleContentProvider<GameObject>,
    IFileHandleContentProvider<Prefab>,
    IFileHandleContentProvider<Scene>
{
    int IFileLoader.Priority => 30;
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.Prefab;

    public IResourceFilePatcher? CreateDiffHandler() => new PrefabPatcher();

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new PfbFile(workspace.Env.RszFileOption, fileHandler);
        if (!file.Read()) return null;

        file.SetupGameObjects();
        return new Prefab(handle, file);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var prefab = handle.GetResource<Prefab>();
        prefab.WriteTo(outputPath);
        return true;
    }

    public PfbFile GetFile(FileHandle handle) => handle.GetResource<Prefab>().File;

    GameObject IFileHandleContentProvider<GameObject>.GetFile(FileHandle handle)
    {
        var prefab = handle.GetResource<Prefab>();
        return prefab.GetSharedInstance();
    }

    Prefab IFileHandleContentProvider<Prefab>.GetFile(FileHandle handle)
    {
        return handle.GetResource<Prefab>();
    }

    Scene IFileHandleContentProvider<Scene>.GetFile(FileHandle handle)
    {
        var prefab = handle.GetResource<Prefab>();
        return prefab.GetSharedInstance().Scene!;
    }
}

public class Prefab(FileHandle handle, PfbFile file) : IResourceFile
{
    private GameObject? _instance;
    public PfbFile File => file;

    public PfbFile GetExportedFile()
    {
        ExportGameObject();
        return file;
    }

    public GameObject GetSharedInstance()
    {
        return _instance ??= Instantiate();
    }

    public GameObject Instantiate(Scene? scene = null)
    {
        return new GameObject(file.GameObjects![0], scene);
    }

    public void WriteTo(string filepath)
    {
        var file = GetExportedFile();
        file.RebuildInfoTables();
        file.SaveOrWriteTo(handle, filepath);
    }
    private void ExportGameObject()
    {
        if (_instance == null) return;

        file.ClearGameObjects();
        file.AddGameObject(_instance.ToPfbGameObject());
        file.RSZ.RebuildInstanceList();
    }
}


public sealed class PrefabPatcher : RszFilePatcherBase, IDisposable
{
    private PfbFile file = null!;
    private FileHandle fileHandle = null!;

    public override IResourceFile LoadBase(ContentWorkspace workspace, FileHandle handle)
    {
        fileHandle = handle;
        file = handle.GetFile<PfbFile>();
        this.workspace = workspace;
        return handle.Resource;
    }

    public override JsonNode? FindDiff(FileHandle handle)
    {
        var newfile = handle.GetFile<PfbFile>();

        var diff = new JsonObject();
        // note, we aren't handling missing/deleted/moved/renamed game objects
        // instead, every path is considered a unique object, therefore rename == delete + create
        // not necessarily perfect, but realistically, there's no reason to rename game objects for modding purposes
        foreach (var (go, path) in IterateGameObjects(newfile.GameObjects[0])) {
            var targetGo = FindGameObjectByPath<PfbGameObject>(file.GameObjects[0], path);
            var godiff = GetGameObjectDiff(targetGo, go);
            if (godiff != null) {
                diff[path] = godiff;
            }
        }

        return diff;
    }

    public override void ApplyDiff(JsonNode diff)
    {
        if (diff is not JsonObject obj) return;

        var unapplied = obj.Where(kv => kv.Value is JsonObject).Select(kv => kv.Key).ToHashSet();
        var maxAttempts = unapplied.Count;
        // repeat the loop to ensure path order doesn't matter for newly added objects
        // e.g. if there's root/new/new_2/new_3 path, we always add them in the right parent order
        while (unapplied.Count > 0 && maxAttempts-- >= 0) {
            foreach (var path in unapplied) {
                var target = FindGameObjectByPath(file.GameObjects[0], path);
                if (target != null) {
                    unapplied.Remove(path);
                    ApplyGameObjectDiff(target, (JsonObject)diff[path]!);
                    break;
                } else {
                    var lastSlash = path.LastIndexOf('/');
                    if (lastSlash == -1) continue;
                    var parentPath = path.AsSpan().Slice(0, lastSlash);
                    var targetParent = FindGameObjectByPath(file.GameObjects[0], parentPath);
                    if (targetParent != null) {
                        unapplied.Remove(path);
                        var newChild = JsonSerializer.Deserialize<PfbGameObject>((JsonObject)diff[path]!, workspace.Env.JsonOptions);
                        if (newChild == null) {
                            throw new Exception("Failed to apply diff for new game object: " + path);
                        }
                        targetParent.Children.Add(newChild);
                        break;
                    }
                }
            }
        }
        if (maxAttempts < 0) {
            throw new Exception("Invalid state - pfb diff failed to fully resolve");
        }
    }

    public void Dispose()
    {
        file?.Dispose();
    }
}
