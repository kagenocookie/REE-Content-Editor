using System.Numerics;
using Assimp;
using ContentEditor.App.Graphics;
using ContentPatcher;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public class GroundMaterialResourceFile(FileHandle handle, GmlFile file, CommonMeshResource assMesh) : IResourceFile
{
    public GmlFile File { get; } = file;
    public CommonMeshResource AssMesh { get; } = assMesh;

    public void WriteTo(string filepath)
    {
        File.SaveOrWriteTo(handle, filepath);
    }
}

public class GmlFileLoader : IFileLoader, IFileHandleContentProvider<CommonMeshResource>
{
    public GmlFileLoader() { }

    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.GroundMaterialList;
    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new GmlFile(fileHandler);
        if (!file.Read()) return null;

        var meshdata = new CommonMeshResource(handle.Filename.ToString(), workspace.Env);
        var mats = new List<Assimp.Material>();
        foreach (var tex in file.Textures) {
            if (workspace.ResourceManager.TryResolveFile(tex.albedoPath, out var texfile)) {
                mats.Add(new Assimp.Material() {
                    TextureDiffuse = new TextureSlot(tex.albedoPath, TextureType.Diffuse, 0, TextureMapping.FromUV, 0, 1, TextureOperation.Multiply, TextureWrapMode.Wrap, TextureWrapMode.Wrap, 0),
                    Name = "terrain" + mats.Count
                });
            }
        }

        meshdata.Scene = new Assimp.Scene();
        meshdata.Scene.Materials.AddRange(mats);

        return new GroundMaterialResourceFile(handle, file, meshdata);
    }

    public CommonMeshResource GetFile(FileHandle handle)
    {
        var res = handle.GetResource<GroundMaterialResourceFile>();
        if (!res.AssMesh.HasNativeMesh) {
            res.AssMesh.PreloadedMeshes ??= new();
            var hm = new HeightmapMesh();
            var file = res.File;
            // note: we need the Min/Max to get set from the ground file's data before this is called
            // var range = file.Ranges[0];
            // hm.Update(file.DataItems[0].Heights, res.Min, res.Max);
            res.AssMesh.PreloadedMeshes.Add(hm);

            // TODO: figure out how we could not need the fake mesh data
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
