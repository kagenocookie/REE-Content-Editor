using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Assimp;
using Assimp.Configs;
using ContentEditor.App.Blender;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.FileLoaders;

public partial class MeshLoader : IFileLoader,
    IFileHandleContentProvider<MeshFile>,
    IFileHandleContentProvider<MotlistFile>
{
    int IFileLoader.Priority => 30;

    public bool CanHandleFile(string filepath, REFileFormat format, FileHandle? file)
    {
        return format.format == KnownFileFormats.Mesh || MeshViewer.IsSupportedFileExtension(filepath);
    }

    public static readonly HashSet<string> StandardFileExtensions = [".glb", ".gltf", ".obj", ".fbx", ".stl", ".ply", ".blend"];

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        var name = PathUtils.GetFilepathWithoutExtensionOrVersion(handle.Filename).ToString();
        Assimp.Scene importedScene;
        if (handle.Format.format == KnownFileFormats.Mesh) {
            var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
            uint magic = fileHandler.Read<uint>(0);
            if (magic == MeshFile.Magic) {
                var file = new MeshFile(fileHandler);
                if (!file.Read()) return null;

                if (file.MeshBuffer?.Positions == null) {
                    Logger.Error("Mesh has no vertices");
                    return null;
                }

                if (file.RequiresStreamingData) {
                    if (workspace.ResourceManager.TryResolveStreamingBufferFile(handle, out var bufferFile)) {
                        file.LoadStreamingData(new FileHandler(bufferFile.Stream, bufferFile.TargetPath));
                    } else {
                        Logger.Warn("Could not resolve streaming buffer for streaming mesh file " + handle.Filepath);
                    }
                }

                var resource = new CommonMeshResource(name, workspace.Env) {
                    NativeMesh = file,
                    GameVersion = workspace.Env.Config.Game.GameEnum,
                };
                resource.PreloadMeshBuffers();
                return resource;
            } else if (magic == MplyMeshFile.Magic) {
                var mply = new MplyMeshFile(fileHandler);
                if (!mply.Read()) return null;

                // if (workspace.ResourceManager.TryResolveStreamingBufferFile(handle, out var bufferFile)) {
                //     mesh.LoadStreamingData(new FileHandler(bufferFile.Stream, bufferFile.NativePath));
                // } else {
                //     Logger.Warn("Could not resolve streaming buffer for streaming mesh file " + handle.Filepath);
                // }

                var mesh = mply.ConvertToMergedClassicMesh();
                mesh.FileHandler = mply.FileHandler;

                var resource = new CommonMeshResource(name, workspace.Env) {
                    NativeMesh = mesh,
                    GameVersion = workspace.Env.Config.Game.GameEnum,
                };
                resource.PreloadMeshBuffers();
                return resource;
            } else {
				throw new NotSupportedException("Unknown mesh type " + magic.ToString("X"));
            }
        } else {
            var filepath = handle.Filepath;
            var ext = Path.GetExtension(handle.Filepath.AsSpan());
            var useDirectFilepath = false;
            if (ext.SequenceEqual(".blend")) {
                filepath = HandleBlenderImportConversion(filepath);
                if (filepath == null) {
                    return null;
                }
                useDirectFilepath = true;
                ext = Path.GetExtension(filepath.AsSpan());
            }
            using AssimpContext importer = new AssimpContext();
            importer.SetConfig(new MeshVertexLimitConfig(ushort.MaxValue));
            importer.SetConfig(new VertexBoneWeightLimitConfig(16));
            importer.SetConfig(new BooleanPropertyConfig("USE_UNLIMITED_BONES_PER VERTEX", true));
            var importFlags = PostProcessSteps.Triangulate |
                PostProcessSteps.GenerateBoundingBoxes |
                PostProcessSteps.GenerateNormals |
                PostProcessSteps.GenerateUVCoords |
                PostProcessSteps.CalculateTangentSpace |
                PostProcessSteps.SplitLargeMeshes;
            if (useDirectFilepath || ext.SequenceEqual(".gltf") && File.Exists(Path.ChangeExtension(filepath, ".bin"))) {
                importedScene = importer.ImportFile(filepath, importFlags);
            } else {
                importedScene = importer.ImportFileFromStream(handle.Stream, importFlags, Path.GetExtension(filepath));
            }

            if (!importedScene.HasMeshes && !importedScene.HasAnimations) {
                Logger.Error("No meshes or animations found in file " + filepath);
                return null;
            }

            var isGltf = ext.SequenceEqual(".gltf") || ext.SequenceEqual(".glb");
            var resource = new CommonMeshResource(name, workspace.Env) {
                Scene = importedScene,
                GameVersion = workspace.Env.Config.Game.GameEnum,
            };
            resource.ImportMaterials(importedScene);
            var mesh = resource.ReImportFromScene(isGltf);
            resource.PreloadMeshBuffers();

            return resource;
        }
    }

    public static bool TryUpdateBlendFileSceneInfo(string blendFilepath)
    {
        var importSettingsPath = blendFilepath + ".import_meta.json";
        if (!File.Exists(importSettingsPath) || !importSettingsPath.TryDeserializeJsonFile<BlendFileImportSettings>(out var settings, out var err)) {
            settings = new BlendFileImportSettings();
            settings.SaveToFile(importSettingsPath);
        }

        return TryUpdateBlendFileSceneInfo(blendFilepath, settings);
    }

    private static bool TryUpdateBlendFileSceneInfo(string blendFilepath, BlendFileImportSettings settings)
    {
        settings.CacheTimeUtc = DateTime.UtcNow;
        settings.CachedSceneInfo = BlenderInterop.GetSceneInfoAsync(blendFilepath).Result;
        if (settings.CachedSceneInfo == null) {
            return false;
        }

        foreach (var arm in settings.CachedSceneInfo.Armatures) {
            var conf = settings.Configs.FirstOrDefault(c => c.SelectedArmature == arm.Name || c.Name == arm.Name);
            if (conf != null) continue;

            conf = new BlendFileImportConfig() {
                SelectedArmature = arm.Name,
                Name = arm.Name,
                ImportAllMeshes = true,
            };
            settings.Configs.Add(conf);
        }
        if (settings.Configs.Count == 0) {
            settings.Configs.Add(new BlendFileImportConfig() { Name = "default" });
        }
        settings.CurrentConfig = settings.Configs[0].Name;
        settings.SaveToFile(blendFilepath + ".import_meta.json");
        return true;
    }

    private static string? HandleBlenderImportConversion(string filepath)
    {
        if (string.IsNullOrEmpty(AppConfig.Instance.BlenderPath.Get())) {
            throw new FileImportException("Currently unable to open .blend files, blender path is not configured.");
        }

        var importSettingsPath = filepath + ".import_meta.json";
        if (!File.Exists(importSettingsPath) || !importSettingsPath.TryDeserializeJsonFile<BlendFileImportSettings>(out var settings, out var err)) {
            settings = new BlendFileImportSettings();
            settings.SaveToFile(importSettingsPath);
        }

        var writeTimeUtc = new FileInfo(filepath).LastWriteTimeUtc;
        if (settings.CachedSceneInfo == null || writeTimeUtc > settings.CacheTimeUtc) {
            if (!TryUpdateBlendFileSceneInfo(filepath, settings) || settings.CachedSceneInfo == null) {
                throw new FileImportException("Could not retrieve .blend file scene info. File cannot be imported.");
            }
        }

        BlendFileImportConfig? importConf = null;
        if (settings.CurrentConfig != null) importConf = settings.Configs.FirstOrDefault(c => c.Name == settings.CurrentConfig);
        importConf ??= settings.Configs.FirstOrDefault(c => c.Name == "default");

        if (importConf == null) {
            importConf = new BlendFileImportConfig();
            if (settings.CachedSceneInfo.Armatures.Count > 0) {
                importConf.SelectedArmature = settings.CachedSceneInfo.Armatures[0].Name;
                importConf.Name = "default";
                settings.Configs.Add(importConf);
                settings.CurrentConfig = importConf.Name;
                settings.SaveToFile(importSettingsPath);
            }
        }

        var whitelist = new List<string>();
        if (importConf.SelectedArmature != null) {
            whitelist.Add(importConf.SelectedArmature);
            if (importConf.ImportAllMeshes) {
                var allMeshes = settings.CachedSceneInfo.Armatures.FirstOrDefault(a => a.Name == importConf.SelectedArmature)?.Objects.Select(o => o.Name);
                if (allMeshes?.Any() == true)
                    whitelist.AddRange(allMeshes);
            } else {
                whitelist.AddRange(importConf.ImportedObjects);
            }
        } else if (importConf.ImportAllMeshes) {
            if (settings.CachedSceneInfo.Armatures.Count > 0) {
                whitelist.AddRange(settings.CachedSceneInfo.StandaloneObjects.Select(o => o.Name));
            }
        } else {
            whitelist.AddRange(importConf.ImportedObjects);
        }

        var whitelistStr = whitelist.Count == 0 ? "" : string.Join(", ", whitelist.Select(m => $"'{m}'"));
        var outputPath = $"{filepath.NormalizeFilepath()}.{importConf.Name}.glb";
        var importScript = BlenderInterop.GetScript("blender_mesh_import.py");

        importScript = importScript
            .Replace("'__IMPORT_WHITELIST__'", whitelistStr)
            .Replace("__INCLUDE_TANGENTS__", importConf.IncludeTangents ? "True": "False")
            .Replace("__APPLY_ROTATION__", importConf.ApplyRotations ? "True": "False")
            .Replace("__OUTPUT_PATH__", outputPath);
        if (File.Exists(outputPath)) {
            File.Delete(outputPath);
        }
        try {
            var exec = BlenderInterop.ExecuteBlenderScriptAsync(filepath, importScript, true, outputPath);
            exec.Wait();
            return outputPath;
        } catch (Exception e) {
            Logger.Error(e);
            return null;
        }
    }

    public IResourceFile? CreateNewFile(ContentWorkspace workspace, FileHandle handle) => null;

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var mesh = handle.GetResource<CommonMeshResource>();
        return mesh.NativeMesh.SaveOrWriteTo(handle, outputPath);
    }

    [GeneratedRegex("Group_([\\d]+)")]
    private static partial Regex MeshGroupRegex();

    [GeneratedRegex("sub([\\d]+)")]
    private static partial Regex SubmeshIndexRegex();

    public static int GetMeshGroupFromName(string meshName)
    {
        var match = MeshGroupRegex().Match(meshName);
        if (match.Success) {
            return int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        }
        return 0;
    }

    public static string GetMeshMaterialFromName(string name)
    {
        var dsub = name.IndexOf("__");
        if (dsub == -1) {
            return "NO_MATERIAL";
        }

        return name.Substring(dsub + 2);
    }

    public static int GetSubMeshIndexFromName(string meshName)
    {
        var match = SubmeshIndexRegex().Match(meshName);
        if (match.Success) {
            return int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        }
        return 0;
    }

    MotlistFile IFileHandleContentProvider<MotlistFile>.GetFile(FileHandle handle)
    {
        var mr = handle.GetResource<CommonMeshResource>();
        if (mr.HasAnimations) {
            return mr.Motlist;
        }

        Logger.Error("Mesh does not contain animations: " + handle.Filepath);
        return new MotlistFile(new FileHandler());
    }

    public MeshFile GetFile(FileHandle handle)
    {
        return handle.GetResource<CommonMeshResource>().NativeMesh;
    }
}

public class BlendFileImportSettings
{
    public string? CurrentConfig { get; set; }
    public DateTime CacheTimeUtc { get; set; }
    public SceneInfo? CachedSceneInfo { get; set; }
    public List<BlendFileImportConfig> Configs { get; set; } = [];

    public void SaveToFile(string importSettingsPath)
    {
        using var fs = File.Create(importSettingsPath);
        JsonSerializer.Serialize(fs, this, JsonConfig.configJsonOptions);
    }
}

public class BlendFileImportConfig
{
    public string Name { get; set; } = "";
    public bool IncludeTangents { get; set; }
    public bool ApplyRotations { get; set; } = true;
    public bool ImportAllMeshes { get; set; } = true;
    public bool ImportTextures { get; set; }

    public string? SelectedArmature { get; set; }
    public List<string> ImportedObjects { get; set; } = [];
}
