using System.Globalization;
using System.Numerics;
using Assimp;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mesh;
using ReeLib.via;

namespace ContentEditor.App.FileLoaders;

public partial class CommonMeshResource : IResourceFile
{
    internal sealed class ExportContext
    {
        public Assimp.Scene scene = new Assimp.Scene();

        public bool IsGltf => format == "glb";

        public bool gltfWarned = false;

        public string format = "";
        public bool includeAllLods;
        public bool includeShadows;
        public bool includeOcclusion;
        public bool includeShapeKeys;
        public FbxSkelFile? skeleton;
        public Dictionary<string, (Node node, bool? deforming, Matrix4x4 inverseTransform)> bonesLookup = new();

        public Node? secondaryWeightNodeContainer;
        public Dictionary<string, Node> secondaryWeightBones = new();

        public bool writeWeight2FlagAsBones = AppConfig.Settings.Import.ExportSecondaryWeightAsBones;

        public void AddWeight2Bone(string name)
        {
            if (!bonesLookup.TryGetValue(name, out var boneData)) {
                return;
            }

            if (writeWeight2FlagAsBones) {
                boneData.node.Children.Add(secondaryWeightBones[name] = new Node(SecondaryWeightDummyBonePrefix + name, boneData.node));
            } else {
                if (secondaryWeightNodeContainer == null) {
                    secondaryWeightNodeContainer = new Node(SecondaryWeightDummyBonePrefix, scene.RootNode);
                    scene.RootNode.Children.Add(secondaryWeightNodeContainer);
                }
                secondaryWeightNodeContainer.Children.Add(secondaryWeightBones[name] = new Node(SecondaryWeightDummyBonePrefix + name, secondaryWeightNodeContainer));
            }
        }
    }

    private static float GetExportScale(string format) {
        if (format != "fbx") return 1;

        var scale = AppConfig.Settings.Import.ExportScale;
        if (scale <= 0) scale = 1;
        return scale;
    }

    private static Matrix4x4 GetScaledMatrix(Matrix4x4 mat, float scale)
    {
        Matrix4x4.Decompose(mat, out var s, out var r, out var t);
        return Transform.GetMatrixFromTransforms(t * scale, r, s);
    }

