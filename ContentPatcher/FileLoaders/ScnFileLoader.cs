using ReeLib;

namespace ContentPatcher;

public class ScnFileLoader : IFileLoader, IFileHandleContentProvider<ScnFile>
{
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.Scene;

    public ScnFile GetFile(FileHandle handle) => handle.GetContent<ScnFile>();

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public bool Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new ScnFile(workspace.Env.RszFileOption, fileHandler);
        file.Read();
        file.SetupGameObjects();
        handle.Resource = new BaseFileResource<ScnFile>(file);
        return true;
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetContent<ScnFile>();
        file.RebuildInfoTable();
        return file.SaveOrWriteTo(handle, outputPath);
    }
}
