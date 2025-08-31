using System.Reflection.Metadata;
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
        return format.format == KnownFileFormats.Mesh || MeshViewer.IsSupportedFileExtension(filepath);
    }

    public static readonly HashSet<string> StandardFileExtensions = [".glb", ".gltf", ".obj", ".fbx", ".stl", ".ply"];

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        Assimp.Scene importedScene;
        if (handle.Format.format == KnownFileFormats.Mesh) {
            var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
            var file = new MeshFile(fileHandler);
            if (!file.Read()) return null;

            if (file.MeshBuffer?.Positions == null) {
                Logger.Error("Mesh has no vertices");
                return null;
            }

            importedScene = new Assimp.Scene();
            foreach (var name in file.MaterialNames) {
                var aiMat = new Material();
                aiMat.Name = name;
                importedScene.Materials.Add(aiMat);
            }

            foreach (var meshData in file.Meshes) {
                foreach (var mesh in meshData.LODs[0].MeshGroups) {
                    foreach (var sub in mesh.Submeshes) {
                        var aiMesh = new Mesh(PrimitiveType.Triangle);
                        aiMesh.MaterialIndex = sub.materialIndex;

                        aiMesh.Vertices.AddRange(sub.Positions);
                        aiMesh.BoundingBox = new BoundingBox(meshData.boundingBox.minpos, meshData.boundingBox.maxpos);
                        if (file.MeshBuffer.UV0 != null) {
                            var uvOut = aiMesh.TextureCoordinateChannels[0];
                            uvOut.EnsureCapacity(sub.UV0.Length);
                            foreach (var uv in sub.UV0) uvOut.Add(new System.Numerics.Vector3(uv.X, uv.Y, 0));
                        }
                        if (file.MeshBuffer.UV1.Length > 0) {
                            var uvOut = aiMesh.TextureCoordinateChannels[1];
                            uvOut.EnsureCapacity(sub.UV1.Length);
                            foreach (var uv in sub.UV1) uvOut.Add(new System.Numerics.Vector3(uv.X, uv.Y, 0));
                        }
                        if (file.MeshBuffer.Normals != null) {
                            aiMesh.Normals.AddRange(sub.Normals);
                        }
                        if (file.MeshBuffer.Tangents != null) {
                            aiMesh.Tangents.AddRange(sub.Tangents);
                        }
                        if (file.MeshBuffer.Colors.Length > 0) {
                            var colOut = aiMesh.VertexColorChannels[0];
                            colOut.EnsureCapacity(sub.Colors.Length);
                            foreach (var col in sub.Colors) colOut.Add(col.ToVector4());
                        }
                        var faces = sub.Indices.Length / 3;
                        aiMesh.Faces.EnsureCapacity(faces);
                        for (int i = 0; i < faces; ++i) {
                            var f = new Face();
                            f.Indices.Add(sub.Indices[i * 3 + 0]);
                            f.Indices.Add(sub.Indices[i * 3 + 1]);
                            f.Indices.Add(sub.Indices[i * 3 + 2]);
                            aiMesh.Faces.Add(f);
                        }

                        importedScene.Meshes.Add(aiMesh);
                    }
                }
            }

        } else {
            using AssimpContext importer = new AssimpContext();
            importedScene = importer.ImportFileFromStream(
                handle.Stream,
                PostProcessSteps.OptimizeMeshes |
                PostProcessSteps.Triangulate |
                PostProcessSteps.FlipUVs |
                PostProcessSteps.GenerateBoundingBoxes |
                PostProcessSteps.GenerateNormals,
                Path.GetExtension(handle.Filepath));
            if (!importedScene.HasMeshes) {
                Logger.Error("No meshes found in file " + handle.Filepath);
                return null;
            }
        }

        return new AssimpMeshResource(importedScene);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var fs = File.Create(outputPath);
        if (handle.Stream.CanSeek) handle.Stream.Seek(0, SeekOrigin.Begin);
        handle.Stream.CopyTo(fs);
        return true;
    }
}

public class AssimpMeshResource(Assimp.Scene importedScene) : IResourceFile
{
    public Assimp.Scene Scene { get; } = importedScene;

    public void WriteTo(string filepath)
    {
        using AssimpContext importer = new AssimpContext();

        var ext = Path.GetExtension(filepath);
        string? exportFormat = null;
        foreach (var fmt in importer.GetSupportedExportFormats()) {
            if (fmt.FileExtension == ext) {
                exportFormat = fmt.FormatId;
                break;
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