using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Assimp;
using Assimp.Configs;
using Assimp.Unmanaged;
using ContentEditor;
using ContentEditor.App;
using ReeLib;
using ReeLib.Mesh;
using ReeLib.via;

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

            return new AssimpMeshResource(name) {
                Scene = importedScene,
                GameVersion = workspace.Env.Config.Game.GameEnum,
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
}

public class AssimpMeshResource(string Name) : IResourceFile
{
    private Assimp.Scene? _scene;
    private MeshFile? _mesh;

    public GameName GameVersion = GameName.dd2;

    public Assimp.Scene Scene
    {
        get => _scene ??= ConvertMeshToAssimpScene(NativeMesh, Name);
        set => _scene = value;
    }

    public MeshFile NativeMesh
    {
        get => _mesh ??= (ImportMeshFromAssimp(_scene!, MeshFile.GetGameMeshVersions(GameVersion)[0]));
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

    private static MeshFile ImportMeshFromAssimp(Assimp.Scene scene, string exportConfig)
    {
        var mesh = new MeshFile(new FileHandler());
        var srcMeshes = scene.Meshes;
        var totalVertCount = srcMeshes.Sum(m => m.VertexCount);
        var totalFaceCount = srcMeshes.Sum(m => m.FaceCount);
        var totalTriCount = totalFaceCount * 3;
        var paddedTriCount = totalTriCount + srcMeshes.Count(m => (m.FaceCount * 3) % 2 != 0);

        var buffer = new MeshBuffer();
        var meshData = new MeshData(new MeshBuffer());
        mesh.Meshes.Add(meshData);
        mesh.MeshBuffer = buffer;

        buffer.Positions = new Vector3[totalVertCount];
        if (srcMeshes.All(m => m.HasNormals && m.HasTangentBasis)) {
            buffer.Normals = new Vector3[totalVertCount];
            buffer.Tangents = new Vector3[totalVertCount];
        }
        if (srcMeshes.All(m => m.HasTextureCoords(0))) buffer.UV0 = new Vector2[totalVertCount];
        if (srcMeshes.All(m => m.HasTextureCoords(1))) buffer.UV1 = new Vector2[totalVertCount];
        if (srcMeshes.All(m => m.Bones.Count > 0 && m.Bones.All(b => b.HasVertexWeights))) {
            buffer.Weights = new VertexBoneWeights[totalVertCount];
        }

        buffer.Faces = new ushort[paddedTriCount];

        var orderedMeshes = srcMeshes.OrderBy(m => (MeshLoader.GetMeshIndexFromName(m.Name), MeshLoader.GetMeshGroupFromName(m.Name), MeshLoader.GetSubMeshIndexFromName(m.Name)));

        foreach (var mat in scene.Materials) {
            if (string.IsNullOrEmpty(mat.Name)) continue;
            mesh.MaterialNames.Add(mat.Name);
        }

        var lod0Mesh = new MeshLOD(buffer);
        meshData.LODs.Add(lod0Mesh);
        int vertOffset = 0;
        int indicesOffset = 0;
        meshData.boundingBox = AABB.Combine(scene.Meshes.Select(m => new AABB(m.BoundingBox.Min, m.BoundingBox.Max)));
        // TODO: sphere bounds not fully accurate
        meshData.boundingSphere = new Sphere(meshData.boundingBox.Center, Math.Max(meshData.boundingBox.Size.X, Math.Max(meshData.boundingBox.Size.Y, meshData.boundingBox.Size.Z)) / 2);

        var boneIndexMap = new Dictionary<string, int>();
        var deformBones = new SortedList<int, MeshBone>();
        if (buffer.Weights.Length > 0) {
            var boneNames = srcMeshes.SelectMany(m => m.Bones.Select(b => b.Name)).ToHashSet();
            static void AddRecursiveBones(MeshFile file, NodeCollection children, HashSet<string> boneNames, MeshBone? parentBone)
            {
                foreach (var node in children) {
                    if (boneNames.Contains(node.Name)) {
                        file.BoneData ??= new();
                        var bone = new MeshBone() { name = node.Name, index = file.BoneData.Bones.Count, localTransform = Matrix4x4.Transpose(node.Transform) };
                        file.BoneData.Bones.Add(bone);
                        if (parentBone == null) {
                            bone.globalTransform = bone.localTransform;
                            bone.parentIndex = -1;
                            file.BoneData.RootBones.Add(bone);
                        } else {
                            bone.globalTransform = Matrix4x4.Multiply(bone.localTransform.ToSystem(), parentBone.globalTransform.ToSystem());
                            bone.Parent = parentBone;
                            bone.parentIndex = parentBone.index;
                            if (parentBone.Children.Count == 0) {
                                parentBone.childIndex = bone.index;
                            }
                            parentBone.Children.Add(bone);
                        }
                        bone.inverseGlobalTransform = Matrix4x4.Invert(bone.globalTransform.ToSystem(), out var inverse) ? inverse : Matrix4x4.Identity;
                        AddRecursiveBones(file, node.Children, boneNames, bone);
                    } else if (node.Children.Count > 0) {
                        // just in case there's some weird hierarchy shenanigans going on?
                        AddRecursiveBones(file, node.Children, boneNames, parentBone);
                    }
                }
            }
            AddRecursiveBones(mesh, scene.RootNode.Children, boneNames, null);
            boneIndexMap = mesh.BoneData!.Bones.ToDictionary(b => b.name, b => b.index);
        }

        foreach (var aiMesh in srcMeshes) {
            var groupIdx = MeshLoader.GetMeshGroupFromName(aiMesh.Name);
            var meshIdx = MeshLoader.GetMeshIndexFromName(aiMesh.Name);
            var subIdx = MeshLoader.GetSubMeshIndexFromName(aiMesh.Name);

            var vertCount = aiMesh.VertexCount;
            var faceCount = aiMesh.FaceCount;
            var indicesCount = faceCount * 3;

            // note: vert limit check shouldn't be needed here, we're letting assimp handling splitting automatically
            if (meshIdx > 0) throw new NotImplementedException($"Only one mesh per file is currently supported.");

            var group = lod0Mesh.MeshGroups.FirstOrDefault(grp => grp.groupId == groupIdx);
            if (group == null) {
                lod0Mesh.MeshGroups.Add(group = new MeshGroup(buffer));
                group.groupId = (byte)groupIdx;
            }

            group.vertexCount += vertCount;
            group.indicesCount += indicesCount;
            var newSub = new Submesh(buffer);
            newSub.facesIndexOffset = indicesOffset;
            newSub.vertsIndexOffset = vertOffset;
            newSub.vertCount = aiMesh.VertexCount;
            newSub.indicesCount = indicesCount;
            newSub.materialIndex = (ushort)aiMesh.MaterialIndex;

            meshData.totalMeshCount++;
            group.submeshCount++;
            group.Submeshes.Add(newSub);

            CollectionsMarshal.AsSpan(aiMesh.Vertices).CopyTo(buffer.Positions.AsSpan(vertOffset));

            if (buffer.Normals.Length > 0) {
                for (int i = 0; i < vertCount; ++i) {
                    buffer.Normals[vertOffset + i] = aiMesh.Normals[i];
                    buffer.Tangents[vertOffset + i] = aiMesh.Tangents[i];
                }
            }

            if (buffer.UV0.Length > 0) {
                var uv = aiMesh.TextureCoordinateChannels[0];
                for (int i = 0; i < vertCount; ++i) buffer.UV0[vertOffset + i] = new Vector2(uv[i].X, uv[i].Y);
            }

            if (buffer.UV1.Length > 0) {
                var uv = aiMesh.TextureCoordinateChannels[1];
                for (int i = 0; i < vertCount; ++i) buffer.UV1[vertOffset + i] = new Vector2(uv[i].X, uv[i].Y);
            }

            for (int i = 0; i < faceCount; ++i) {
                var face = aiMesh.Faces[i];
                // note: assimp should've forced triangulation already, therefore always assume 3 indices

                buffer.Faces[indicesOffset + i * 3 + 0] = (ushort)face.Indices[0];
                buffer.Faces[indicesOffset + i * 3 + 1] = (ushort)face.Indices[1];
                buffer.Faces[indicesOffset + i * 3 + 2] = (ushort)face.Indices[2];
            }

            if (buffer.Weights.Length > 0) {
                for (int i = 0; i < aiMesh.Bones.Count; i++) {
                    Bone? bone = aiMesh.Bones[i];
                    var boneIndex = boneIndexMap[bone.Name];
                    var hasWeight = false;
                    foreach (var entry in bone.VertexWeights) {
                        if (entry.Weight == 0) continue;

                        var outWeight = (buffer.Weights[vertOffset + entry.VertexID] ??= new(MeshFile.GetSerializerVersion(exportConfig)));
                        var weightIndex = Array.FindIndex(outWeight.boneWeights, bb => bb == 0);
                        if (weightIndex == -1) {
                            throw new Exception("Too many weights for bone " + bone.Name);
                        }
                        outWeight.boneIndices[weightIndex] = boneIndex;
                        outWeight.boneWeights[weightIndex] = entry.Weight;
                        hasWeight = true;
                    }
                    if (hasWeight) deformBones.TryAdd(boneIndex, mesh.BoneData!.Bones[boneIndex]);
                }

                var hasLooseVerts = Array.IndexOf(buffer.Weights, null, vertOffset, vertCount) != -1;
                if (hasLooseVerts) throw new Exception($"Found {buffer.Weights.AsSpan(vertOffset, vertCount).ToArray().Count(w => w == null)} unweighted vertices in imported mesh {aiMesh.Name} - this is not OK");

                foreach (var wee in buffer.Weights.AsSpan(vertOffset, vertCount)) {
                    // ensure normalized weights
                    wee.NormalizeWeights();
                }
            }

            if ((indicesCount % 2) != 0) indicesOffset++; // handle padding TODO test correctness
            vertOffset += vertCount;
            indicesOffset += indicesCount;
        }

        if (buffer.Weights.Length > 0) {
            int remapIndex = 0;
            foreach (var (remap, bone) in deformBones) {
                bone.remapIndex = remapIndex++;
                mesh.BoneData!.DeformBones.Add(bone);
            }

            foreach (var weight in buffer.Weights) {
                for (int i = 0; i < weight.boneIndices.Length; ++i) {
                    var index = weight.boneIndices[i];
                    if (deformBones.TryGetValue(index, out var remap)) {
                        weight.boneIndices[i] = remap.remapIndex;
                    }
                }
            }
        }

        return mesh;
    }

    private static Assimp.Scene ConvertMeshToAssimpScene(MeshFile file, string rootName)
    {
        // NOTE: every matrix needs to be transposed, assimp expects them transposed compared to default System.Numeric.Matrix4x4 for some shit ass reason
        // NOTE2: assimp currently forces vert deduplication for gltf export so we may lose some vertices (https://github.com/assimp/assimp/issues/6349)
        // TODO: export extra dummy nodes to ensure materials with no meshes (possibly used by LODs only) don't get dropped? (see dd2 ch20_000.mesh.240423143)
        // also: lods read and export?

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

        var meshes = new Dictionary<(int meshIndex, int meshGroup), Node>();
        int meshId = 0;
        foreach (var meshData in file.Meshes) {
            foreach (var mesh in meshData.LODs[0].MeshGroups) {
                int subId = 0;
                // separate meshes by mesh group so they don't get merged together
                if (!meshes.TryGetValue((meshId, mesh.groupId), out var meshNode)) {
                    meshes[(meshId, mesh.groupId)] = meshNode = new Node($"Group_{mesh.groupId.ToString(CultureInfo.InvariantCulture)}_mesh{meshId}", scene.RootNode);
                    scene.RootNode.Children.Add(meshNode);
                }

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

                    meshNode.MeshIndices.Add(scene.Meshes.Count);
                    scene.Meshes.Add(aiMesh);
                }
            }
            meshId++;
        }

        return scene;
    }
}