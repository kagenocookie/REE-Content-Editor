using ReeLib;

namespace ContentPatcher;

public class TerrFileLoader : IFileLoader, IFileHandleContentProvider<TerrFile>
{
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.Terrain;

    public TerrFile GetFile(FileHandle handle) => handle.GetFile<TerrFile>();

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile Load(ContentWorkspace workspace, FileHandle handle)
    {
        var file = new TerrFile(new FileHandler(handle.Stream, handle.Filepath));
        file.Read();
        return new BaseFileResource<TerrFile>(file);
    }

    public IResourceFile? CreateNewFile(ContentWorkspace workspace, FileHandle handle)
    {
        var file = new TerrFile(new FileHandler(handle.Stream, handle.Filepath));
        file.Write();
        return new BaseFileResource<TerrFile>(file);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var file = handle.GetFile<TerrFile>();
        file.bvh?.RegenerateNodeBoundaries();
        file.bvh?.BuildTree();
        return file.SaveOrWriteTo(handle, outputPath);
    }
}
