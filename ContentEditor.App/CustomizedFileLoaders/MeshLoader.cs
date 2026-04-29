using System.Globalization;
using System.Text.RegularExpressions;
using Assimp;
using Assimp.Configs;
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

    public static readonly HashSet<string> StandardFileExtensions = [".glb", ".gltf", ".obj", ".fbx", ".stl", ".ply"];

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
                        file.LoadStreamingData(new FileHandler(bufferFile.Stream, bufferFile.NativePath));
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
            if (Path.GetExtension(handle.Filepath) == ".gltf" && File.Exists(Path.ChangeExtension(handle.Filepath, ".bin"))) {
                importedScene = importer.ImportFile(handle.Filepath, importFlags);
            } else {
                importedScene = importer.ImportFileFromStream(handle.Stream, importFlags, Path.GetExtension(handle.Filepath));
            }

            if (!importedScene.HasMeshes && !importedScene.HasAnimations) {
                Logger.Error("No meshes or animations found in file " + handle.Filepath);
                return null;
            }

            var resource = new CommonMeshResource(name, workspace.Env) {
                Scene = importedScene,
                GameVersion = workspace.Env.Config.Game.GameEnum,
            };
            resource.ImportMaterials(importedScene);

            var mesh = resource.NativeMesh;
            resource.PreloadMeshBuffers();

            return resource;
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
