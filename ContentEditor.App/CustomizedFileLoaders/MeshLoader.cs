using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Assimp;
using ContentEditor;
using ContentEditor.App;
using ReeLib;
using ReeLib.Mesh;

namespace ContentPatcher;

public partial class MeshLoader : IFileLoader
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

            return new AssimpMeshResource(name) {
                NativeMesh = file,
            };
        } else {
            using AssimpContext importer = new AssimpContext();
            importedScene = importer.ImportFileFromStream(
                handle.Stream,
                PostProcessSteps.OptimizeMeshes |
                PostProcessSteps.Triangulate |
                PostProcessSteps.FlipUVs |
                PostProcessSteps.GenerateBoundingBoxes |
                PostProcessSteps.GenerateNormals |
                PostProcessSteps.GenerateUVCoords,
                Path.GetExtension(handle.Filepath));
            if (!importedScene.HasMeshes) {
                Logger.Error("No meshes found in file " + handle.Filepath);
                return null;
            }

            return new AssimpMeshResource(name) {
                Scene = importedScene,
            };
        }

    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var fs = File.Create(outputPath);
        if (handle.Stream.CanSeek) handle.Stream.Seek(0, SeekOrigin.Begin);
        handle.Stream.CopyTo(fs);
        return true;
    }

    [GeneratedRegex("Group_([\\d]+)")]
    private static partial Regex MeshNameRegex();

    public static int GetMeshGroupFromName(string meshName)
    {
        var match = MeshNameRegex().Match(meshName);
        if (match.Success) {
            return int.Parse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
        }
        return 0;
    }
}

public class AssimpMeshResource(string Name) : IResourceFile
{
    private Assimp.Scene? _scene;
    private MeshFile? _mesh;

    public Assimp.Scene Scene
    {
        get => _scene ??= (NativeMesh != null ? ConvertMeshToAssimpScene(NativeMesh, Name) : null!);
        set => _scene = value;
    }

    public MeshFile? NativeMesh
    {
        get => _mesh;
        set => _mesh = value;
    }

    public List<Material>? MaterialList
    {
        get {
            if (_scene != null) return _scene.Materials;
            if (_mesh != null) {
                return _mesh.MaterialNames.Select(name => new Material() { Name = name }).ToList();
            }
            return null;
        }
    }

    public bool HasNativeMesh => _mesh != null;
    public bool HasAssimpScene => _scene != null;

    public IEnumerable<int> GroupIDs =>
        _mesh?.Meshes.SelectMany(m => m.LODs[0].MeshGroups.Select(g => (int)g.groupId)).Distinct()
        ?? _scene?.Meshes.Select(m => string.IsNullOrEmpty(m.Name) ? 0 : MeshLoader.GetMeshGroupFromName(m.Name)).Distinct()
        ?? [];

    public int VertexCount => _mesh?.MeshBuffer?.Positions.Length
        ?? _scene?.Meshes.Sum(m => m.VertexCount)
        ?? -1;

    public int PolyCount => _mesh?.MeshBuffer?.Faces.Length
        ?? _scene?.Meshes.Sum(m => m.FaceCount)
        ?? -1;

    public int MaterialCount => _mesh?.MaterialNames.Count
        ?? _scene?.MaterialCount
        ?? -1;

    public int BoneCount => _mesh?.BoneData?.Bones.Count
        ?? _scene?.Meshes[0].BoneCount
        ?? -1;

    public int MeshCount => _mesh?.Meshes.Sum(mm => mm.totalMeshCount)
        ?? _scene?.MeshCount
        ?? -1;

    public void WriteTo(string filepath)
    {
        using AssimpContext importer = new AssimpContext();

        var ext = PathUtils.GetExtensionWithoutPeriod(filepath);
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

        var scene = Scene;
        importer.ExportFile(scene, filepath, exportFormat);
    }

