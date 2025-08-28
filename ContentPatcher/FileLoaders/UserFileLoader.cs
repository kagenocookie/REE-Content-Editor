using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public class UserFileLoader : IFileLoader, IFileHandleContentProvider<UserFile>
{
    public static readonly UserFileLoader Instance = new();

    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.UserData;

    public UserFile GetFile(FileHandle handle) => handle.GetFile<UserFile>();

    public IResourceFilePatcher? CreateDiffHandler() => new UserFilePatcher();

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        if (handle.Resource == null) {
            var file = new UserFile(workspace.Env.RszFileOption, new FileHandler(handle.Stream, handle.Filepath));
            if (!file.Read()) return null;

            return new BaseFileResource<UserFile>(file);
        } else {
            var file = ((BaseFileResource<UserFile>)handle.Resource).File;
            file.Clear();
            return file.Read() ? handle.Resource : null;
        }
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetFile<UserFile>();
        file.RebuildInfoTable();
        return file.SaveOrWriteTo(handle, outputPath);
    }
}

public sealed class UserFilePatcher : RszFilePatcherBase, IDisposable
{
    private UserFile file = null!;
    private FileHandle fileHandle = null!;

    public override IResourceFile LoadBase(ContentWorkspace workspace, FileHandle handle)
    {
        fileHandle = handle;
        file = handle.GetFile<UserFile>();
        this.workspace = workspace;
        return handle.Resource;
    }

    public override JsonNode? FindDiff(FileHandle handle)
    {
        var newfile = handle.GetFile<UserFile>();
        var baseObj = file.RSZ.ObjectList[0];
        var patchObj = newfile.RSZ.ObjectList[0];
        var diff = GetRszInstanceDiff(baseObj, patchObj);
        var embedDiff = GetEmbeddedDiffs(file.RSZ, newfile.RSZ);
        if (embedDiff != null) {
            diff ??= new JsonObject();
            diff["__embeds"] = embedDiff;
        }
        return diff;
    }

    public override void ApplyDiff(JsonNode diff) => ApplyDiff(fileHandle, diff);
    public override void ApplyDiff(FileHandle targetFile, JsonNode diff)
    {
        var file = targetFile.GetFile<UserFile>();
        ApplyObjectDiff(file.RSZ.ObjectList[0], diff);
        if (diff is JsonObject obj && obj.TryGetPropertyValue("__embeds", out var embedDiff) && embedDiff is JsonObject eDiffObj) {
            ApplyEmbeddedDiffs(file.RSZ, eDiffObj);
        }
    }

    public void Dispose()
    {
        file?.Dispose();
    }

    public UserFile GetFile() => file;
}
