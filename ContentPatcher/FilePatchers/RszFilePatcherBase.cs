namespace ContentPatcher;

using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor;
using ReeLib;
using ReeLib.Scn;

public abstract class RszFilePatcherBase : IResourceFilePatcher
{
    protected ContentWorkspace workspace = null!;

    public abstract IResourceFile LoadBase(ContentWorkspace workspace, FileHandle file);

    public abstract JsonNode? FindDiff(FileHandle file);
    public abstract void ApplyDiff(JsonNode diff);
    public abstract void ApplyDiff(FileHandle targetFile, JsonNode diff);

    protected JsonNode? GetRszInstanceDiff(RszInstance target, RszInstance source)
    {
        // if this got called, it means both values are either an object or a struct, not basic types
        var targetJson = target.ToJson(workspace.Env);
        var sourceJson = source.ToJson(workspace.Env);
        return workspace.Diff.GetHierarchicalDataDiff(targetJson, sourceJson);
    }

    protected JsonNode? GetGameObjectDiff(IGameObject? target, IGameObject? source)
    {
        if (target?.Instance == null) {
            if (source?.Instance == null) return null;
            return JsonSerializer.SerializeToNode(source, source.GetType(), workspace.Env.JsonOptions);
        } else if (source?.Instance == null) {
            // TODO delete?
            return null;
        }

        var diff = new JsonObject();
        if (target is ScnGameObject targetScn && source is ScnGameObject sourceScn) {
            if (targetScn.Guid != sourceScn.Guid) {
                diff["_guid"] = sourceScn.Guid.ToString();
            }

            if (targetScn.Prefab?.Path != sourceScn.Prefab?.Path) {
                diff["_prefab"] = sourceScn.Prefab?.Path?.ToString();
            }
        }

        var dataDiff = workspace.Diff.GetHierarchicalDataDiff(target.Instance.ToJson(workspace.Env), source.Instance.ToJson(workspace.Env));
        var targetComps = new JsonObject(target.Components.ToDictionary(comp => comp.RszClass.name, comp => JsonSerializer.SerializeToNode(comp, workspace.Env.JsonOptions)!)!);
        var sourceComps = new JsonObject(source.Components.ToDictionary(comp => comp.RszClass.name, comp => JsonSerializer.SerializeToNode(comp, workspace.Env.JsonOptions)!)!);
        var compDiff = (JsonObject?)workspace.Diff.Maker.GetMinimalDiff(targetComps, sourceComps);
        if (dataDiff == null && compDiff == null) {
            return null;
        }

        if (dataDiff != null) {
            diff["_data"] = dataDiff;
        }
        if (compDiff != null) {
            foreach (var (k, v) in compDiff) {
                diff[k] = v?.DeepClone();
            }
        }
        if (diff.Count == 0) return null;
        return diff;
    }

    protected static IEnumerable<(TGO target, string path)> IterateGameObjects<TGO>(TGO root, string? path = null) where TGO : IGameObject
    {
        path ??= root.Name!;
        var next = root;
        yield return (next, path);
        var dupes = new Dictionary<string, int>();
        foreach (var child in root.GetChildren().Cast<TGO>()) {
            if (dupes.TryGetValue(child.Name!, out var counter)) {
                dupes[child.Name!] = ++counter;
            } else {
                dupes[child.Name!] = counter = 1;
            }
            var subpath = counter == 1 ? $"{path}/{child.Name}" : $"{path}/{child.Name}#{counter}";
            foreach (var (subchild, subpath1) in IterateGameObjects(child, subpath)) {
                yield return (subchild, subpath1);
            }
        }
    }

