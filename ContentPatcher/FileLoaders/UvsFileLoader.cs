using ReeLib;

namespace ContentPatcher;

public class UvsFileLoader : IFileLoader, IFileHandleContentProvider<UvsFile>
{
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.UVSequence;

    public UvsFile GetFile(FileHandle handle) => handle.GetContent<UvsFile>();

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public bool Load(ContentWorkspace workspace, FileHandle handle)
    {
        if (handle.Resource != null) {
            return ((BaseFileResource<UvsFile>)handle.Resource).File.Read();
        }
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new UvsFile(fileHandler);
        if (!file.Read()) return false;

        handle.Resource = new BaseFileResource<UvsFile>(file);
        return true;
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetContent<UvsFile>();
        file.TrimOrphanTextures();
        return file.SaveOrWriteTo(handle, outputPath);
    }
}
