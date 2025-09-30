using System.Numerics;
using System.Runtime.InteropServices;
using Assimp;
using ContentEditor;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mesh;
using ReeLib.Mot;
using ReeLib.Motlist;
using ReeLib.via;

namespace ContentPatcher;

public partial class AssimpMeshResource : IResourceFile
{
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
                        bone.inverseGlobalTransform = Matrix4x4.Invert(bone.globalTransform.ToSystem(), out var inverse) ? inverse : throw new Exception("Failed to calculate inverse bone matrix " + bone.name);
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

    private MotlistFile ImportAnimationsFromAssimp(Assimp.Scene scene, GameName gameVersion)
    {
        workspace.TryGetFileExtensionVersion("motlist", out var version);
        var motlist = new MotlistFile(new FileHandler() { FileVersion = version });
        motlist.Header.version = (ReeLib.Motlist.MotlistVersion)version;
        motlist.Header.MotListName = Name;
        var motver = motlist.Header.version.GetMotVersion();
        var meta = scene.Metadata;

        // setup mot bone hierarchy
        var boneNames = scene.Animations.SelectMany(a => a.NodeAnimationChannels.Select(ann => ann.NodeName)).ToHashSet();
        static void AddRecursiveBones(List<MotBone> bones, NodeCollection children, HashSet<string> boneNames, MotBone? parentBone)
        {
            foreach (var node in children) {
                if (boneNames.Contains(node.Name)) {
                    var header = new BoneHeader() { boneName = node.Name, boneHash = MurMur3HashUtils.GetHash(node.Name), Index = bones.Count };
                    var bone = new MotBone(header);
                    var localMatrix = Matrix4x4.Transpose(node.Transform);
                    if (!Matrix4x4.Decompose(localMatrix, out _, out header.quaternion, out header.translation)) {
                        Logger.Error("Failed to decompose bone offset");
                    }
                    if (header.quaternion.W < 0) {
                        header.quaternion = Quaternion.Negate(header.quaternion);
                    }

                    bones.Add(bone);
                    bone.Parent = parentBone;
                    parentBone?.Children.Add(bone);
                    AddRecursiveBones(bones, node.Children, boneNames, bone);
                } else if (node.Children.Count > 0) {
                    // just in case there's some weird hierarchy shenanigans going on?
                    AddRecursiveBones(bones, node.Children, boneNames, parentBone);
                }
            }
        }
        List<MotBone> motBones = new();
        AddRecursiveBones(motBones, scene.RootNode.Children, boneNames, null);
        var rootBones = motBones.Where(b => b.Parent == null).ToList();
        List<BoneHeader> boneHeaders = motBones.Select(b => b.Header).ToList();

        foreach (var aiAnim in scene.Animations) {
            if (!aiAnim.HasNodeAnimations) continue;

            var mot = new MotFile(motlist.FileHandler);
            mot.Name = aiAnim.Name;
            mot.Header.version = motver;
            mot.Bones.AddRange(motBones);
            mot.RootBones.AddRange(rootBones);
            mot.BoneHeaders = boneHeaders;

            var sourceFps = aiAnim.TicksPerSecond;
            ushort targetFps = 60;
            if (sourceFps == 0) sourceFps = 60;

            var duration = aiAnim.DurationInTicks / sourceFps;
            mot.Header.frameCount = (float)Math.Floor(duration * targetFps);
            mot.Header.endFrame = mot.Header.frameCount;
            mot.Header.FrameRate = targetFps;
            motlist.MotFiles.Add(mot);
            var motIndex = new MotIndex(motlist.Header.version) { MotFile = mot, motNumber = (ushort)motlist.MotFiles.Count };
            motlist.Motions.Add(motIndex);

            var timeScale = targetFps / sourceFps;

            // TODO determine best compression types
            foreach (var channel in aiAnim.NodeAnimationChannels) {
                var clipHeader = new BoneClipHeader(motver);
                var clip = new BoneMotionClip(clipHeader);

                clipHeader.boneName = channel.NodeName;
                clipHeader.boneHash = MurMur3HashUtils.GetHash(channel.NodeName);
                var bone = mot.GetBoneByHash(clipHeader.boneHash);
                clipHeader.boneIndex = (ushort)(bone?.Index ?? 0); // would we need these to be remap index?
                if (channel.HasPositionKeys && (bone == null || channel.PositionKeys.Any(k => Vector3.DistanceSquared(k.Value, bone.Translation) > 0.000001f))) {
                    clipHeader.trackFlags |= TrackFlag.Translation;
                    var track = clip.Translation = new Track(motver, TrackValueType.Vector3);
                    track.maxFrame = (float)(channel.PositionKeys.Last().Time * timeScale);
                    track.frameRate = targetFps;
                    track.frameIndexes = new int[channel.PositionKeyCount];
                    track.translations = new Vector3[channel.PositionKeyCount];
                    track.keyCount = channel.PositionKeyCount;
                    for (int i = 0; i < channel.PositionKeyCount; ++i) {
                        var key = channel.PositionKeys[i];
                        track.frameIndexes[i] = (int)Math.Round(key.Time * timeScale);
                        track.translations[i] = key.Value;
                    }
                }

                if (channel.HasScalingKeys && (bone == null || channel.ScalingKeys.Any(k => Vector3.DistanceSquared(k.Value, Vector3.One) > 0.000001f))) {
                    clipHeader.trackFlags |= TrackFlag.Scale;
                    var track = clip.Scale = new Track(motver, TrackValueType.Vector3);
                    track.maxFrame = (float)(channel.ScalingKeys.Last().Time * timeScale);
                    track.frameRate = targetFps;
                    track.frameIndexes = new int[channel.ScalingKeyCount];
                    track.translations = new Vector3[channel.ScalingKeyCount];
                    track.keyCount = channel.ScalingKeyCount;
                    for (int i = 0; i < channel.ScalingKeyCount; ++i) {
                        var key = channel.ScalingKeys[i];
                        track.frameIndexes[i] = (int)Math.Round(key.Time * timeScale);
                        track.translations[i] = key.Value;
                    }
                }

                if (channel.HasRotationKeys && (bone == null || channel.RotationKeys.Any(k => k.Value != bone.Quaternion))) {
                    clipHeader.trackFlags |= TrackFlag.Rotation;
                    var track = clip.Rotation = new Track(motver, TrackValueType.Quaternion);
                    track.maxFrame = (float)(channel.RotationKeys.Last().Time * timeScale);
                    track.frameRate = targetFps;
                    track.frameIndexes = new int[channel.RotationKeyCount];
                    track.rotations = new Quaternion[channel.RotationKeyCount];
                    track.keyCount = channel.RotationKeyCount;
                    for (int i = 0; i < channel.RotationKeyCount; ++i) {
                        var key = channel.RotationKeys[i];
                        track.frameIndexes[i] = (int)Math.Round(key.Time * timeScale);
                        var quat = key.Value;
                        if (quat.W < 0) {
                            quat = Quaternion.Negate(quat);
                        }
                        track.rotations[i] = quat;
                    }
                }

                if (clip.ClipHeader.trackFlags != 0) {
                    mot.BoneClips.Add(clip);
                }
            }
        }
        return motlist;
    }
}
