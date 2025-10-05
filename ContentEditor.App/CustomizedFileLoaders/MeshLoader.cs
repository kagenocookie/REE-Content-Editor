using System.Globalization;
using System.Text.RegularExpressions;
using Assimp;
using Assimp.Configs;
using Assimp.Unmanaged;
using ContentEditor;
using ContentEditor.App;
using ContentPatcher;
using ReeLib;

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
            var file = new MeshFile(fileHandler);
            if (!file.Read()) return null;

            if (file.MeshBuffer?.Positions == null) {
                Logger.Error("Mesh has no vertices");
                return null;
            }

            return new AssimpMeshResource(name, workspace.Env) {
                NativeMesh = file,
                GameVersion = workspace.Env.Config.Game.GameEnum,
            };
        } else {
            using AssimpContext importer = new AssimpContext();
            importer.SetConfig(new IntegerPropertyConfig(AiConfigs.AI_CONFIG_PP_SLM_VERTEX_LIMIT, ushort.MaxValue));
            importedScene = importer.ImportFileFromStream(
                handle.Stream,
                PostProcessSteps.Triangulate |
                PostProcessSteps.GenerateBoundingBoxes |
                PostProcessSteps.GenerateNormals |
                PostProcessSteps.GenerateUVCoords |
                PostProcessSteps.CalculateTangentSpace |
                PostProcessSteps.SplitLargeMeshes,
                Path.GetExtension(handle.Filepath));

            if (!importedScene.HasMeshes) {
                Logger.Error("No meshes found in file " + handle.Filepath);
                return null;
            }

            return new AssimpMeshResource(name, workspace.Env) {
                Scene = importedScene,
                GameVersion = workspace.Env.Config.Game.GameEnum,
            };
        }

    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var mesh = handle.GetResource<AssimpMeshResource>();
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
        var mr = handle.GetResource<AssimpMeshResource>();
        if (mr.HasAnimations) {
            return mr.Motlist;
        }

        Logger.Error("Mesh does not contain animations: " + handle.Filepath);
        return new MotlistFile(new FileHandler());
    }
}
