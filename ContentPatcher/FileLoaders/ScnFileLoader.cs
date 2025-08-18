using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor;
using ReeLib;
using ReeLib.Scn;

namespace ContentPatcher;

public class ScnFileLoader : IFileLoader, IFileHandleContentProvider<ScnFile>
{
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.Scene;

    public ScnFile GetFile(FileHandle handle) => handle.GetFile<ScnFile>();

    public IResourceFilePatcher? CreateDiffHandler() => new ScenePatcher();

    public IResourceFile Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new ScnFile(workspace.Env.RszFileOption, fileHandler);
        file.Read();
        file.SetupGameObjects();
        return new BaseFileResource<ScnFile>(file);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetFile<ScnFile>();
        file.RebuildInfoTable();
        return file.SaveOrWriteTo(handle, outputPath);
    }
}

public sealed class ScenePatcher : RszFilePatcherBase, IDisposable
{
    private ScnFile file = null!;
    private FileHandle fileHandle = null!;

    public override IResourceFile LoadBase(ContentWorkspace workspace, FileHandle handle)
    {
        fileHandle = handle;
        file = handle.GetFile<ScnFile>();
        this.workspace = workspace;
        return handle.Resource;
    }

    public override JsonNode? FindDiff(FileHandle handle)
    {
        var newfile = handle.GetFile<ScnFile>();
        if (handle == fileHandle) {
            if (handle.HandleType == FileHandleType.Bundle) {
                // this can happen if a diff is attempted for a custom file - no need to log an error here
                return null;
            }
            Logger.Error("Attempted diff between the exact same file: " + handle.Filepath);
            return null;
        }

        var diff = new JsonObject();
        // note, we aren't handling moved/renamed game objects
        // instead, every path is considered a unique object, therefore rename == delete + create
        // not necessarily perfect, but realistically, there's no reason to rename game objects for modding purposes
        foreach (var (folder, folderPath) in IterateFolders(newfile.FolderDatas!)) {
            var targetFolder = FindFolderByPath(file.FolderDatas!, folderPath);

            if (targetFolder?.Instance == null) {
                if (folder == null) continue;

                diff[folderPath] = JsonSerializer.SerializeToNode(folder.Instance, workspace.Env.JsonOptions);
                HandleGameObjectDiffs(diff, folder.GameObjects, null, null, folderPath, file);
                continue;
            }

            if (folder?.Instance == null) {
                // TODO mark remove
                continue;
            }

            var folderInfoDiff = GetRszInstanceDiff(targetFolder.Instance, folder.Instance);
            if (folderInfoDiff != null) {
                diff[folderPath] = folderInfoDiff;
            }

            HandleGameObjectDiffs(diff, folder.GameObjects, targetFolder.GameObjects, targetFolder, folderPath, file);
        }
        if (newfile.GameObjects?.Count > 0) {
            HandleGameObjectDiffs(diff, newfile.GameObjects, file.GameObjects, null, "", file);
        }

        var embedDiff = GetEmbeddedDiffs(file.RSZ, newfile.RSZ);
        if (embedDiff != null) {
            diff ??= new JsonObject();
            diff["__embeds"] = embedDiff;
        }

        if (diff.Count == 0) return null;
        return diff;
    }

    private void HandleGameObjectDiffs(JsonObject diff, IEnumerable<ScnGameObject> sourceObjects, IEnumerable<ScnGameObject>? targetObjects, ScnFolderData? targetFolder, string folderPath, ScnFile targetFile)
    {
        if (targetObjects == null) {
            foreach (var (go, path) in IterateGameObjects(sourceObjects)) {
                var goPath = folderPath + "//" + path;
                diff[goPath] = JsonSerializer.SerializeToNode(go, workspace.Env.JsonOptions);
            }
            return;
        }

        foreach (var (go, path) in IterateGameObjects(sourceObjects)) {
            var target = FindGameObjectByPath(targetObjects, path);
            var goDiff = GetGameObjectDiff(target, go);
            if (goDiff != null) {
                var goPath = folderPath + "//" + path;
                diff[goPath] = goDiff;
            }
        }
    }

    private static ScnGameObject? FindGameObjectByPath(IEnumerable<ScnGameObject> rootChildren, ReadOnlySpan<char> path)
    {
        while (path[0] == '/') path = path.Slice(1);
        foreach (var ch in rootChildren) {
            var match = FindGameObjectByPath(ch, path);
            if (match != null) return match;
        }
        return null;
    }

