using System.Globalization;
using System.Text.RegularExpressions;
using Assimp;
using Assimp.Configs;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;

namespace ContentEditor.App.FileLoaders;

public partial class MeshLoader : IFileLoader, IFileHandleContentProvider<MotlistFile>
{
    int IFileLoader.Priority => 30;

    public bool CanHandleFile(string filepath, REFileFormat format)
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
            uint magic = 0;
            handle.Stream.Seek(0, SeekOrigin.Begin);
            handle.Stream.Read(MemoryUtils.StructureAsBytes(ref magic));
            handle.Stream.Seek(0, SeekOrigin.Begin);
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
            importedScene = importer.ImportFileFromStream(
                handle.Stream,
                PostProcessSteps.Triangulate |
                PostProcessSteps.GenerateBoundingBoxes |
                PostProcessSteps.GenerateNormals |
                PostProcessSteps.GenerateUVCoords |
                PostProcessSteps.CalculateTangentSpace |
                PostProcessSteps.SplitLargeMeshes,
                Path.GetExtension(handle.Filepath));

            if (!importedScene.HasMeshes && !importedScene.HasAnimations) {
                Logger.Error("No meshes or animations found in file " + handle.Filepath);
                return null;
            }

            var resource = new CommonMeshResource(name, workspace.Env) {
                Scene = importedScene,
                GameVersion = workspace.Env.Config.Game.GameEnum,
            };

            var mesh = resource.NativeMesh;
            resource.PreloadMeshBuffers();

            return resource;
        }
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var mesh = handle.GetResource<CommonMeshResource>();
        return mesh.NativeMesh.SaveOrWriteTo(handle, outputPath);
    }

    [GeneratedRegex("Group_([\\d]+)")]
    private static partial Regex MeshGroupRegex();

    [GeneratedRegex("mesh([\\d]+)")]
    private static partial Regex MeshIndexRegex();

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

    public static int GetMeshIndexFromName(string meshName)
    {
        var match = MeshIndexRegex().Match(meshName);
        if (match.Success) {
            return int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        }
        return 0;
    }

    public static int GetSubMeshIndexFromName(string meshName)
    {
        var match = SubmeshIndexRegex().Match(meshName);
        if (match.Success) {
            return int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        }
        return 0;
    }

    public MotlistFile GetFile(FileHandle handle)
    {
        var mr = handle.GetResource<CommonMeshResource>();
        if (mr.HasAnimations) {
            return mr.Motlist;
        }

        Logger.Error("Mesh does not contain animations: " + handle.Filepath);
        return new MotlistFile(new FileHandler());
    }
}