    private static Assimp.Scene ConvertMeshToAssimpScene(MeshFile file, string rootName)
    {
        var scene = new Assimp.Scene();
        scene.RootNode = new Node(rootName);
        foreach (var name in file.MaterialNames) {
            var aiMat = new Material();
            aiMat.Name = name;
            scene.Materials.Add(aiMat);
        }

        var boneDict = new Dictionary<int, Node>();
        var bones = file.BoneData?.Bones;

        if (bones?.Count > 0 && file.MeshBuffer!.Weights.Length > 0) {
            // insert root bones first to ensure all parents exist
            foreach (var srcBone in bones) {
                if (srcBone.parentIndex == -1) {
                    var boneNode = new Node(srcBone.name, scene.RootNode);
                    boneDict[srcBone.index] = boneNode;
                    boneNode.Transform = Matrix4x4.Transpose(srcBone.localTransform.ToSystem());
                    scene.RootNode.Children.Add(boneNode);
                }
            }

            // insert bones by queue and requeue them if we don't have their parent yet
            var pendingBones = new Queue<MeshBone>(bones.Where(b => b.parentIndex != -1));
            while (pendingBones.TryDequeue(out var srcBone)) {
                if (!boneDict.TryGetValue(srcBone.parentIndex, out var parentBone)) {
                    pendingBones.Enqueue(srcBone);
                    continue;
                }
                var boneNode = new Node(srcBone.name, parentBone);
                boneDict[srcBone.index] = boneNode;
                boneNode.Transform = Matrix4x4.Transpose(srcBone.localTransform.ToSystem());
                parentBone.Children.Add(boneNode);
            }
        }

        int meshId = 0;
        foreach (var meshData in file.Meshes) {
            foreach (var mesh in meshData.LODs[0].MeshGroups) {
                int subId = 0;
                foreach (var sub in mesh.Submeshes) {
                    var aiMesh = new Mesh(PrimitiveType.Triangle);
                    aiMesh.MaterialIndex = sub.materialIndex;

                    aiMesh.Vertices.AddRange(sub.Positions);
                    aiMesh.BoundingBox = new BoundingBox(meshData.boundingBox.minpos, meshData.boundingBox.maxpos);
                    if (file.MeshBuffer!.UV0 != null) {
                        var uvOut = aiMesh.TextureCoordinateChannels[0];
                        uvOut.EnsureCapacity(sub.UV0.Length);
                        foreach (var uv in sub.UV0) uvOut.Add(new System.Numerics.Vector3(uv.X, uv.Y, 0));
                        aiMesh.UVComponentCount[0] = 2;
                    }
                    if (file.MeshBuffer.UV1.Length > 0) {
                        var uvOut = aiMesh.TextureCoordinateChannels[1];
                        uvOut.EnsureCapacity(sub.UV1.Length);
                        foreach (var uv in sub.UV1) uvOut.Add(new System.Numerics.Vector3(uv.X, uv.Y, 0));
                        aiMesh.UVComponentCount[1] = 2;
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
                    var weightedVerts = new HashSet<int>();
                    if (bones?.Count > 0 && file.MeshBuffer.Weights.Length > 0) {
                        // should we only be grabbing SkinBones here?
                        foreach (var srcBone in bones) {
                            var bone = new Bone();
                            bone.Name = srcBone.name;
                            bone.OffsetMatrix = Matrix4x4.Transpose(srcBone.inverseGlobalTransform.ToSystem());
                            aiMesh.Bones.Add(bone);
                        }

                        for (int vertId = 0; vertId < sub.Weights.Length; ++vertId) {
                            var vd = sub.Weights[vertId];
                            for (int i = 0; i < vd.boneIndices.Length; ++i) {
                                var weight = vd.boneWeights[i];
                                if (weight > 0) {
                                    var srcBone = file.BoneData!.DeformBones[vd.boneIndices[i]];
                                    var bone = aiMesh.Bones[srcBone.index];
                                    weightedVerts.Add(vertId);
                                    bone.VertexWeights.Add(new VertexWeight(vertId, weight));
                                }
                            }
                        }
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
                    aiMesh.Name = $"Group_{mesh.groupId.ToString(CultureInfo.InvariantCulture)}_mesh{meshId}_sub{subId++}";

                    scene.RootNode.MeshIndices.Add(scene.Meshes.Count);
                    scene.Meshes.Add(aiMesh);
                }
            }
            meshId++;
        }

        return scene;
    }
}