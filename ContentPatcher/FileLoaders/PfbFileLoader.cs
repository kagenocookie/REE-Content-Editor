using ReeLib;

namespace ContentPatcher;

public class PfbFileLoader : IFileLoader, IFileHandleContentProvider<PfbFile>
{
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.Prefab;

    public PfbFile GetFile(FileHandle handle) => handle.GetContent<PfbFile>();

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public bool Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new PfbFile(workspace.Env.RszFileOption, fileHandler);
        file.Read();
        file.SetupGameObjects();
        handle.Resource = new BaseFileResource<PfbFile>(file);
        return true;
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetContent<PfbFile>();
        file.RebuildInfoTable();
        return file.SaveOrWriteTo(handle, outputPath);
    }
}
