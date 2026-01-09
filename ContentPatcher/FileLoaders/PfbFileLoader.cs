using System.Text.Json;
using System.Text.Json.Nodes;
using ReeLib;
using ReeLib.Pfb;

namespace ContentPatcher;

public class PfbFileLoader : IFileLoader, IFileHandleContentProvider<PfbFile>
{
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.Prefab;

    public PfbFile GetFile(FileHandle handle) => handle.GetFile<PfbFile>();

    public IResourceFilePatcher? CreateDiffHandler() => new PrefabPatcher();

    public IResourceFile Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new PfbFile(workspace.Env.RszFileOption, fileHandler);
        file.Read();
        file.SetupGameObjects();
        return new BaseFileResource<PfbFile>(file);
    }

    public IResourceFile? CreateNewFile(ContentWorkspace workspace, FileHandle handle)
    {
        var file = new PfbFile(workspace.Env.RszFileOption, new FileHandler(handle.Stream, handle.Filepath));
        file.Write();
        return new BaseFileResource<PfbFile>(file);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetFile<PfbFile>();
        file.RebuildInfoTables();
        return file.SaveOrWriteTo(handle, outputPath);
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
        // note, we aren't handling moved/renamed game objects
        // instead, every path is considered a unique object, therefore rename == delete + create
        // not necessarily perfect, but realistically, there's no reason to rename game objects for modding purposes
        foreach (var (go, path) in IterateGameObjects(newfile.GameObjects[0])) {
            var targetGo = FindGameObjectByPath<PfbGameObject>(file.GameObjects[0], path);
            var godiff = GetGameObjectDiff(targetGo, go);
            if (godiff != null) {
                diff[path] = godiff;
            }
        }

        foreach (var (go, path) in IterateGameObjects(file.GameObjects[0])) {
            var newGo = FindGameObjectByPath<PfbGameObject>(newfile.GameObjects[0], path);
            if (newGo == null) {
                // mark deleted - it's gone from the new file
                diff[path] = null;
            }
        }

        var embedDiff = GetEmbeddedDiffs(file.RSZ, newfile.RSZ);
        if (embedDiff != null) {
            diff ??= new JsonObject();
            diff["__embeds"] = embedDiff;
        }

        if (diff.Count == 0) return null;
        return diff;
    }

    public override void ApplyDiff(JsonNode diff) => ApplyDiff(fileHandle, diff);
    public override void ApplyDiff(FileHandle targetFile, JsonNode diff)
    {
        if (diff is not JsonObject obj) return;
        var file = targetFile.GetFile<PfbFile>();

        var unapplied = obj.Where(kv => kv.Value is JsonObject && kv.Key != "__embeds").Select(kv => kv.Key).ToHashSet();
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

        if (obj.TryGetPropertyValue("__embeds", out var embedDiff) && embedDiff is JsonObject eDiffObj) {
            ApplyEmbeddedDiffs(file.RSZ, eDiffObj);
        }
    }

    public void Dispose()
    {
        file?.Dispose();
    }
}
