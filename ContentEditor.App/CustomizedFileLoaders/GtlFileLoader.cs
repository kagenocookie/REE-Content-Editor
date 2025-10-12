using System.Numerics;
using ContentEditor.App.Graphics;
using ContentPatcher;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public class GroundTerrainResourceFile(FileHandle handle, GtlFile file, CommonMeshResource assMesh) : IResourceFile
{
    public GtlFile File { get; } = file;
    public CommonMeshResource AssMesh { get; } = assMesh;

    public Vector3 Min { get; set; }
    public Vector3 Max { get; set; }

    public void WriteTo(string filepath)
    {
        File.SaveOrWriteTo(handle, filepath);
    }
}

public class GtlFileLoader : IFileLoader, IFileHandleContentProvider<CommonMeshResource>
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
            file.ReadData(workspace.ResourceManager.ReadFileResource<GrndFile>("natives/stm/editdata/ground/world00/world00.grnd.858720015"));
        } else {
            var grndMatch = workspace.Env.GetFilesWithExtension("grnd").FirstOrDefault();
            if (grndMatch.Item1 == null || grndMatch.Item2 == null) {
                Logger.Warn("Could not find ground file. Content cannot be fully read.");
            } else {
                file.ReadData(workspace.ResourceManager.ReadFileResource<GrndFile>(grndMatch.Item1));
            }
        }

        return new GroundTerrainResourceFile(handle, file, new CommonMeshResource(handle.Filename.ToString(), workspace.Env));
    }

    public CommonMeshResource GetFile(FileHandle handle)
    {
        var res = handle.GetResource<GroundTerrainResourceFile>();
        if (!res.AssMesh.HasNativeMesh) {
            res.AssMesh.PreloadedMeshes ??= new();
            var hm = new HeightmapMesh();
            var file = res.File;

            // note: we need the Min/Max to have been set from the ground file's data before this is called
            var range = file.Ranges[0];
            hm.Update(file.DataItems[0].Heights, res.Min, res.Max, new Vector2(file.DataItems[0].Heights.Length / 2));
            res.AssMesh.PreloadedMeshes.Add(hm);

            // TODO: figure out how we could not need the fake mesh data here
            var mesh = res.AssMesh.NativeMesh = new MeshFile(file.FileHandler);
            mesh.MeshBuffer = new ();
            mesh.MeshData = new(mesh.MeshBuffer);
            mesh.MeshData.LODs.Add(new ReeLib.Mesh.MeshLOD(mesh.MeshBuffer));
            mesh.MeshData.LODs[0].MeshGroups.Add(new ReeLib.Mesh.MeshGroup(mesh.MeshBuffer));
            mesh.MeshData.LODs[0].MeshGroups[0].Submeshes.Add(new ReeLib.Mesh.Submesh(mesh.MeshBuffer) { materialIndex = 0 });
            mesh.MaterialNames.Add("terrain0");
        }
        return res.AssMesh;
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        throw new NotImplementedException();
    }
}
