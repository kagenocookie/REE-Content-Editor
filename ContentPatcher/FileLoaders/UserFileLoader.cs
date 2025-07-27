using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public class UserFileLoader : IFileLoader, IFileHandleContentProvider<UserFile>
{
    public static readonly UserFileLoader Instance = new();

    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.UserData;

    public UserFile GetFile(FileHandle handle) => handle.GetContent<UserFile>();

    public IResourceFilePatcher? CreateDiffHandler() => new UserFilePatcher();

    public bool Load(ContentWorkspace workspace, FileHandle handle)
    {
        if (handle.Resource == null) {
            var file = new UserFile(workspace.Env.RszFileOption, new FileHandler(handle.Stream, handle.Filepath));
            if (!file.Read()) return false;

            handle.Resource = new BaseFileResource<UserFile>(file);
            return true;
        } else {
            var file = ((BaseFileResource<UserFile>)handle.Resource).File;
            file.Clear();
            return file.Read();
        }
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetContent<UserFile>();
        file.RebuildInfoTable();
        return file.SaveOrWriteTo(handle, outputPath);
    }
}

public sealed class UserFilePatcher : RszFilePatcherBase, IDisposable
{
    private UserFile file = null!;
    private FileHandle fileHandle = null!;

    private sealed class UserFileResource(UserFile file) : BaseFileResource<UserFile>(file), IResourceFile
    {
        void IResourceFile.WriteTo(string filepath)
        {
            File.RebuildInfoTable();
            File.WriteTo(filepath);
        }
    }

    public override IResourceFile LoadBase(ContentWorkspace workspace, FileHandle handle)
    {
        fileHandle = handle;
        file = handle.GetContent<UserFile>();
        this.workspace = workspace;
        return handle.Resource;
    }

    public override JsonNode? FindDiff(FileHandle handle)
    {
        var newfile = handle.GetContent<UserFile>();
        var baseObj = file.RSZ.ObjectList[0];
        var patchObj = newfile.RSZ.ObjectList[0];
        var diff = GetRszInstanceDiff(baseObj, patchObj);
        return diff;
    }

    public override void ApplyDiff(JsonNode diff)
    {
        ApplyObjectDiff(file.RSZ.ObjectList[0], diff);
    }

    public void Dispose()
    {
        file?.Dispose();
    }

    public UserFile GetFile() => file;
}
