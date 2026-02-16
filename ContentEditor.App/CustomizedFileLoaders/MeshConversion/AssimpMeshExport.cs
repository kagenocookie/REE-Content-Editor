using System.Globalization;
using System.Numerics;
using Assimp;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mesh;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App.FileLoaders;

public partial class CommonMeshResource : IResourceFile
{
    private static IEnumerable<Node> FlatNodes(Node node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(FlatNodes)) {
            yield return child;
        }
    }

    private static void AddMotlistToScene(Assimp.Scene scene, MotlistFile motlist, string exportFormat)
    {
        foreach (var file in motlist.MotFiles) {
            if (file is MotFile mot) {
                AddMotToScene(scene, mot, exportFormat);
            }
        }
    }

    private static void AddMotToScene(Assimp.Scene scene, MotFile mot, string exportFormat)
    {
        var anim = new Assimp.Animation();
        anim.Name = mot.Name;
        anim.TicksPerSecond = mot.Header.FrameRate;
        anim.DurationInTicks = mot.Header.endFrame;
        // fbx is stupid and we need to do this for the keyframes to read correctly
        var timescale = exportFormat == "fbx" ? mot.Header.FrameRate / 24f : 1;

        var nodeDict = FlatNodes(scene.RootNode)
            .ToDictionary(n => n.Name.StartsWith("_hash") && uint.TryParse(n.Name.AsSpan("_hash".Length), out var hash) ? hash : MurMur3HashUtils.GetHash(n.Name));
        foreach (var clip in mot.BoneClips) {
            var header = clip.ClipHeader;
            var channel = new NodeAnimationChannel();
            var boneNode = nodeDict.GetValueOrDefault(header.boneHash);
            channel.NodeName = header.boneName
                ?? header.OriginalName
                ?? mot.GetBoneByHash(header.boneHash)?.boneName
                ?? boneNode?.Name;

            if (boneNode == null || channel.NodeName == null) {
                // not a known bone for our mesh - add placeholder bone nodes and hope they fit
                boneNode ??= new Node(header.boneName ?? $"_hash{header.boneHash}");
                channel.NodeName = boneNode.Name;

                var motBone = mot.GetBoneByHash(header.boneHash);
                Node? targetNode = null;
                if (motBone != null) {
                    // try and match the closest parent bone
                    var parent = motBone;
                    while (parent != null) {
                        targetNode = nodeDict.GetValueOrDefault(parent.boneHash);
                        if (targetNode != null) break;
                        parent = parent.Parent;
                    }
                }

                var rootNode = targetNode
                    ?? nodeDict.GetValueOrDefault(mot.RootBones.First().boneHash)
                    ?? scene.RootNode.Children.FirstOrDefault(n => n.Name.Equals("root", StringComparison.InvariantCultureIgnoreCase));
                if (rootNode == null) {
                    Logger.Error($"Animation {mot.Name} contains an unnamed bone {header.boneHash} and no viable root bone was not found.");
                    continue;
                }

                Logger.Warn($"Animation {mot.Name} contains an unnamed bone {header.boneHash} that the mesh or the motlist file does not specify. It will get exported as placeholder 'hash{header.boneHash}' and may not be fully correct.");
                rootNode.Children.Add(new Node(channel.NodeName, rootNode) {
                    Transform = Matrix4x4.Transpose(Transform.GetMatrixFromTransforms(
                        motBone?.translation ?? Vector3.Zero,
                        motBone?.quaternion ?? Quaternion.Identity,
                        Vector3.One))
                });
                foreach (var mesh in scene.Meshes) {
                    if (mesh.Bones.Count == 0) continue;
                    mesh.Bones.Add(new Bone(channel.NodeName, Matrix4x4.Identity, []));
                }

                continue;
            }
            if (clip.HasTranslation) {
                if (clip.Translation!.frameIndexes == null) {
                    if (clip.Translation.translations?.Length > 0) channel.PositionKeys.Add(new VectorKey(0, clip.Translation.translations[0]));
                } else {
                    for (int i = 0; i < clip.Translation!.frameIndexes.Length; ++i) {
                        channel.PositionKeys.Add(new VectorKey(clip.Translation!.frameIndexes[i] * timescale, clip.Translation!.translations![i]));
                    }
                }
            } else {
                // some blender fbx importer versions don't work unless we also add at least one position key to everything
                // unsure if the assimp exporter does something weird or blender's importer being bad
                var rest = mot.GetBoneByHash(header.boneHash)?.translation ?? new Vector3(boneNode.Transform.M14, boneNode.Transform.M24, boneNode.Transform.M34);
                channel.PositionKeys.Add(new VectorKey(0, rest));
            }
            if (clip.HasRotation) {
                if (clip.Rotation!.frameIndexes == null) {
                    if (clip.Rotation.rotations?.Length > 0) channel.RotationKeys.Add(new QuaternionKey(0, clip.Rotation.rotations[0]));
                } else {
                    for (int i = 0; i < clip.Rotation!.frameIndexes!.Length; ++i) {
                        channel.RotationKeys.Add(new QuaternionKey(clip.Rotation!.frameIndexes![i] * timescale, clip.Rotation!.rotations![i]));
                    }
                }
            } else {
                // some blender fbx importer versions don't work unless we also add at least one rotation key to everything
                // unsure if the assimp exporter does something weird or blender's importer being bad
                var rest = mot.GetBoneByHash(header.boneHash)?.quaternion ?? Quaternion.CreateFromRotationMatrix(Matrix4x4.Transpose(boneNode.Transform));
                channel.RotationKeys.Add(new QuaternionKey(0, rest));
            }
            if (clip.HasScale) {
                if (clip.Scale!.frameIndexes == null) {
                    if (clip.Scale.translations?.Length > 0) channel.ScalingKeys.Add(new VectorKey(0, clip.Scale.translations[0]));
                } else {
                    for (int i = 0; i < clip.Scale!.frameIndexes!.Length; ++i) {
                        channel.ScalingKeys.Add(new VectorKey(clip.Scale!.frameIndexes![i] * timescale, clip.Scale!.translations![i]));
                    }
                }
            }
            anim.NodeAnimationChannels.Add(channel);
        }
        scene.Animations.Add(anim);
    }

    private static Assimp.Scene ConvertMeshToAssimpScene(MeshFile file, string rootName, bool isGltf, bool includeAllLods, bool includeShadows, bool includeOcclusion)
    {
        // NOTE: every matrix needs to be transposed, assimp expects them transposed compared to default System.Numeric.Matrix4x4 for some shit ass reason
        // NOTE2: assimp currently forces vert deduplication for gltf export so we may lose some vertices (https://github.com/assimp/assimp/issues/6349)
        // NOTE3: weights > 4 will get get lost for gltf because we can't tell it to write more weights (AI_CONFIG_EXPORT_GLTF_UNLIMITED_SKINNING_BONES_PER_VERTEX)
        // we'd either need access to assim's Exporter class directly, or have the ExportFile method modified on the assimp side
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

        var includeShapeKeys = false;
        if (bones?.Count > 0 && file.MeshBuffer!.Weights.Length > 0) {
            // insert root bones first to ensure all parents exist
            Node boneRoot = scene.RootNode;
            if (file.MeshBuffer.ShapeKeyWeights.Length > 0) {
                if (isGltf) {
                    Logger.Warn($"GLTF exporter does not support enough bones to include shape keys. Mesh will not behave correctly when re-imported. Consider using a different file format.");
                } else {
                    includeShapeKeys = true;
                }
            }
            foreach (var srcBone in bones) {
                if (srcBone.parentIndex == -1) {
                    var boneNode = new Node(srcBone.name, boneRoot);
                    boneDict[srcBone.index] = boneNode;
                    boneNode.Transform = Matrix4x4.Transpose(srcBone.localTransform.ToSystem());
                    boneRoot.Children.Add(boneNode);
                    if (srcBone.useSecondaryWeight) {
                        boneNode.Children.Add(new Node(SecondaryWeightDummyBonePrefix + srcBone.name, boneNode));
                    }
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
                if (srcBone.useSecondaryWeight) {
                    boneNode.Children.Add(new Node(SecondaryWeightDummyBonePrefix + srcBone.name, boneNode));
                }
                parentBone.Children.Add(boneNode);
            }

            if (includeShapeKeys) {
                if (isGltf) {
                    Logger.Warn($"GLTF exporter does not support enough bones to include shape keys. Mesh will not behave correctly when re-imported. Consider using a different file format.");
                } else {
                    // add shape key specific bone nodes
                    var boneNames = bones.Select(b => b.name).ToHashSet();
                    var deformBones = file.BoneData!.DeformBones.Select(b => b.name).ToHashSet();
                    static void RecursiveDuplicateShapeBones(Node parent, HashSet<string> boneNames, HashSet<string> deformBones)
                    {
                        foreach (var child in parent.Children.ToArray()) {
                            if (boneNames.Contains(child.Name)) {
                                if (deformBones.Contains(child.Name)) {
                                    var shapeChild = new Node() { Name = ShapekeyPrefix + child.Name };
                                    child.Children.Add(shapeChild);
                                    RecursiveDuplicateShapeBones(child, boneNames, deformBones);
                                } else {
                                    RecursiveDuplicateShapeBones(child, boneNames, deformBones);
                                }
                            }
                        }
                    }

                    RecursiveDuplicateShapeBones(boneRoot, boneNames, deformBones);
                }
            }
        }

        if (file.MeshData != null) {
            for (int i = 0; i < file.MeshData.LODs.Count; i++) {
                var lod = file.MeshData.LODs[i];
                if (i == 0) {
                    ExportLod(file, isGltf, scene, bones, includeShapeKeys, lod, includeAllLods ? "lod0_" : "");
                    if (!includeAllLods) break;
                } else {
                    ExportLod(file, isGltf, scene, bones, includeShapeKeys, lod, $"lod{i}_");
                }
            }
        }
        if (includeShadows && file.ShadowMesh != null) {
            for (int i = 0; i < file.ShadowMesh.LODs.Count; i++) {
                var lod = file.ShadowMesh.LODs[i];
                ExportLod(file, isGltf, scene, bones, includeShapeKeys, lod, $"shadow_lod{i}_");
            }
        }
        if (includeOcclusion && file.OccluderMesh != null) {
            if (scene.MaterialCount == 0) {
                scene.Materials.Add(new Material() { Name = "default" });
            }
            ExportLod(file, isGltf, scene, bones, includeShapeKeys, file.OccluderMesh, $"occ_");
        }

        return scene;
    }

    private static void ExportLod(MeshFile file, bool isGltf, Assimp.Scene scene, List<MeshBone>? bones, bool includeShapeKeys, MeshLOD lod, string namePrefix)
    {
        var bounds = file.MeshData?.boundingBox ?? new AABB();
        foreach (var mesh in lod.MeshGroups) {
            int subId = 0;
            foreach (var sub in mesh.Submeshes) {
                var aiMesh = new Mesh(PrimitiveType.Triangle);
                aiMesh.MaterialIndex = sub.materialIndex;


                aiMesh.Vertices.AddRange(sub.Positions);
                aiMesh.BoundingBox = new BoundingBox(bounds.minpos, bounds.maxpos);
                if (sub.Buffer.UV0.Length > 0) {
                    var uvOut = aiMesh.TextureCoordinateChannels[0];
                    uvOut.EnsureCapacity(sub.UV0.Length);
                    foreach (var uv in sub.UV0) uvOut.Add(new System.Numerics.Vector3(uv.X, 1 - uv.Y, 0));
                    aiMesh.UVComponentCount[0] = 2;
                }
                if (sub.Buffer.UV1.Length > 0) {
                    var uvOut = aiMesh.TextureCoordinateChannels[1];
                    uvOut.EnsureCapacity(sub.UV1.Length);
                    foreach (var uv in sub.UV1) uvOut.Add(new System.Numerics.Vector3(uv.X, 1 - uv.Y, 0));
                    aiMesh.UVComponentCount[1] = 2;
                }
                if (sub.Buffer.Normals.Length > 0) {
                    aiMesh.Normals.AddRange(sub.Normals);
                }
                if (sub.Buffer.Tangents.Length > 0) {
                    aiMesh.Tangents.AddRange(sub.Tangents);
                    for (int i = 0; i < sub.BiTangents.Length; ++i) {
                        aiMesh.BiTangents.Add(sub.GetBiTangent(i));
                    }
                }
                if (sub.Buffer.Colors.Length > 0) {
                    var colOut = aiMesh.VertexColorChannels[0];
                    colOut.EnsureCapacity(sub.Colors.Length);
                    foreach (var col in sub.Colors) colOut.Add(col.ToVector4());
                }
                if (bones?.Count > 0 && sub.Buffer.Weights.Length > 0) {
                    foreach (var srcBone in bones) {
                        var bone = new Bone();
                        bone.Name = srcBone.name;
                        bone.OffsetMatrix = Matrix4x4.Transpose(srcBone.inverseGlobalTransform.ToSystem());
                        aiMesh.Bones.Add(bone);
                    }
                    foreach (var srcBone in bones) {
                        if (srcBone.useSecondaryWeight) {
                            aiMesh.Bones.Add(new Bone() { Name = SecondaryWeightDummyBonePrefix + srcBone.name, OffsetMatrix = Matrix4x4.Identity });
                        }
                    }

                    for (int vertId = 0; vertId < sub.Weights.Length; ++vertId) {
                        var vd = sub.Weights[vertId];
                        for (int i = 0; i < vd.boneIndices.Length; ++i) {
                            var weight = vd.boneWeights[i];
                            if (weight > 0) {
                                var srcBone = file.BoneData!.DeformBones.Count == 0
                                    ? file.BoneData.RootBones[0]
                                    : file.BoneData.DeformBones[vd.boneIndices[i]];
                                var bone = aiMesh.Bones[srcBone.index];
                                bone.VertexWeights.Add(new VertexWeight(vertId, weight));
                                if (isGltf && i > 4) {
                                    isGltf = false;
                                    Logger.Warn($"GLTF exporter does not support more than 4 vertex bone weights. Mesh will not behave correctly when re-imported. Consider using a different file format.");
                                }
                            }
                        }
                    }

                    if (sub.Buffer.ExtraWeights != null) {
                        for (int vertId = 0; vertId < sub.ExtraWeights.Length; ++vertId) {
                            var vd = sub.ExtraWeights[vertId];
                            for (int i = 0; i < vd.boneIndices.Length; ++i) {
                                var weight = vd.boneWeights[i];
                                if (weight > 0) {
                                    var srcBone = file.BoneData!.DeformBones[vd.boneIndices[i]];
                                    var bone = aiMesh.Bones[srcBone.index];
                                    bone.VertexWeights.Add(new VertexWeight(vertId, weight));
                                }
                            }
                        }
                    }

                    if (includeShapeKeys) {
                        var dict = new Dictionary<int, Bone>();
                        foreach (var bone in file.BoneData!.DeformBones) {
                            var attach = dict[bone.remapIndex] = new Bone() { Name = ShapekeyPrefix + bone.name };
                            aiMesh.Bones.Add(attach);
                        }
                        for (int vertId = 0; vertId < sub.ShapeKeyWeights.Length; ++vertId) {
                            var vd = sub.ShapeKeyWeights[vertId];
                            for (int i = 0; i < vd.boneIndices.Length; ++i) {
                                var weight = vd.boneWeights[i];
                                if (weight > 0) {
                                    var bone = dict[vd.boneIndices[i]];
                                    bone.VertexWeights.Add(new VertexWeight(vertId, weight));
                                }
                            }
                        }
                    }

                    // blend shape export disabled for now, crashes - probably missing something stupid

                    // if (file.BlendShapes != null && file.BlendShapes.Shapes.Count > lod)
                    // {
                    //     var shape = file.BlendShapes.Shapes[lod];
                    //     // aiMesh.MorphMethod = MeshMorphingMethod.VertexBlend;
                    //     // var attach = new MeshAnimationAttachment() { Name = shape };

                    //     for (int i = 0; i < shape.Targets.Count; i++) {
                    //         var target = shape.Targets[i];
                    //         var attach = new MeshAnimationAttachment() { Name = target.name };
                    //         aiMesh.MeshAnimationAttachments.Add(attach);
                    //         attach.Vertices.AddRange(sub.Positions);
                    //         attach.Normals.AddRange(sub.Normals);
                    //         attach.Weight = 1;

                    //         foreach (var blendSub in target.Submeshes) {
                    //             var span = sub.GetBlendShapeRange(blendSub);
                    //             span.Span.CopyTo(CollectionsMarshal.AsSpan(attach.Vertices).Slice(span.StartIndex));
                    //         }
                    //     }
                    // }
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

                // each submesh needs to have a unique node so they don't get merged together
                var meshNode = new Node($"{namePrefix}Group_{mesh.groupId.ToString(CultureInfo.InvariantCulture)}_sub{subId++}", scene.RootNode);
                scene.RootNode.Children.Add(meshNode);
                aiMesh.Name = meshNode.Name;
                meshNode.MeshIndices.Add(scene.Meshes.Count);
                scene.Meshes.Add(aiMesh);
            }
        }
    }
}