    private static IEnumerable<(ScnGameObject target, string path)> IterateGameObjects(IEnumerable<ScnGameObject> rootChildren)
    {
        foreach (var ch in rootChildren) {
            foreach (var (go, path) in IterateGameObjects(ch)) {
                yield return (go, path);
            }
        }
    }

    private static IEnumerable<(ScnFolderData target, string path)> IterateFolders(IEnumerable<ScnFolderData> folders, string? path = null)
    {
        foreach (var child in folders) {
            var subpath = path == null ? child.Name! : $"{path}/{child.Name}";
            yield return (child, subpath);
            if (!string.IsNullOrEmpty(child.Path)) continue;

            foreach (var (subchild, subpath1) in IterateFolders(child.Children, subpath)) {
                yield return (child, subpath1);
            }
        }
    }

    private static ScnFolderData? FindFolderByPath(IEnumerable<ScnFolderData> folders, ReadOnlySpan<char> path)
    {
        var sep = path.IndexOf('/');
        if (sep == -1) {
            foreach (var folder in folders) {
                if (path.SequenceEqual(folder.Name)) {
                    return folder;
                }
            }
            return null;
        }

        var name = path.Slice(0, sep);
        foreach (var folder in folders) {
            if (path.SequenceEqual(folder.Name)) {
                return FindFolderByPath(folder.Children, path.Slice(sep + 1));
            }
        }

        return null;
    }

    public override void ApplyDiff(JsonNode diff)
    {
        if (diff is not JsonObject obj) return;

        var unapplied = obj.Where(kv => kv.Value is JsonObject && kv.Key != "__embeds").Select(kv => kv.Key).ToHashSet();
        var maxAttempts = unapplied.Count;
        // repeat the loop to ensure key iteration order doesn't matter for newly added objects
        // e.g. if there's a root/new/new_2/new_3 path, we always add them in the right parent order
        while (unapplied.Count > 0 && maxAttempts-- >= 0) {
            foreach (var path in unapplied) {
                var pathSpan = path.AsSpan();
                var foldersep = path.IndexOf("//");
                ScnFolderData? folder;
                if (foldersep == -1) {
                    // folder info instance
                    var lastSlash = path.LastIndexOf('/');
                    if (lastSlash == -1) {
                        // new root folder
                        folder = new ScnFolderData() { Instance = obj[path]!.Deserialize<RszInstance>(workspace.Env.JsonOptions)! };
                        file.FolderDatas!.Add(folder);
                        unapplied.Remove(path);
                        break;
                    }
                    var parentPath = pathSpan.Slice(0, lastSlash);
                    var parent = FindFolderByPath(file.FolderDatas!, parentPath);
                    if (parent == null) continue;

                    folder = new ScnFolderData() { Instance = obj[path]!.Deserialize<RszInstance>(workspace.Env.JsonOptions)! };
                    parent!.Children.Add(folder);
                    unapplied.Remove(path);
                    break;
                } else if (foldersep > 0) {
                    // gameobject inside folder
                    folder = FindFolderByPath(file.FolderDatas!, pathSpan.Slice(0, foldersep));
                    if (folder == null) continue;
                    pathSpan = pathSpan.Slice(foldersep + 2);
                } else {
                    // root gameobject
                    folder = null;
                }
                var gameObjects = folder?.GameObjects ?? file.GameObjects!;

                var target = FindGameObjectByPath(gameObjects, pathSpan);
                if (target != null) {
                    unapplied.Remove(path);
                    ApplyGameObjectDiff(target, (JsonObject)obj[path]!);
                    break;
                } else {
                    var lastSlash = pathSpan.LastIndexOf('/');
                    if (lastSlash == -1) {
                        // add to root
                        unapplied.Remove(path);
                        var newChild = JsonSerializer.Deserialize<ScnGameObject>((JsonObject)obj[path]!, workspace.Env.JsonOptions);
                        if (newChild == null) {
                            throw new Exception("Failed to apply diff for new game object: " + path);
                        }
                        gameObjects.Add(newChild!);
                        break;
                    }
                    var parentPath = pathSpan.Slice(0, lastSlash);
                    var targetParent = FindGameObjectByPath(gameObjects, pathSpan.Slice(0, lastSlash));
                    if (targetParent != null) {
                        unapplied.Remove(path);
                        var newChild = JsonSerializer.Deserialize<ScnGameObject>((JsonObject)obj[path]!, workspace.Env.JsonOptions);
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
            throw new Exception("Invalid patch diff - could not resolve objects\n" + string.Join("\n", unapplied));
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
