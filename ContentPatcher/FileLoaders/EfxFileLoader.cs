using ReeLib;

namespace ContentPatcher;

public class EfxFileLoader : IFileLoader, IFileHandleContentProvider<EfxFile>
{
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.Effect;

    public EfxFile GetFile(FileHandle handle) => handle.GetFile<EfxFile>();

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new EfxFile(fileHandler);
        file.Read();
        file.ParseExpressions();
        return new BaseFileResource<EfxFile>(file);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetFile<EfxFile>();
        return file.SaveOrWriteTo(handle, outputPath);
    }
}
