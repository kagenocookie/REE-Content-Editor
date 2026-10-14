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
    internal class AssimpNodeImpl(Node node, float scale) : IMeshNodeInterface
    {
        public Matrix4x4 Transform {
            get => GetScaledMatrix(Matrix4x4.Transpose(node.Transform), 1 / scale);
            set => node.Transform = Matrix4x4.Transpose(GetScaledMatrix(value, scale));
        }

        public string Name { get => node.Name; set => node.Name = value; }

        public bool HasChild(string name) => node.Children.Any(c => c.Name == name);
    }

    internal sealed class AssimpExportContext : MeshInterface<Node, AssimpNodeImpl>
    {
        public Assimp.Scene scene = new Assimp.Scene();

        public override Node RootNode => scene.RootNode;

        public bool gltfWarned = false;
        private readonly AssimpContext assimpContext;
        public float scale;

        public AssimpExportContext(AssimpContext context, string name, string ext)
        {
            this.assimpContext = context;

            format = context.GetFormatIDFromExtension(ext);

            scene.RootNode ??= new Node(name);
            includeShapeKeys = format == "fbx";
            scale = GetExportScale(format);
        }

        protected override bool AllowShapeKeys => includeShapeKeys;

        public override Node AddChild(string name, Node parent)
        {
            var newNode = new Node(name, parent);
            parent.Children.Add(newNode);
            return newNode;
        }


        public override void AddMot(MotFile file)
        {
            AddMotToScene(scene, file, format);
        }

        public override void ExportToFile(string filepath)
        {
            assimpContext.ExportFile(scene, filepath, format);
        }

        public override void Dispose()
        {
            assimpContext.Dispose();
        }

        internal override AssimpNodeImpl GetNodeImpl(Node node)
        {
            return new AssimpNodeImpl(node, scale);
        }

        protected override void ExportMeshLod(MeshFile file, MeshLOD lod, string namePrefix, Matrix4x4 transform = default)
        {
            if (file.BoneData?.Bones.Count > 0 && bonesLookup.Count == 0) {
                PrepareSkeleton(file);
            }

            ExportLod(file, this, lod, namePrefix, transform);
        }

        public override int AddAndGetMaterialIndex(string materialName)
        {
            var idx = scene.Materials.FindIndex(m => m.Name == materialName);
            if (idx != -1) return idx;
            idx = scene.MaterialCount;
            scene.Materials.Add(new Material() { Name = materialName });
            return idx;
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

    private static void ExportLod(
        MeshFile file,
        AssimpExportContext context,
        MeshLOD lod,
        string namePrefix,
        Matrix4x4 transform)
    {
        // NOTE: every matrix needs to be transposed, assimp expects them transposed compared to default System.Numeric.Matrix4x4 for some shit ass reason
        // NOTE2: assimp currently forces vert deduplication for gltf export so we may lose some vertices (https://github.com/assimp/assimp/issues/6349)
        // NOTE3: weights > 4 will get get lost for gltf because we can't tell it to write more weights (AI_CONFIG_EXPORT_GLTF_UNLIMITED_SKINNING_BONES_PER_VERTEX)
        // unless assimp changes defaults or AssimpNetter exposes the setting somehow, we can't handle that through assimp
        // alternative direct gltf export solves that better

        var scene = context.scene;
        var scale = GetExportScale(context.format);

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
                        // note to self: AVOID the constructor
                        // it accepts a Matrix3x3 instead for some godwaful reason,
                        // and that doesn't play nice with a transposed 4x4 matrix
                        aiMesh.Bones.Add(new Bone() {
                            Name = srcBone.name,
                            OffsetMatrix = Matrix4x4.Transpose(GetScaledMatrix(context.bonesLookup[srcBone.name].inverseTransform, scale))
                        });
                    }
                    if (context.writeWeight2FlagAsBones) {
                        foreach (var srcBone in file.BoneData.Bones) {
                            if (context.secondaryWeightBones.ContainsKey(srcBone.name)) {
                                aiMesh.Bones.Add(new Bone() {
                                    Name = SecondaryWeightDummyBonePrefix + srcBone.name,
                                    OffsetMatrix = Matrix4x4.Transpose(GetScaledMatrix(context.bonesLookup[srcBone.name].inverseTransform, scale))
                                });
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
                                if (!context.gltfWarned && !context.includeShapeKeys && i > 4) {
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
                    // ensure all bones exist in every mesh (otherwise some bones might not get detected as bones when importing to Blender, or the exporter might crash)
                    foreach (var w in context.bonesLookup) {
                        if (!aiMesh.Bones.Any(b => b.Name == w.Key)) {
                            aiMesh.Bones.Add(new Bone() {
                                Name = w.Key,
                                OffsetMatrix = Matrix4x4.Transpose(GetScaledMatrix(w.Value.inverseTransform, scale))
                            });
                        }
                    }

                    if (context.includeShapeKeys && mesh.Buffer.ShapeKeyWeights.Length > 0) {
                        var dict = new Dictionary<int, Bone>();
                        foreach (var bone in file.BoneData!.DeformBones) {
                            var attach = dict[bone.remapIndex] = new Bone() {
                                Name = ShapekeyPrefix + bone.name,
                                OffsetMatrix = Matrix4x4.Transpose(GetScaledMatrix(context.bonesLookup[bone.name].inverseTransform, scale))
                            };
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