    private static readonly char[] PathSeparators = ['/', '#'];
    protected static TGO? FindGameObjectByPath<TGO>(TGO root, ReadOnlySpan<char> path, IEnumerable<TGO>? rootChildren = null) where TGO : class, IGameObject
    {
        static (int sep, int slash, int counter) GetNextSegment(ReadOnlySpan<char> path)
        {
            var sep = path.IndexOfAny(PathSeparators);
            if (sep == -1) {
                return (-1, -1, 1);
            }
            var slash = sep;
            var counter = 1;
            if (path[sep] == '#') {
                slash = path.IndexOf('/');
                if (slash == -1) {
                    counter = int.Parse(path[(sep + 1)..]);
                } else {
                    counter = int.Parse(path[(sep + 1)..slash]);
                }
            }
            return (sep, slash, counter);
        }

        var (sep, slash, counter) = GetNextSegment(path);

        if (sep == -1) {
            if (path.Length == 0 || path.SequenceEqual(root.Name)) return root;
            return null;
        }

        var name = path.Slice(0, sep);
        // every diff path must include the root objects's name
        if (!name.SequenceEqual(root.Name) || counter != 1) return null;

        var next = root;
        path = path.Slice(slash + 1);
        do {
            (sep, slash, counter) = GetNextSegment(path);
            name = sep == -1 ? path : path.Slice(0, sep);
            var found = false;
            foreach (var child in (next == root && rootChildren != null ? rootChildren : next.GetChildren())) {
                if (!name.SequenceEqual(child.Name)) continue;

                if (--counter == 0) {
                    if (slash == -1) {
                        return (TGO)child;
                    }

                    next = (TGO)child;
                    found = true;
                    path = path.Slice(slash + 1);
                    break;
                }
            }
            if (!found) break;
        } while (true);

        return null;
    }

    protected void ApplyObjectDiff(RszInstance target, JsonNode diff)
    {
        workspace.Diff.ApplyDiff(target, diff);
    }

    protected void ApplyGameObjectDiff<TGO>(TGO target, JsonObject diff) where TGO : class, IGameObject
    {
        foreach (var (key, prop) in diff) {
            if (prop == null) continue;

            if (key == "_guid" && target is ScnGameObject scn) {
                if (Guid.TryParse(prop.GetValue<string>(), out var guid)) {
                    scn.Guid = guid;
                }
            } else if (key == "_prefab" && target is ScnGameObject scn2) {
                scn2.Prefab ??= new();
                scn2.Prefab.Path = prop.GetValue<string>();
            } else if (key == "_data") {
                ApplyObjectDiff(target.Instance ?? throw new Exception("Missing game object data instance"), prop);
            } else {
                var comp = target.Components.FirstOrDefault(comp => comp.RszClass.name == key);
                if (comp == null) {
                    target.Components.Add(JsonSerializer.Deserialize<RszInstance>(prop, workspace.Env.JsonOptions) ?? throw new Exception("Invalid component " + key));
                } else {
                    ApplyObjectDiff(comp, prop);
                }
            }
        }
    }

    protected JsonObject? GetEmbeddedDiffs(RSZFile target, RSZFile source)
    {
        if (source.EmbeddedRSZFileList == null) return null;
        if (source.EmbeddedRSZFileList.Count == 0 && (target.EmbeddedRSZFileList == null || target.EmbeddedRSZFileList.Count == 0)) return null;
        var list = new JsonObject();
        foreach (var sourceEmbed in source.EmbeddedRSZFileList) {
            var path = sourceEmbed.ObjectList[0].Values[0] as string;
            if (string.IsNullOrEmpty(path)) {
                Logger.Warn("Found embedded userdata without file path");
                continue;
            }

            var targetEmbed = target.EmbeddedRSZFileList?.FirstOrDefault(e => e.ObjectList[0].Values[0] as string == path);
            if (targetEmbed == null) {
                list.Add(path, sourceEmbed.ObjectList[0].ToJson(workspace.Env));
                continue;
            }

            var diff = GetRszInstanceDiff(targetEmbed.ObjectList[0], sourceEmbed.ObjectList[0]);
            if (diff != null) {
                list.Add(path, diff);
            }
        }

        if (list.Count == 0) return null;
        return list;
    }

    protected void ApplyEmbeddedDiffs(RSZFile target, JsonObject? diff)
    {
        if (diff == null || diff.Count == 0) return;

        target.EmbeddedRSZFileList ??= new();
        foreach (var (path, data) in diff) {
            if (data == null) {
                // delete?
                continue;
            }
            var userdata = target.EmbeddedRSZFileList.FirstOrDefault(e => e.ObjectList[0].Values[0] as string == path);
            if (userdata == null) {
                var instance = data.Deserialize<RszInstance>(workspace.Env.JsonOptions);
                if (instance == null) {
                    Logger.Error("Failed to deserialize embedded userdata instance " + path);
                    continue;
                }

                userdata = new RSZFile(target.Option, target.FileHandler);
                userdata.AddToObjectTable(instance);
                continue;
            }

            ApplyObjectDiff(userdata.ObjectList[0], data);
        }
    }
}
