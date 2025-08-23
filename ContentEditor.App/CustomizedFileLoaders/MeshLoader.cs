using Assimp;
using Assimp.Configs;
using Assimp.Unmanaged;
using ContentEditor;
using ContentEditor.App;
using ReeLib;

namespace ContentPatcher;

public class MeshLoader : IFileLoader
{
    int IFileLoader.Priority => 30;

    public bool CanHandleFile(string filepath, REFileFormat format)
    {
        return MeshViewer.IsSupportedFileExtension(filepath);
    }

    public static readonly HashSet<string> StandardFileExtensions = [".glb", ".gltf", ".obj", ".fbx", ".stl", ".ply"];

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        if (handle.Format.format == KnownFileFormats.Mesh) {
            throw new NotSupportedException("RE ENGINE mesh formats not yet supported");
        }

        // TODO should we keep and reuse an AssimpContext instance?
        using AssimpContext importer = new AssimpContext();
        var importedScene = importer.ImportFileFromStream(handle.Stream, PostProcessSteps.OptimizeMeshes|PostProcessSteps.OptimizeGraph|PostProcessSteps.Triangulate|PostProcessSteps.FlipUVs|PostProcessSteps.GenerateBoundingBoxes, Path.GetExtension(handle.Filepath));
        if (!importedScene.HasMeshes) {
            Logger.Error("No meshes found in file " + handle.Filepath);
            return null;
        }

        return new AssimpMeshResource(importedScene);
    }


    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        return false;
    }
}

public class AssimpMeshResource(Assimp.Scene importedScene) : IResourceFile
{
    public Assimp.Scene Scene { get; } = importedScene;

    public void WriteTo(string filepath)
    {
        if (PathUtils.ParseFileFormat(filepath).format == KnownFileFormats.Mesh) {
            throw new NotSupportedException("RE ENGINE mesh format export not yet supported");
        }

        using AssimpContext importer = new AssimpContext();

        var ext = Path.GetExtension(filepath);
        string? exportFormat = null;
        foreach (var fmt in importer.GetSupportedExportFormats()) {
            if (fmt.FileExtension == ext) {
                exportFormat = fmt.FormatId;
            }
        }
        if (exportFormat == null) {
            throw new NotImplementedException("Unsupported export format " + ext);
        }

        var scn = new Assimp.Scene();
        scn.Meshes.AddRange(Scene.Meshes);
        importer.ExportFile(scn, filepath, exportFormat);
    }
}