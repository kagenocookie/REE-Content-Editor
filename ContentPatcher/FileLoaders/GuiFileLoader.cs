using ReeLib;

namespace ContentPatcher;

public class GuiFileLoader : IFileLoader, IFileHandleContentProvider<GuiFile>
{
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.GUI;

    public GuiFile GetFile(FileHandle handle) => handle.GetFile<GuiFile>();

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile Load(ContentWorkspace workspace, FileHandle handle)
    {
        var file = new GuiFile(new FileHandler(handle.Stream, handle.Filepath));
        file.Read();
        return new BaseFileResource<GuiFile>(file);
    }

    public IResourceFile? CreateNewFile(ContentWorkspace workspace, FileHandle handle)
    {
        var file = new GuiFile(new FileHandler(handle.Stream, handle.Filepath));
        file.Write();
        return new BaseFileResource<GuiFile>(file);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetFile<GuiFile>();
        return file.SaveOrWriteTo(handle, outputPath);
    }
}
