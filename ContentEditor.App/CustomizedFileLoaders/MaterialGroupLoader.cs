using System.Reflection.Metadata;
using Assimp;
using Assimp.Configs;
using Assimp.Unmanaged;
using ContentEditor;
using ContentEditor.App;
using ContentEditor.App.Graphics;
using ReeLib;

namespace ContentPatcher;

public class MaterialGroupLoader : IFileLoader,
    IFileHandleContentProvider<Assimp.Scene>
{
    int IFileLoader.Priority => 30;

    public bool CanHandleFile(string filepath, REFileFormat format)
    {
        return format.format == KnownFileFormats.MaterialDefinition;
    }

    public IResourceFilePatcher? CreateDiffHandler() => null;

    private static HashSet<string> AlbedoTextures = ["BaseDielectricMap", "ALBD", "ALBDmap", "BackMap", "BaseMetalMap", "BaseDielectricMapBase"];

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var mdf = new MdfFile(fileHandler);
        if (!mdf.Read()) return null;

        var scene = new Assimp.Scene();
        foreach (var srcMat in mdf.Materials) {
            var newMat = new Assimp.Material();
            newMat.Name = srcMat.Header.matName;
            var diffuse = srcMat.Textures.FirstOrDefault(tex => AlbedoTextures.Contains(tex.texType));
            if (diffuse != null) {
                newMat.TextureDiffuse = new TextureSlot(diffuse.texPath, TextureType.Diffuse, 0, TextureMapping.FromUV, 0, 1, TextureOperation.Multiply, TextureWrapMode.Wrap, TextureWrapMode.Wrap, 0);
            }

            scene.Materials.Add(newMat);
        }

        return new AssimpMaterialResource(mdf, scene);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var fs = File.Create(outputPath);
        if (handle.Stream.CanSeek) handle.Stream.Seek(0, SeekOrigin.Begin);
        handle.Stream.CopyTo(fs);
        return true;
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