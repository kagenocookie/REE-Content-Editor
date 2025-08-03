using ReeLib;

namespace ContentPatcher;

public class UvsFileLoader : IFileLoader, IFileHandleContentProvider<UvsFile>
{
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.UVSequence;

    public UvsFile GetFile(FileHandle handle) => handle.GetFile<UvsFile>();

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        if (handle.Resource != null) {
            return ((BaseFileResource<UvsFile>)handle.Resource).File.Read() ? handle.Resource : null;
        }
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new UvsFile(fileHandler);
        if (!file.Read()) return null;

        return new BaseFileResource<UvsFile>(file);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetFile<UvsFile>();
        file.TrimOrphanTextures();
        return file.SaveOrWriteTo(handle, outputPath);
    }
}
