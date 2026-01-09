using ReeLib;

namespace ContentPatcher;

public class McolFileLoader : IFileLoader, IFileHandleContentProvider<McolFile>
{
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.CollisionMesh;

    public McolFile GetFile(FileHandle handle) => handle.GetFile<McolFile>();

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile Load(ContentWorkspace workspace, FileHandle handle)
    {
        var file = new McolFile(new FileHandler(handle.Stream, handle.Filepath));
        file.Read();
        return new BaseFileResource<McolFile>(file);
    }

    public IResourceFile? CreateNewFile(ContentWorkspace workspace, FileHandle handle)
    {
        var file = new McolFile(new FileHandler(handle.Stream, handle.Filepath));
        file.Write();
        return new BaseFileResource<McolFile>(file);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetFile<McolFile>();
        file.bvh?.RegenerateNodeBoundaries();
        file.bvh?.BuildTree();
        return file.SaveOrWriteTo(handle, outputPath);
    }
}
