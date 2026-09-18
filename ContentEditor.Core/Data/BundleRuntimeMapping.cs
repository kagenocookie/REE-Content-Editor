namespace ContentEditor.Core;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

public class BundleRuntimeMapping
{
    private readonly Dictionary<string, RuntimeMappingEntry> editorEntries = new();
    private readonly Dictionary<string, RuntimeMappingEntry> runtimeEntries = new();
    private readonly Dictionary<string, string> runtimeToEditorName = new();
    private readonly Dictionary<string, string> editorToRuntimeName = new();

    private class RuntimeMappingEntry
    {
        public Dictionary<(string field, string path), string> editorToRuntime = new();
        public Dictionary<string, (string field, string path)> runtimeToEditor = new();
    }

    public void Add(
        string editorEntityType,
        string runtimeType,
        Dictionary<string, string> toRuntime,
        Dictionary<string, string> toDesktop,
        Dictionary<string, string> toBoth)
    {
        var entry = new RuntimeMappingEntry();
        editorEntries[editorEntityType] = entry;
        runtimeEntries[runtimeType] = entry;

        if (!runtimeToEditorName.TryAdd(runtimeType, editorEntityType)) {
            throw new Exception($"Duplicate runtime entity name {runtimeType}->{editorEntityType}, already maps to {runtimeToEditorName[runtimeType]}");
        }

        if (!editorToRuntimeName.TryAdd(editorEntityType, runtimeType)) {
            throw new Exception($"Duplicate desktop entity name {editorEntityType}->{runtimeType}, already maps to {editorToRuntimeName[editorEntityType]}");
        }

        foreach (var (editorPath, runtimePath) in toBoth ?? []) {
            var editorSep = editorPath.IndexOf('.');
            string field, subpath;
            if (editorSep == -1) {
                field = editorPath;
                subpath = "";
            } else {
                field = editorPath.Substring(0, editorSep);
                subpath = editorPath.Substring(editorSep + 1);
            }
            if (!entry.editorToRuntime.TryAdd((field, subpath), runtimePath) || !entry.runtimeToEditor.TryAdd(runtimePath, (field, subpath))) {
                throw new Exception($"Duplicate runtime/desktop bothways entity mapping entry {editorPath} -> {runtimePath}");
            }
        }

        foreach (var (editorPath, runtimePath) in toRuntime ?? []) {
            var editorSep = editorPath.IndexOf('.');
            string field, subpath;
            if (editorSep == -1) {
                field = editorPath;
                subpath = "";
            } else {
                field = editorPath.Substring(0, editorSep);
                subpath = editorPath.Substring(editorSep + 1);
            }
            if (!entry.editorToRuntime.TryAdd((field, subpath), runtimePath)) {
                throw new Exception($"Duplicate runtime entity mapping entry {editorPath} -> {runtimePath}");
            }
        }

        foreach (var (runtimePath, editorPath) in toDesktop ?? []) {
            var editorSep = editorPath.IndexOf('.');
            string field, subpath;
            if (editorSep == -1) {
                field = editorPath;
                subpath = "";
            } else {
                field = editorPath.Substring(0, editorSep);
                subpath = editorPath.Substring(editorSep + 1);
            }
            if (!entry.runtimeToEditor.TryAdd(runtimePath, (field, subpath))) {
                throw new Exception($"Duplicate desktop entity mapping entry {runtimePath} -> {editorPath}");
            }
        }
    }

    public bool HasRuntimeMapping(string runtimeType, [MaybeNullWhen(false)] out string editorType)
    {
        if (!runtimeEntries.TryGetValue(runtimeType, out var mapping)) {
            editorType = null;
            return false;
        }

        return runtimeToEditorName.TryGetValue(runtimeType, out editorType);
    }

    public bool HasDesktopMapping(string editorType, [MaybeNullWhen(false)] out string runtimeType)
    {
        if (!editorEntries.TryGetValue(editorType, out var mapping)) {
            runtimeType = null;
            return false;
        }

        return editorToRuntimeName.TryGetValue(editorType, out runtimeType);
    }

    public bool MapToDesktop(string runtimeType, JsonObject? runtimeData, Entity editorEntity)
    {
        if (!runtimeEntries.TryGetValue(runtimeType, out var mapping) || !runtimeToEditorName.TryGetValue(runtimeType, out var editorType)) {
            return false;
        }

        foreach (var (runtimePath, desktop) in mapping.runtimeToEditor) {
            var (editorField, editorPath) = desktop;
            editorEntity.Data ??= new();
            var sourceData = GetNodeByPath(runtimeData, runtimePath)?.DeepClone();
            if (!editorEntity.Data.TryGetValue(editorField, out var editorData) || editorData == null) {
                if (string.IsNullOrEmpty(editorPath)) {
                    editorEntity.Data[editorField] = sourceData;
                    continue;
                }

                editorEntity.Data[editorField] = editorData = new JsonObject();
            }

            SetNodeByPath(editorData, editorPath, sourceData);
        }
        return true;
    }

    public bool MapToRuntime(string editorType, Entity editorEntity, JsonObject runtimeData)
    {
        if (!editorEntries.TryGetValue(editorType, out var mapping) || !editorToRuntimeName.TryGetValue(editorType, out var runtimeType)) {
            return false;
        }

        if (editorEntity.Data == null) return true;

        foreach (var (desktop, runtimePath) in mapping.editorToRuntime) {
            var (editorField, editorPath) = desktop;
            if (!editorEntity.Data.TryGetValue(editorField, out var sourceData) || sourceData == null) {
                if (editorField == "null") {
                    SetNodeByPath(runtimeData, runtimePath, new JsonObject());
                }
                continue;
            }

            if (!string.IsNullOrEmpty(editorPath)) {
                sourceData = GetNodeByPath(sourceData, editorPath)?.DeepClone();
            }
            SetNodeByPath(runtimeData, runtimePath, sourceData);
        }
        return true;
    }

    private bool ApplyMapping(JsonObject? source, JsonObject? target, string sourcePath, string targetPath)
    {
        return false;
    }

    private static JsonNode? GetNodeByPath(JsonNode? node, string path)
    {
        if (string.IsNullOrEmpty(path)) return node;

        if (node is not JsonObject obj) {
            Logger.Error("Only object paths are currently supported");
            return null;
        }
        var sep = path.IndexOf('.');
        if (sep == -1) {
            return obj[path];
        }

        var field = path.Substring(0, sep);
        if (obj.TryGetPropertyValue(field, out var next)) {
            return GetNodeByPath(next, path.Substring(sep + 1));
        }

        return null;
    }

    private static void SetNodeByPath(JsonNode node, string path, JsonNode? value)
    {
        if (node is not JsonObject obj) {
            Logger.Error("Only object paths are currently supported");
            return;
        }
        var sep = path.IndexOf('.');
        if (sep == -1) {
            obj[path] = value;
            return;
        }

        var field = path.Substring(0, sep);
        if (!obj.TryGetPropertyValue(field, out var next) || next == null) {
            obj[field] = next = new JsonObject();
        }

        SetNodeByPath(next, path.Substring(sep + 1), value);
    }
}
