using Assimp;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public class MaterialGroupLoader : IFileLoader,
    IFileHandleContentProvider<Assimp.Scene>
{
    int IFileLoader.Priority => 30;

    public bool CanHandleFile(string filepath, REFileFormat format)
    {
        return format.format == KnownFileFormats.MeshMaterial;
    }

    public IResourceFilePatcher? CreateDiffHandler() => null;

    private static readonly HashSet<string> AlbedoTextures = ["BaseDielectricMap", "ALBD", "ALBDmap", "BackMap", "BaseMetalMap", "BaseDielectricMapBase", "BaseAlphaMap"];

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var mdf = new MdfFile(fileHandler);
        if (!mdf.Read()) return null;

        var scene = new Assimp.Scene();
        foreach (var srcMat in mdf.Materials) {
            var newMat = new Assimp.Material();
            newMat.Name = srcMat.Header.matName;
            var diffuseCandidates = srcMat.Textures.Where(tex => AlbedoTextures.Contains(tex.texType));
            var diffuse = diffuseCandidates.FirstOrDefault(tex => tex.texPath?.Contains("null", StringComparison.InvariantCultureIgnoreCase) == false)
                ?? diffuseCandidates.FirstOrDefault();
            if (diffuse?.texPath != null) {
                newMat.TextureDiffuse = new TextureSlot(diffuse.texPath, TextureType.Diffuse, 0, TextureMapping.FromUV, 0, 1, TextureOperation.Multiply, TextureWrapMode.Wrap, TextureWrapMode.Wrap, 0);
                // preload the texture since we'll probably need it
                workspace.ResourceManager.TryResolveGameFile(diffuse.texPath, out _);
            }

            scene.Materials.Add(newMat);
        }

        return new AssimpMaterialResource(mdf, scene);
    }

    public IResourceFile? CreateNewFile(ContentWorkspace workspace, FileHandle handle)
    {
        var file = new MdfFile(new FileHandler(handle.Stream, handle.Filepath));
        file.Write();
        file.FileHandler.Seek(0);
        return Load(workspace, handle);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var res = handle.GetResource<AssimpMaterialResource>();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        return res.File.WriteTo(outputPath);
    }

    Assimp.Scene IFileHandleContentProvider<Assimp.Scene>.GetFile(FileHandle handle) => handle.GetResource<AssimpMaterialResource>().Scene;
}

public class AssimpMaterialResource : BaseFileResource<MdfFile>
{
    public AssimpMaterialResource(MdfFile file, Assimp.Scene importedScene) : base(file)
    {
        Scene = importedScene;
    }

    public Assimp.Scene Scene { get; }
}