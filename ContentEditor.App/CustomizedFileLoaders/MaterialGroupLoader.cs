using Assimp;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.Mdf;

namespace ContentEditor.App.FileLoaders;

public class MaterialGroupLoader : IFileLoader
{
    int IFileLoader.Priority => 30;

    public bool CanHandleFile(string filepath, REFileFormat format, FileHandle? file)
    {
        return format.format == KnownFileFormats.MeshMaterial;
    }

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var mdf = new MdfFile(fileHandler);
        if (!mdf.Read()) return null;

        var wrapper = new MaterialGroupWrapper(mdf);
        wrapper.UpdateMaterialLookups();
        foreach (var mat in wrapper.Materials) {
            if (!string.IsNullOrEmpty(mat.AlbedoTexture?.texPath)) {
                // preload the texture since we'll probably need it
                workspace.ResourceManager.TryResolveGameFile(mat.AlbedoTexture.texPath, out _);
            }
        }
        handle.Reverted += wrapper.UpdateMaterialLookups;

        return wrapper;
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
        var res = handle.GetResource<MaterialGroupWrapper>();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        return res.File.WriteTo(outputPath);
    }

    public static void ExportTextures(ContentWorkspace env, MdfFile mdf2, string outputFolder, string format, Func<MaterialGroupWrapper.MaterialLookupData, TexHeader, bool>? exportCondition = null)
    {
        Texture? tempTexture = null;
        if (format != "dds") {
            tempTexture = new Texture();
        }
        var material = new MaterialGroupWrapper(mdf2);

        exportCondition ??= (mat, param) => param == mat.AlbedoTexture || param == mat.NormalTexture || param == mat.ATXXTexture;
        foreach (var mat in material.Materials) {
            var matPath = Path.Combine(outputFolder, mat.Name);
            foreach (var param in mat.Textures) {
                if (string.IsNullOrEmpty(param.texPath) || !exportCondition.Invoke(mat, param)) {
                    continue;
                }

                if (!env.ResourceManager.TryResolveGameFile(param.texPath, out var file)) {
                    continue;
                }
                var tex = file.GetFile<TexFile>();

                var streamingPath = PathUtils.GetStreamingInternalPath(param.texPath);
                if (tex.Header.flags.HasFlag(ReeLib.Tex.TexFlags.IsStreaming) && env.ResourceManager.TryResolveGameFile(streamingPath, out var streamingFile)) {
                    tex = streamingFile.GetFile<TexFile>();
                }

                var texOutPath = Path.Combine(matPath, PathUtils.GetFilenameWithoutExtensionOrVersion(param.texPath).ToString());
                if (format == "dds") {
                    texOutPath += ".dds";
                    var dds = tex.ConvertToDDS();
                    dds.FileHandler.SaveAs(texOutPath);
                } else {
                    texOutPath += "." + format;
                    tempTexture!.LoadFromTex(tex);
                    tempTexture.SaveAs(texOutPath);
                }
            }
        }
    }
}
