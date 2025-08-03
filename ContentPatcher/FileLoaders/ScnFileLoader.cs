using ReeLib;

namespace ContentPatcher;

public class ScnFileLoader : IFileLoader, IFileHandleContentProvider<ScnFile>
{
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.Scene;

    public ScnFile GetFile(FileHandle handle) => handle.GetFile<ScnFile>();

    public IResourceFilePatcher? CreateDiffHandler() => null;

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
