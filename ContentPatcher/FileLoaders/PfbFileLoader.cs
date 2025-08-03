using ReeLib;

namespace ContentPatcher;

public class PfbFileLoader : IFileLoader, IFileHandleContentProvider<PfbFile>
{
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.Prefab;

    public PfbFile GetFile(FileHandle handle) => handle.GetFile<PfbFile>();

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new PfbFile(workspace.Env.RszFileOption, fileHandler);
        file.Read();
        file.SetupGameObjects();
        return new BaseFileResource<PfbFile>(file);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetFile<PfbFile>();
        file.RebuildInfoTables();
        return file.SaveOrWriteTo(handle, outputPath);
    }
}
