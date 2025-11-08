using System.Numerics;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public class GroundTerrainResourceFile(FileHandle handle, GtlFile file) : IResourceFile
{
    public GtlFile File { get; } = file;
    public Mesh? Mesh { get; set; }

    public Vector3 Min { get; set; }
    public Vector3 Max { get; set; }

    public void WriteTo(string filepath)
    {
        File.SaveOrWriteTo(handle, filepath);
    }
}

public class GtlFileLoader : IFileLoader, IFileHandleContentProvider<Mesh>
{
    public GtlFileLoader() { }

    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.GroundTextureList;
    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new GtlFile(fileHandler);
        if (!file.Read()) return null;

        if (workspace.Env.Config.Game == GameIdentifier.dd2) {
            file.ReadData(workspace.ResourceManager.ReadFileResource<GrndFile>("natives/stm/editdata/ground/world00/world00.grnd.858720015"), 0, 1);
        } else {
            var grndMatch = workspace.Env.GetFilesWithExtension("grnd").FirstOrDefault();
            if (grndMatch.Item1 == null || grndMatch.Item2 == null) {
                Logger.Warn("Could not find ground file. Content cannot be fully read.");
            } else {
                file.ReadData(workspace.ResourceManager.ReadFileResource<GrndFile>(grndMatch.Item1), 0, 1);
            }
        }

        return new GroundTerrainResourceFile(handle, file);
    }

    public Mesh GetFile(FileHandle handle)
    {
        var res = handle.GetResource<GroundTerrainResourceFile>();
        if (res.Mesh == null) {
            var hmesh = new HeightmapMesh();
            var file = res.File;

            // note: we need the Min/Max to have been set from the ground file's data before this is called
            var range = file.Ranges[0];
            hmesh.Update(file.DataItems[0].Heights, res.Min, res.Max, new Vector2(file.DataItems[0].Heights.Length / 2));
            res.Mesh = hmesh;
        }
        return res.Mesh;
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        // TODO: before we allow saving gtl files, need to allow full read instead of just level 1
        throw new NotImplementedException();
    }
}