    private static IEnumerable<Node> FlatNodes(Node node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(FlatNodes)) {
            yield return child;
        }
    }

    private static Dictionary<string, Node> FlatNodesDict(Node node)
    {
        var existingNodes = FlatNodes(node);
        var nodeDict = new Dictionary<string, Node>();
        foreach (var ext in existingNodes) nodeDict.TryAdd(ext.Name, ext);
        return nodeDict;
    }

    private static void AddMotToScene(Assimp.Scene scene, MotFile mot, string exportFormat)
    {
        var isFbx = exportFormat == "fbx";
        var scale = GetExportScale(exportFormat);

        var anim = new Assimp.Animation();
        anim.Name = mot.Name;
        anim.TicksPerSecond = mot.Header.FrameRate;
        anim.DurationInTicks = mot.Header.endFrame;
        // fbx is stupid and we need to do this for the keyframes to read correctly
        var timescale = isFbx ? mot.Header.FrameRate / 24f : 1;

        var nodeDict = new Dictionary<uint, Node>();
        foreach (var node in FlatNodes(scene.RootNode)) {
            if (!node.Name.StartsWith("_hash") || uint.TryParse(node.Name.AsSpan("_hash".Length), out var hash)) {
                hash = MurMur3HashUtils.GetHash(node.Name);
            }
            nodeDict.TryAdd(hash, node);
        }

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
                    Transform = Matrix4x4.Transpose(GetScaledMatrix(Transform.GetMatrixFromTransforms(
                        (motBone?.translation ?? Vector3.Zero),
                        motBone?.quaternion ?? Quaternion.Identity,
                        Vector3.One), scale))
                });
                foreach (var mesh in scene.Meshes) {
                    if (mesh.Bones.Count == 0) continue;
                    mesh.Bones.Add(new Bone(channel.NodeName, Matrix4x4.Identity, []));
                }

                continue;
            }
            if (clip.HasTranslation) {
                if (clip.Translation!.frameIndexes == null) {
                    if (clip.Translation.translations?.Length > 0) channel.PositionKeys.Add(new VectorKey(0, clip.Translation.translations[0] * scale));
                } else {
                    for (int i = 0; i < clip.Translation!.frameIndexes.Length; ++i) {
                        channel.PositionKeys.Add(new VectorKey(clip.Translation!.frameIndexes[i] * timescale, clip.Translation!.translations![i] * scale));
                    }
                }
            } else {
                // some blender fbx importer versions don't work unless we also add at least one position key to everything
                // unsure if the assimp exporter does something weird or blender's importer being bad
                var rest = ((mot.GetBoneByHash(header.boneHash)?.translation * scale) ?? new Vector3(boneNode.Transform.M14, boneNode.Transform.M24, boneNode.Transform.M34));
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

    internal static void PrepareSkeleton(ExportContext context, MeshFile mesh)
    {
        var scene = context.scene;
        // var existingNodes = FlatNodes(scene.RootNode);
        var scale = GetExportScale(context.format);

        Node boneRoot = context.scene.RootNode;
        if (context.skeleton != null && context.bonesLookup.Count == 0) {
            foreach (var refBone in context.skeleton.Bones) {
                Node? parent = null;
                if (refBone.parentIndex != -1) {
                    var parentRef = context.skeleton.Bones[refBone.parentIndex];
                    parent = context.bonesLookup.GetValueOrDefault(parentRef.name).node;
                    if (parent == null) {
                        throw new NotImplementedException("Unordered ref skel bones currently not supported");
                    }
                }
                var node = new Node(refBone.name, parent ?? boneRoot);
                (parent ?? boneRoot).Children.Add(node);

                var transform = Transform.GetMatrixFromTransforms(refBone.position, refBone.rotation, refBone.scale);
                node.Transform = Matrix4x4.Transpose(GetScaledMatrix(transform, scale));

                Matrix4x4.Invert(transform, out var inverseGlobal);
                if (parent != null) {
                    inverseGlobal = inverseGlobal * context.bonesLookup[parent.Name].inverseTransform;
                }
                context.bonesLookup[refBone.name] = (node, null, inverseGlobal);
            }
        }

        if (mesh.BoneData == null) return;

        foreach (var srcBone in mesh.BoneData.Bones.Where(b => b.parentIndex == -1)) {
            if (context.bonesLookup.TryGetValue(srcBone.name, out var boneData)) {
                if (srcBone.IsDeformBone && boneData.deforming != true) {
                    context.bonesLookup[srcBone.name] = (boneData.node, true, boneData.inverseTransform);
                }
                continue;
            }

            var boneNode = new Node(srcBone.name, boneRoot);
            boneNode.Transform = Matrix4x4.Transpose(GetScaledMatrix(srcBone.localTransform.ToSystem(), scale));
            boneRoot.Children.Add(boneNode);
            context.bonesLookup[srcBone.name] = (boneNode, srcBone.IsDeformBone, srcBone.inverseGlobalTransform.ToSystem());
            if (srcBone.useSecondaryWeight) {
                context.AddWeight2Bone(srcBone.name);
            }
        }

        // insert bones by queue and requeue them if we don't have their parent yet
        var pendingBones = new Queue<MeshBone>(mesh.BoneData.Bones.Where(b => b.parentIndex != -1));
        while (pendingBones.TryDequeue(out var srcBone)) {
            if (context.bonesLookup.TryGetValue(srcBone.name, out var boneData)) {
                if (srcBone.IsDeformBone && boneData.deforming != true) {
                    context.bonesLookup[srcBone.name] = (boneData.node, true, boneData.inverseTransform);
                }
                continue;
            }

            if (!context.bonesLookup.TryGetValue(srcBone.Parent!.name, out var parent)) {
                pendingBones.Enqueue(srcBone);
                continue;
            }

            var boneNode = new Node(srcBone.name, parent.node);
            boneNode.Transform = Matrix4x4.Transpose(GetScaledMatrix(srcBone.localTransform.ToSystem(), scale));
            parent.node.Children.Add(boneNode);
            context.bonesLookup[srcBone.name] = (boneNode, srcBone.IsDeformBone, srcBone.inverseGlobalTransform.ToSystem());
            if (srcBone.useSecondaryWeight) {
                context.AddWeight2Bone(srcBone.name);
            }
        }

        if (mesh.MeshBuffer?.ShapeKeyWeights.Length > 0) {
            if (context.IsGltf) {
                context.includeShapeKeys = false;
                if (!context.gltfWarned) {
                    context.gltfWarned = true;
                    Logger.Warn($"GLTF exporter does not support enough bones to include shape keys. Mesh will not behave correctly when re-imported. Consider using a different file format.");
                }
            } else {
                context.includeShapeKeys = true;

                // add shape key specific bone nodes
                foreach (var (name, data) in context.bonesLookup) {
                    var shapeKeyName = ShapekeyPrefix + data.node.Name;
                    if (data.deforming != true || data.node.Children.Any(c => c.Name == shapeKeyName)) continue;

                    data.node.Children.Add(new Node() { Name = shapeKeyName });
                }
            }
        }
    }

    internal static ExportContext AddMeshToScene(ExportContext context, MeshFile file, string rootName, Matrix4x4 transform = default)
    {
        // NOTE: every matrix needs to be transposed, assimp expects them transposed compared to default System.Numeric.Matrix4x4 for some shit ass reason
        // NOTE2: assimp currently forces vert deduplication for gltf export so we may lose some vertices (https://github.com/assimp/assimp/issues/6349)
        // NOTE3: weights > 4 will get get lost for gltf because we can't tell it to write more weights (AI_CONFIG_EXPORT_GLTF_UNLIMITED_SKINNING_BONES_PER_VERTEX)
        // we'd either need access to assimp's Exporter class directly, or have the ExportFile method modified on the assimp side

        context.scene.RootNode ??= new Node(rootName);
        if (file.BoneData?.Bones.Count > 0 && context.bonesLookup.Count == 0) {
            PrepareSkeleton(context, file);
        }

        if (transform == default) transform = Matrix4x4.Identity;

        foreach (var name in file.MaterialNames) {
            if (!context.scene.Materials.Any(mat => mat.Name == name)) {
                context.scene.Materials.Add(new Material() { Name = name });
            }
        }

        if (file.MeshData != null) {
            for (int i = 0; i < file.MeshData.LODs.Count; i++) {
                var lod = file.MeshData.LODs[i];
                if (i == 0) {
                    ExportLod(file, context, lod, context.includeAllLods ? $"{rootName}_lod0_" : $"{rootName}_", transform);
                    if (!context.includeAllLods) break;
                } else {
                    ExportLod(file, context, lod, $"{rootName}_lod{i}_", transform);
                }
            }
        }
        if (context.includeShadows && file.ShadowMesh != null) {
            for (int i = 0; i < file.ShadowMesh.LODs.Count; i++) {
                var lod = file.ShadowMesh.LODs[i];
                ExportLod(file, context, lod, $"{rootName}_shadow_lod{i}_", transform);
            }
        }
        if (context.includeOcclusion && file.OccluderMesh != null) {
            if (context.scene.MaterialCount == 0) {
                context.scene.Materials.Add(new Material() { Name = "default" });
            }
            ExportLod(file, context, file.OccluderMesh, $"{rootName}_occ_", transform);
        }

        return context;
    }

    private static void ExportLod(
        MeshFile file,
        ExportContext context,
        MeshLOD lod,
        string namePrefix,
        Matrix4x4 transform)
    {
        var scene = context.scene;
        var scale = GetExportScale(context.format);
        var identity = GetScaledMatrix(Matrix4x4.Identity, scale);

        var bounds = file.MeshData?.boundingBox ?? new AABB();
        foreach (var mesh in lod.MeshGroups) {
            int subId = 0;
            foreach (var sub in mesh.Submeshes) {
                var aiMesh = new Mesh(PrimitiveType.Triangle);
                var matName = file.MaterialNames.ElementAtOrDefault(sub.materialIndex) ?? "NO_MATERIAL";
                var matIndex = scene.Materials.FindIndex(mat => mat.Name == matName);
                if (matIndex == -1) {
                    matIndex = scene.Materials.Count;
                    scene.Materials.Add(new Material() { Name = matName });
                }
                aiMesh.MaterialIndex = matIndex;

                aiMesh.Vertices.AddRange(sub.Positions);
                if (scale != 1) {
                    for (int i = 0; i < aiMesh.Vertices.Count; ++i) aiMesh.Vertices[i] *= scale;
                }
                aiMesh.BoundingBox = new BoundingBox(bounds.minpos, bounds.maxpos);
                if (sub.Buffer.UV0.Length > 0) {
                    var uvOut = aiMesh.TextureCoordinateChannels[0];
                    uvOut.EnsureCapacity(sub.UV0.Length);
                    foreach (var uv in sub.UV0) uvOut.Add(new Vector3((float)uv.x, 1 - (float)uv.y, 0));
                    aiMesh.UVComponentCount[0] = 2;
                }
                if (sub.Buffer.UV1.Length > 0) {
                    var uvOut = aiMesh.TextureCoordinateChannels[1];
                    uvOut.EnsureCapacity(sub.UV1.Length);
                    foreach (var uv in sub.UV1) uvOut.Add(new Vector3((float)uv.x, 1 - (float)uv.y, 0));
                    aiMesh.UVComponentCount[1] = 2;
                }
                if (sub.Buffer.UV2.Length > 0) {
                    var uvOut = aiMesh.TextureCoordinateChannels[2];
                    uvOut.EnsureCapacity(sub.UV2.Length);
                    foreach (var uv in sub.UV2) uvOut.Add(new Vector3((float)uv.x, 1 - (float)uv.y, 0));
                    aiMesh.UVComponentCount[2] = 2;
                }
                if (sub.Buffer.NormalsTangents.Length > 0) {
                    foreach (var nortan in sub.NormalsTangents) {
                        aiMesh.Normals.Add(nortan.Normal);
                        aiMesh.Tangents.Add(nortan.Tangent);
                        aiMesh.BiTangents.Add(nortan.BiTangent);
                    }
                }
                if (sub.Buffer.Colors.Length > 0) {
                    var colOut = aiMesh.VertexColorChannels[0];
                    colOut.EnsureCapacity(sub.Colors.Length);
                    foreach (var col in sub.Colors) colOut.Add(col.ToVector4());
                }
                if (file.BoneData != null && sub.Buffer.Weights.Length > 0) {
                    foreach (var srcBone in file.BoneData.Bones.OrderBy(b => b.index)) {
                        aiMesh.Bones.Add(new Bone(
                            srcBone.name,
                            Matrix4x4.Transpose(GetScaledMatrix(context.bonesLookup[srcBone.name].inverseTransform, scale)),
                            null
                        ));
                    }
                    if (context.writeWeight2FlagAsBones) {
                        foreach (var srcBone in file.BoneData.Bones) {
                            if (context.secondaryWeightBones.ContainsKey(srcBone.name)) {
                                aiMesh.Bones.Add(new Bone(SecondaryWeightDummyBonePrefix + srcBone.name, identity, null));
                            }
                        }
                    }

                    var indexCount = sub.Weights[0].IndexCount;
                    for (int vertId = 0; vertId < sub.Weights.Length; ++vertId) {
                        var vd = sub.Weights[vertId];
                        for (int i = 0; i < indexCount; ++i) {
                            var weight = vd.GetWeight(i);
                            if (weight > 0) {
                                var srcBone = file.BoneData!.DeformBones.Count == 0
                                    ? file.BoneData.RootBones[0]
                                    : file.BoneData.DeformBones[vd.GetIndex(i)];
                                var bone = aiMesh.Bones[srcBone.index];
                                bone.VertexWeights.Add(new VertexWeight(vertId, weight));
                                if (!context.gltfWarned && context.IsGltf && i > 4) {
                                    context.gltfWarned = true;
                                    Logger.Warn($"GLTF exporter does not support more than 4 vertex bone weights. Mesh will not behave correctly when re-imported. Consider using a different file format.");
                                }
                            }
                        }
                    }

                    if (sub.Buffer.ExtraWeights != null) {
                        for (int vertId = 0; vertId < sub.ExtraWeights.Length; ++vertId) {
                            var vd = sub.ExtraWeights[vertId];
                            for (int i = 0; i < indexCount; ++i) {
                                var weight = vd.GetWeight(i);
                                if (weight > 0) {
                                    var srcBone = file.BoneData.DeformBones[vd.GetIndex(i)];
                                    var bone = aiMesh.Bones[srcBone.index];
                                    bone.VertexWeights.Add(new VertexWeight(vertId, weight));
                                }
                            }
                        }
                    }
                    // ensure all bones exist in every mesh (otherwise some bones might not get detected as bones when importing to Blender)
                    foreach (var w in context.bonesLookup) {
                        if (!aiMesh.Bones.Any(b => b.Name == w.Key)) {
                            aiMesh.Bones.Add(new Bone(w.Key, Matrix4x4.Transpose(GetScaledMatrix(w.Value.inverseTransform, scale)), []));
                        }
                    }

                    if (context.includeShapeKeys) {
                        var dict = new Dictionary<int, Bone>();
                        foreach (var bone in file.BoneData!.DeformBones) {
                            var attach = dict[bone.remapIndex] = new Bone() { Name = ShapekeyPrefix + bone.name };
                            aiMesh.Bones.Add(attach);
                        }
                        for (int vertId = 0; vertId < sub.ShapeKeyWeights.Length; ++vertId) {
                            var vd = sub.ShapeKeyWeights[vertId];
                            for (int i = 0; i < indexCount; ++i) {
                                var weight = vd.GetWeight(i);
                                if (weight > 0) {
                                    var bone = dict[vd.GetIndex(i)];
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
                var meshNode = new Node($"{namePrefix}Group_{mesh.groupId.ToString(CultureInfo.InvariantCulture)}_sub{subId++}__{matName}", scene.RootNode);
                meshNode.Transform = transform;
                scene.RootNode.Children.Add(meshNode);
                aiMesh.Name = meshNode.Name;
                meshNode.MeshIndices.Add(scene.Meshes.Count);
                scene.Meshes.Add(aiMesh);
            }
        }
    }
}
