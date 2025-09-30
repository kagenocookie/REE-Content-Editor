using System.Globalization;
using System.Numerics;
using Assimp;
using ContentEditor;
using ContentEditor.App;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mesh;
using Silk.NET.Maths;

namespace ContentPatcher;

public partial class AssimpMeshResource : IResourceFile
{
    private static IEnumerable<Node> FlatNodes(Node node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(FlatNodes)) {
            yield return child;
        }
    }

    private static void AddMotToScene(Assimp.Scene scene, MotFile mot)
    {
        var anim = new Assimp.Animation();
        anim.Name = mot.Name;
        anim.TicksPerSecond = mot.Header.FrameRate;
        anim.DurationInTicks = mot.Header.endFrame;
        foreach (var clip in mot.BoneClips) {
            var header = clip.ClipHeader;
            var channel = new NodeAnimationChannel();
            channel.NodeName = header.boneName
                ?? header.OriginalName
                ?? mot.GetBoneByHash(header.boneHash)?.Name
                ?? FlatNodes(scene.RootNode).FirstOrDefault(n => MurMur3HashUtils.GetHash(n.Name) == header.boneHash)?.Name;
            if (channel.NodeName == null) {
                // not a known bone for our mesh - add placeholder bone nodes and hope they fit
                channel.NodeName = $"_hash{header.boneHash}";
                var motBone = mot.GetBoneByHash(header.boneHash);
                var rootNode = FlatNodes(scene.RootNode).FirstOrDefault(node => MurMur3HashUtils.GetHash(node.Name) == mot.RootBones.First().Header.boneHash);
                if (rootNode != null) {
                    Logger.Warn($"Animation {mot.Name} contains an unnamed bone {header.boneHash} that the mesh or the motlist file does not specify. It will get exported as placeholder 'hash{header.boneHash}' and may not be fully correct.");
                    rootNode.Children.Add(new Node(channel.NodeName, rootNode) {
                        Transform = Matrix4x4.Transpose(Transform.GetMatrixFromTransforms(
                            motBone?.Translation.ToGeneric() ?? Vector3D<float>.Zero,
                            motBone?.Quaternion.ToGeneric() ?? Quaternion<float>.Identity,
                            Vector3D<float>.One).ToSystem())
                    });
                    foreach (var mesh in scene.Meshes) {
                        if (mesh.Bones.Count == 0) continue;
                        mesh.Bones.Add(new Bone(channel.NodeName, Matrix4x4.Identity, []));
                    }
                }
            }
            if (clip.HasTranslation) {
                if (clip.Translation!.frameIndexes == null) {
                    if (clip.Translation.translations?.Length > 0) channel.PositionKeys.Add(new VectorKey(0, clip.Translation.translations[0]));
                } else {
                    for (int i = 0; i < clip.Translation!.frameIndexes.Length; ++i) {
                        channel.PositionKeys.Add(new VectorKey(clip.Translation!.frameIndexes[i], clip.Translation!.translations![i]));
                    }
                }
            }
            if (clip.HasRotation) {
                if (clip.Rotation!.frameIndexes == null) {
                    if (clip.Rotation.rotations?.Length > 0) channel.RotationKeys.Add(new QuaternionKey(0, clip.Rotation.rotations[0]));
                } else {
                    for (int i = 0; i < clip.Rotation!.frameIndexes!.Length; ++i) {
                        channel.RotationKeys.Add(new QuaternionKey(clip.Rotation!.frameIndexes![i], clip.Rotation!.rotations![i]));
                    }
                }
            }
            if (clip.HasScale) {
                if (clip.Scale!.frameIndexes == null) {
                    if (clip.Scale.translations?.Length > 0) channel.ScalingKeys.Add(new VectorKey(0, clip.Scale.translations[0]));
                } else {
                    for (int i = 0; i < clip.Scale!.frameIndexes!.Length; ++i) {
                        channel.ScalingKeys.Add(new VectorKey(clip.Scale!.frameIndexes![i], clip.Scale!.translations![i]));
                    }
                }
            }
            anim.NodeAnimationChannels.Add(channel);
        }
        scene.Animations.Add(anim);
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

                    // aiMesh.Vertices.AddRange(sub.Positions);
                    foreach (var pos in sub.Positions) aiMesh.Vertices.Add(pos);
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
