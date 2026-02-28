using Assimp;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.Mdf;

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

    public static readonly HashSet<string> AlbedoTextureNames = ["BaseDielectricMap", "ALBD", "ALBDmap", "BackMap", "BaseMetalMap", "BaseDielectricMapBase", "BaseAlphaMap"];
    public static readonly HashSet<string> NormalTextureNames = ["NormalRoughnessMap", "NormalRoughnessCavityMap"];
    public static readonly HashSet<string> ATXXTextureNames = ["AlphaTranslucentOcclusionCavityMap", "AlphaTranslucentOcclusionSSSMap"];

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var mdf = new MdfFile(fileHandler);
        if (!mdf.Read()) return null;

        var scene = new Assimp.Scene();
        foreach (var srcMat in mdf.Materials) {
            var newMat = new Assimp.Material();
            newMat.Name = srcMat.Header.matName;
            var diffuseCandidates = srcMat.Textures.Where(tex => AlbedoTextureNames.Contains(tex.texType));
            var diffuse = diffuseCandidates.FirstOrDefault(tex => tex.texPath?.Contains("null", StringComparison.InvariantCultureIgnoreCase) == false)
                ?? diffuseCandidates.FirstOrDefault();
            if (diffuse?.texPath != null && PathUtils.ParseFileFormat(diffuse.texPath).format != KnownFileFormats.RenderTexture) {
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

    public static void ExportTextures(ContentWorkspace env, MdfFile mdf2, string outputFolder, bool useTga, Func<string, TexHeader, bool>? exportCondition = null)
    {
        Texture? tempTexture = null;
        if (useTga) {
            tempTexture = new Texture();
        }
        exportCondition ??= (mat, param) => AlbedoTextureNames.Contains(param.texType) || NormalTextureNames.Contains(param.texType) || ATXXTextureNames.Contains(param.texType);
        foreach (var mat in mdf2.Materials) {
            var matPath = Path.Combine(outputFolder, mat.Header.matName);
            foreach (var param in mat.Textures) {
                if (string.IsNullOrEmpty(param.texPath) || !exportCondition.Invoke(mat.Header.matName, param)) {
                    continue;
                }

                if (!env.ResourceManager.TryResolveGameFile(param.texPath, out var file)) {
                    continue;
                }
                var tex = file.GetFile<TexFile>();

                var texOutPath = Path.Combine(matPath, PathUtils.GetFilenameWithoutExtensionOrVersion(param.texPath).ToString());
                if (useTga) {
                    texOutPath += ".tga";
                    tempTexture!.LoadFromTex(tex);
                    tempTexture.SaveAs(texOutPath);
                } else {
                    texOutPath += ".dds";
                    var dds = tex.ConvertToDDS();
                    dds.FileHandler.SaveAs(texOutPath);
                }
            }
        }
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