namespace ContentPatcher;

using System.Text.Json.Nodes;
using ReeLib;

public abstract class RszFilePatcherBase : IResourceFilePatcher
{
    protected ContentWorkspace workspace = null!;

    public abstract IResourceFile LoadBase(ContentWorkspace workspace, FileHandle file);

    public abstract JsonNode? FindDiff(FileHandle file);
    public abstract void ApplyDiff(JsonNode diff);

    protected JsonNode? GetRszInstanceDiff(RszInstance target, RszInstance source, bool fullySerialize = false)
    {
        // if this got called, it means both values are either an object or a struct, not basic types
        var targetJson = target.ToJson(workspace.Env);
        var sourceJson = source.ToJson(workspace.Env);
        var differ = new DiffHandler(workspace.Env);
        return differ.GetHierarchicalDataDiff(targetJson, sourceJson);
    }

    protected void ApplyObjectDiff(RszInstance target, JsonNode diff)
    {
        workspace.Diff.ApplyDiff(target, diff);
    }
}
