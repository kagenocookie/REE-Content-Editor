using Assimp;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public class GroundMaterialResourceFile(FileHandle handle, GmlFile file, CommonMeshResource assMesh) : IResourceFile
{
    public GmlFile File { get; } = file;
    public CommonMeshResource Mesh { get; } = assMesh;

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
        return handle.GetResource<GroundMaterialResourceFile>().Mesh;
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        throw new NotImplementedException();
    }
}
