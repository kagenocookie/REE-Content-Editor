using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ContentPatcher;
using glTFLoader;
using glTFLoader.Schema;
using ReeLib;
using ReeLib.Mesh;
using SixLabors.ImageSharp.PixelFormats;

namespace ContentEditor.App.FileLoaders;

public partial class CommonMeshResource : IResourceFile
{
    internal class GltfNodeImpl(GltfExportContext model, Node node) : IMeshNodeInterface
    {
        public Matrix4x4 Transform {
            get => (node.Scale == null ? Matrix4x4.Identity : Matrix4x4.CreateScale(node.Scale[0], node.Scale[1], node.Scale[2]))
                * (node.Rotation == null ? Matrix4x4.Identity : Matrix4x4.CreateFromQuaternion(new Quaternion(node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3])))
                * (node.Translation == null ? Matrix4x4.Identity : Matrix4x4.CreateTranslation(node.Translation[0], node.Translation[1], node.Translation[2]));
            set {
                // using TRS instead of matrix because gltf specs say to not use matrix for animated joints
                Matrix4x4.Decompose(value, out var s, out var r, out var t);
                if (t != Vector3.Zero) node.Translation = [t.X, t.Y, t.Z];
                if (r != Quaternion.Identity) node.Rotation = [r.X, r.Y, r.Z, r.W];
                if (s != Vector3.One) node.Scale = [s.X, s.Y, s.Z];
            }
        }
        public string Name { get => node.Name; set => node.Name = value; }

        public bool HasChild(string name) => node.Children.Any(c => model.nodes[c].Name == name);
    }

    internal sealed class GltfExportContext : MeshInterface<Node, GltfNodeImpl>
    {
        public Gltf model;
        public List<Node> nodes = new();
        public FileHandler binaryBuffer = new();

        private List<BufferView> buffers = new();
        private List<Accessor> accessors = new();

        public int FindNodeIndex(string name) => nodes.FindIndex(n => n.Name == name);

        public GltfExportContext(Gltf model, string name)
        {
            this.model = model;
            // intentionally not adding the node to the model nodes list yet
            // we'll do it at the end when setting up the gltf scene and skeleton
            RootNode = new Node() { Name = name };
            model.Asset = new Asset() {
                Generator = "REE Content Editor v" + AppConfig.Version,
                Version = "2.0"
            };
        }

        public override Node RootNode { get; }
        protected override bool AllowShapeKeys => true;

        public override void AddMot(MotFile file)
        {
            AddMotToScene(this, file);
        }

        public override void ExportToFile(string targetFilepath)
        {
            if (!nodes.Contains(RootNode)) {
                model.Scenes = [
                    new glTFLoader.Schema.Scene() {
                        Name = "Scene",
                        Nodes = [nodes.Count]
                    }
                ];
                model.Scene = 0;
                if (model.Skins?.Length == 1) {
                    model.Skins[0].Skeleton = nodes.Count;
                }
                nodes.Add(RootNode);
            }
            model.Nodes = nodes.ToArray();
            model.BufferViews = buffers.ToArray();
            model.Accessors = accessors.ToArray();
            var ext = Path.GetExtension(targetFilepath);
            binaryBuffer.Seek(0);

            if (ext == ".gltf") {
                var binFilepath = Path.ChangeExtension(targetFilepath, ".bin");
                var binFilename = Path.GetFileName(binFilepath);
                model.Buffers = [
                    new glTFLoader.Schema.Buffer() {
                        Uri = binFilename,
                        ByteLength = (int)binaryBuffer.Stream.Length
                    }
                ];
                model.SaveModel(targetFilepath);
                using var binfs = File.Create(binFilepath);
                binaryBuffer.Stream.CopyTo(binfs);
            } else { // glb
                model.Buffers = [
                    new glTFLoader.Schema.Buffer() {
                        Uri = null,
                        ByteLength = (int)binaryBuffer.Stream.Length
                    }
                ];
                var binBytes = binaryBuffer.ReadBytes((int)binaryBuffer.Stream.Length);
                model.SaveBinaryModelPacked(targetFilepath, targetFilepath, binBytes);
            }
        }

        public override Node AddChild(string name, Node parent)
        {
            var newNode = new Node() {
                Name = name,
            };
            var index = nodes.Count;
            nodes.Add(newNode);
            parent.Children = (parent.Children ?? []).Append(index).ToArray();
            return newNode;
        }

        public override void PrepareSkeleton(MeshFile mesh)
        {
            base.PrepareSkeleton(mesh);
            var skin = model.Skins?.ElementAtOrDefault(0);
            if (skin == null) {
                skin = new Skin() { Name = "Armature" };
                model.Skins = [skin];
            }

            var joints = skin.Joints?.ToList() ?? [];

            void TryAddJoint(Node node)
            {
                var idx = nodes.IndexOf(node);
                if (idx != -1 && !joints.Contains(idx)) {
                    joints.Add(idx);
                }
            }
            if (skeleton != null) {
                foreach (var sk in skeleton.Bones) {
                    TryAddJoint(bonesLookup[sk.name].node);
                }
            }
            foreach (var bb in mesh.BoneData!.Bones) {
                TryAddJoint(bonesLookup[bb.name].node);
                if (writeWeight2FlagAsBones && secondaryWeightBones.TryGetValue(bb.name, out var secb)) {
                    TryAddJoint(secb);
                }
                var skname = ShapekeyPrefix + bb.name;
                var skb = nodes.FirstOrDefault(n => n.Name == skname);
                if (skb != null) {
                    TryAddJoint(skb);
                }
            }
            skin.Joints = joints.ToArray();
        }

        protected override void ExportMeshLod(MeshFile file, MeshLOD lod, string namePrefix, Matrix4x4 transform = default)
        {
            if (file.BoneData?.Bones.Count > 0 && bonesLookup.Count == 0) {
                PrepareSkeleton(file);
            }

            ExportLod(file, this, lod, namePrefix, transform);
        }

        public int AddBuffer<T, TStored>(Accessor.TypeEnum type, Accessor.ComponentTypeEnum componentType, ReadOnlySpan<T> array, Func<T, TStored> convert)
            where TStored : unmanaged
        {
            var converted = new TStored[array.Length];
            for (int i = 0; i < array.Length; i++) {
                converted[i] = convert.Invoke(array[i]);
            }
            return AddBuffer(type, componentType, converted);
        }

        public int AddBuffer<T>(Accessor.TypeEnum type, Accessor.ComponentTypeEnum componentType, ReadOnlySpan<T> array)
            where T : unmanaged
        {
            return AddBuffer(type, componentType, array, array.Length);
        }
        public int AddBuffer<T>(Accessor.TypeEnum type, Accessor.ComponentTypeEnum componentType, ReadOnlySpan<T> array, int elementCount, BufferView.TargetEnum target = BufferView.TargetEnum.ARRAY_BUFFER)
            where T : unmanaged
        {
            var bufIdx = buffers.Count;
            var accIdx = accessors.Count;
            var accessor = new Accessor() {
                BufferView = bufIdx,
                Count = elementCount,
                ComponentType = componentType,
                Type = type,
            };
            if (type == Accessor.TypeEnum.VEC3 && componentType == Accessor.ComponentTypeEnum.FLOAT) {
                // positions require min/max properties (mesh bounds)
                var vecs = MemoryMarshal.Cast<T, Vector3>(array);
                Vector3 min = new Vector3(float.MaxValue);
                Vector3 max = new Vector3(float.MinValue);
                for (int i = 0; i < vecs.Length; i++) {
                    min = Vector3.Min(vecs[i], min);
                    max = Vector3.Max(vecs[i], max);
                }
                accessor.Min = [min.X, min.Y, min.Z];
                accessor.Max = [max.X, max.Y, max.Z];
            }
            accessors.Add(accessor);

            var buf = new BufferView() {
                Buffer = 0,
                ByteOffset = (int)binaryBuffer.Position,
                ByteLength = array.Length * Marshal.SizeOf<T>(),
                Target = target,
            };
            buffers.Add(buf);
            binaryBuffer.WriteSpan(array);
            return accIdx;
        }

        public override void Dispose()
        {
        }

        internal override GltfNodeImpl GetNodeImpl(Node node) => new GltfNodeImpl(this, node);

        public override int AddAndGetMaterialIndex(string materialName)
        {
            var idx = Array.FindIndex(model.Materials ?? [], m => m.Name == materialName);
            if (idx == -1) {
                idx = model.Materials?.Length ?? 0;
                model.Materials = (model.Materials ?? []).Append(new Material() { Name = materialName }).ToArray();
            }
            return idx;
        }

    }

    private static void AddMotToScene(GltfExportContext context, MotFile mot)
    {
        var anim = new Animation() {
            Name = mot.Name,
        };

        // do we care to include hash-only mot bones?

        var channels = new List<AnimationChannel>();
        var samplers = new List<AnimationSampler>();
        foreach (var clip in mot.BoneClips) {
            if (clip.ClipHeader.boneName == null) {
                continue;
            }

            var targetName = clip.ClipHeader.boneName;
            var node = context.FindNodeIndex(targetName);
            if (node == -1) continue;

            void AddSampler<TData>(AnimationChannelTarget.PathEnum trackType, Accessor.TypeEnum valueType, ReeLib.Mot.Track track, TData[] frameValues) where TData : unmanaged
            {
                var input = track.frameIndexes?.Select(i => i / (float)mot.Header.FrameRate).ToArray() ?? [];
                if (track.frameIndexes == null) {
                    if (frameValues?.Length > 0) {
                        input = [0];
                    } else {
                        var b = mot.GetBoneByHash(clip.ClipHeader.boneHash);
                        if (b == null || trackType == AnimationChannelTarget.PathEnum.scale) {
                            return;
                        }
                        // when possible write a one-frame channel with the mot specific default bone pose instead of bind pose
                        if (trackType == AnimationChannelTarget.PathEnum.translation) {
                            Vector3[] tmp = [b.translation];
                            frameValues = MemoryMarshal.Cast<Vector3, TData>(tmp).ToArray();
                            input = [0];
                        } else if (trackType == AnimationChannelTarget.PathEnum.rotation) {
                            Quaternion[] tmp = [b.quaternion];
                            frameValues = MemoryMarshal.Cast<Quaternion, TData>(tmp).ToArray();
                            input = [0];
                        } else {
                            return;
                        }
                    }
                }

                var channel = new AnimationChannel() {
                    Target = new AnimationChannelTarget() { Node = node, Path = trackType, },
                    Sampler = samplers.Count,
                };
                var sampler = new AnimationSampler() {
                    Interpolation = AnimationSampler.InterpolationEnum.LINEAR,
                    Input = context.AddBuffer(Accessor.TypeEnum.SCALAR, Accessor.ComponentTypeEnum.FLOAT, input),
                    Output = context.AddBuffer(valueType, Accessor.ComponentTypeEnum.FLOAT, frameValues),
                };
                channels.Add(channel);
                samplers.Add(sampler);
            }

            if (clip.HasTranslation && clip.Translation != null) {
                AddSampler(AnimationChannelTarget.PathEnum.translation, Accessor.TypeEnum.VEC3, clip.Translation, clip.Translation.translations!);
            }

            if (clip.HasScale && clip.Scale != null) {
                AddSampler(AnimationChannelTarget.PathEnum.scale, Accessor.TypeEnum.VEC3, clip.Scale, clip.Scale.translations!);
            }

            if (clip.HasRotation && clip.Rotation != null) {
                AddSampler(AnimationChannelTarget.PathEnum.rotation, Accessor.TypeEnum.VEC4, clip.Rotation, clip.Rotation.rotations!);
            }
        }
        anim.Channels = channels.ToArray();
        anim.Samplers = samplers.ToArray();

        context.model.Animations = (context.model.Animations ?? []).Append(anim).ToArray();
    }

    private static void ExportLod(
        MeshFile file,
        GltfExportContext context,
        MeshLOD lod,
        string namePrefix,
        Matrix4x4 transform)
    {
        var scene = context.model;

        var meshToGltfJointIndexMap = new Dictionary<(int jointType, int remapIndex), int>();
        var skin = context.model.Skins.ElementAtOrDefault(0);
        if (skin != null && file.BoneData != null) {
            for (int i = 0; i < context.nodes.Count; i++) {
                var node = context.nodes[i];
                int jointType;
                MeshBone? bone;
                if (node.Name.StartsWith(ShapekeyPrefix)) {
                    jointType = 1;
                    bone = file.BoneData?.GetByName(node.Name.Substring(ShapekeyPrefix.Length));
                } else {
                    jointType = 0;
                    bone = file.BoneData?.GetByName(node.Name);
                }
                if (bone == null || bone.remapIndex == -1) continue;
                meshToGltfJointIndexMap[(jointType, bone.remapIndex)] = skin.Joints.IndexOf(i);
            }
        }
        var doShapekeys = context.includeShapeKeys && file.MeshBuffer?.ShapeKeyWeights.Length > 0;
        // gltf standard requires weights to be normalized, but for "shapekey" second weights we have two separately normalized weights per vertex
        // so a fully compliant gltf file would have both sets of weights at [0-0.5] scale each
        // blender loads these non-normalized fine (as-is), which should make it easier to use with blender so I'm keeping it non-fully-compliant
        // re-export back from blender will set it down to 0.5 again so import will still need to handle both cases
        // but I'm not fully opposed to changing that or adding an option for it, or if we can find another way of storing them
        var weightScale = 1f;
        // var weightScale = doShapekeys ? 0.5f : 1f;

        foreach (var mesh in lod.MeshGroups) {
            int subId = 0;
            foreach (var sub in mesh.Submeshes) {
                var matName = file.MaterialNames.ElementAtOrDefault(sub.materialIndex) ?? "NO_MATERIAL";

                var gMesh = new Mesh();
                var prim = new MeshPrimitive() {
                    Mode = MeshPrimitive.ModeEnum.TRIANGLES,
                    Material = context.AddAndGetMaterialIndex(matName),
                    Attributes = new(),
                };

                gMesh.Primitives = [prim];

                prim.Attributes["POSITION"] = context.AddBuffer(Accessor.TypeEnum.VEC3, Accessor.ComponentTypeEnum.FLOAT, sub.Positions);
                if (mesh.Buffer.NormalsTangents.Length > 0) {
                    prim.Attributes["NORMAL"] = context.AddBuffer(Accessor.TypeEnum.VEC3, Accessor.ComponentTypeEnum.FLOAT, sub.NormalsTangents, (nortan) => nortan.Normal);
                    prim.Attributes["TANGENT"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.FLOAT, sub.NormalsTangents, (nortan) => new Vector4(nortan.Tangent, nortan.BiTangentSign > 0 ? 1 : -1));
                }
                if (mesh.Buffer.UV0.Length > 0) {
                    prim.Attributes["TEXCOORD_0"] = context.AddBuffer(Accessor.TypeEnum.VEC2, Accessor.ComponentTypeEnum.FLOAT, sub.UV0, (uv) => new Vector2((float)uv.x, (float)uv.y));
                }
                if (mesh.Buffer.UV1.Length > 0) {
                    prim.Attributes["TEXCOORD_1"] = context.AddBuffer(Accessor.TypeEnum.VEC2, Accessor.ComponentTypeEnum.FLOAT, sub.UV1, (uv) => new Vector2((float)uv.x, (float)uv.y));
                }
                if (mesh.Buffer.UV2.Length > 0) {
                    prim.Attributes["TEXCOORD_2"] = context.AddBuffer(Accessor.TypeEnum.VEC2, Accessor.ComponentTypeEnum.FLOAT, sub.UV2, (uv) => new Vector2((float)uv.x, (float)uv.y));
                }
                if (mesh.Buffer.Colors.Length > 0) {
                    prim.Attributes["COLOR_0"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.UNSIGNED_BYTE, sub.Colors);
                }
                if (file.BoneData != null && mesh.Buffer.Weights.Length > 0) {
                    var indexCount = sub.Weights[0].IndexCount;

                    // always store joints at ushort size in case the user is doing a merged skeleton export (likely to have >255 joints)
                    // always store weights as floats because shapekeys halve our accuracy due to gltf standard expecting strictly normalized weight scales so floats should conteract that
                    prim.Attributes["WEIGHTS_0"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.FLOAT, sub.Weights, (w) => new Vector4(
                        w.GetWeight(0) * weightScale,
                        w.GetWeight(1) * weightScale,
                        w.GetWeight(2) * weightScale,
                        w.GetWeight(3) * weightScale
                    ));
                    prim.Attributes["JOINTS_0"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.UNSIGNED_SHORT, sub.Weights, (w) => new Short4(
                        meshToGltfJointIndexMap[(0, w.GetIndex(0))],
                        meshToGltfJointIndexMap[(0, w.GetIndex(1))],
                        meshToGltfJointIndexMap[(0, w.GetIndex(2))],
                        meshToGltfJointIndexMap[(0, w.GetIndex(3))]
                    ));

                    if (sub.GetPerVertexMaxWeightCount(0) > 4) {
                        if (indexCount == 8) {
                            prim.Attributes["WEIGHTS_1"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.FLOAT, sub.Weights, (w) => new Vector4(
                                w.GetWeight(4) * weightScale,
                                w.GetWeight(5) * weightScale,
                                w.GetWeight(6) * weightScale,
                                w.GetWeight(7) * weightScale
                            ));
                            prim.Attributes["JOINTS_1"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.UNSIGNED_SHORT, sub.Weights, (w) => new Short4(
                                meshToGltfJointIndexMap[(0, w.GetIndex(4))],
                                meshToGltfJointIndexMap[(0, w.GetIndex(5))],
                                meshToGltfJointIndexMap[(0, w.GetIndex(6))],
                                meshToGltfJointIndexMap[(0, w.GetIndex(7))]
                            ));

                            if (mesh.Buffer.ExtraWeights != null) {
                                var exWc = sub.GetPerVertexMaxWeightCount(1);
                                if (exWc > 0) {
                                    prim.Attributes["WEIGHTS_2"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.FLOAT, sub.ExtraWeights, (w) => new Vector4(
                                        w.GetWeight(0) * weightScale,
                                        w.GetWeight(1) * weightScale,
                                        w.GetWeight(2) * weightScale,
                                        w.GetWeight(3) * weightScale
                                    ));
                                    prim.Attributes["JOINTS_2"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.UNSIGNED_SHORT, sub.ExtraWeights, (w) => new Short4(
                                        meshToGltfJointIndexMap[(0, w.GetIndex(0))],
                                        meshToGltfJointIndexMap[(0, w.GetIndex(1))],
                                        meshToGltfJointIndexMap[(0, w.GetIndex(2))],
                                        meshToGltfJointIndexMap[(0, w.GetIndex(3))]
                                    ));
                                }
                                if (exWc > 4) {
                                    prim.Attributes["WEIGHTS_3"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.FLOAT, sub.ExtraWeights, (w) => new Vector4(
                                        w.GetWeight(4) * weightScale,
                                        w.GetWeight(5) * weightScale,
                                        w.GetWeight(6) * weightScale,
                                        w.GetWeight(7) * weightScale
                                    ));
                                    prim.Attributes["JOINTS_3"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.UNSIGNED_SHORT, sub.ExtraWeights, (w) => new Short4(
                                        meshToGltfJointIndexMap[(0, w.GetIndex(4))],
                                        meshToGltfJointIndexMap[(0, w.GetIndex(5))],
                                        meshToGltfJointIndexMap[(0, w.GetIndex(6))],
                                        meshToGltfJointIndexMap[(0, w.GetIndex(7))]
                                    ));
                                }
                            }
                        } else { // indexCount == 6
                            if (mesh.Buffer.ExtraWeights == null) {
                                prim.Attributes["WEIGHTS_1"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.FLOAT, sub.Weights, (w) => new Vector4(
                                    w.GetWeight(0) * weightScale,
                                    w.GetWeight(1) * weightScale,
                                    0,
                                    0
                                ));
                                prim.Attributes["JOINTS_1"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.UNSIGNED_SHORT, sub.Weights, (w) => new Short4(
                                    meshToGltfJointIndexMap[(0, w.GetIndex(4))],
                                    meshToGltfJointIndexMap[(0, w.GetIndex(5))],
                                    0,
                                    0
                                ));
                            } else {
                                // split between first and second weight buffer
                                var weights = new Vector4[sub.vertCount];
                                var joints = new short[4 * sub.vertCount];
                                for (int i = 0; i < sub.vertCount; i++) {
                                    var w1 = sub.Weights[i];
                                    var w2 = sub.ExtraWeights[i];
                                    weights[i].X = w1.GetWeight(4) * weightScale;
                                    weights[i].Y = w1.GetWeight(5) * weightScale;
                                    weights[i].Z = w2.GetWeight(0) * weightScale;
                                    weights[i].W = w2.GetWeight(1) * weightScale;
                                    joints[i * 4 + 0] = (short)meshToGltfJointIndexMap[(0, w1.GetIndex(4))];
                                    joints[i * 4 + 1] = (short)meshToGltfJointIndexMap[(0, w1.GetIndex(5))];
                                    joints[i * 4 + 2] = (short)meshToGltfJointIndexMap[(0, w2.GetIndex(0))];
                                    joints[i * 4 + 3] = (short)meshToGltfJointIndexMap[(0, w2.GetIndex(1))];
                                }
                                prim.Attributes["WEIGHTS_1"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.FLOAT, weights);
                                prim.Attributes["JOINTS_1"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.UNSIGNED_SHORT, joints, sub.vertCount);

                                if (sub.GetPerVertexMaxWeightCount(1) > 2) {
                                    prim.Attributes["WEIGHTS_2"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.FLOAT, sub.ExtraWeights, (w) => new Vector4(
                                        w.GetWeight(2) * weightScale,
                                        w.GetWeight(3) * weightScale,
                                        w.GetWeight(4) * weightScale,
                                        w.GetWeight(5) * weightScale
                                    ));
                                    prim.Attributes["JOINTS_2"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.UNSIGNED_SHORT, sub.ExtraWeights, (w) => new Short4(
                                        meshToGltfJointIndexMap[(0, w.GetIndex(2))],
                                        meshToGltfJointIndexMap[(0, w.GetIndex(3))],
                                        meshToGltfJointIndexMap[(0, w.GetIndex(4))],
                                        meshToGltfJointIndexMap[(0, w.GetIndex(5))]
                                    ));
                                }
                            }
                        }
                    }

                    if (doShapekeys) {
                        int shapekeyAttrIndex;
                        if (prim.Attributes.ContainsKey("WEIGHTS_3")) {
                            shapekeyAttrIndex = 4;
                        } else if (prim.Attributes.ContainsKey("WEIGHTS_2")) {
                            shapekeyAttrIndex = 3;
                        } else if (prim.Attributes.ContainsKey("WEIGHTS_1")) {
                            shapekeyAttrIndex = 2;
                        } else if (prim.Attributes.ContainsKey("WEIGHTS_0")) {
                            shapekeyAttrIndex = 1;
                        } else {
                            shapekeyAttrIndex = 0;
                        }

                        prim.Attributes[$"WEIGHTS_{shapekeyAttrIndex}"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.FLOAT, sub.ShapeKeyWeights, (w) => new Vector4(
                            w.GetWeight(0) * weightScale,
                            w.GetWeight(1) * weightScale,
                            w.GetWeight(2) * weightScale,
                            w.GetWeight(3) * weightScale
                        ));
                        prim.Attributes[$"JOINTS_{shapekeyAttrIndex}"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.UNSIGNED_SHORT, sub.ShapeKeyWeights, (w) => new Short4(
                            meshToGltfJointIndexMap[(1, w.GetIndex(0))],
                            meshToGltfJointIndexMap[(1, w.GetIndex(1))],
                            meshToGltfJointIndexMap[(1, w.GetIndex(2))],
                            meshToGltfJointIndexMap[(1, w.GetIndex(3))]
                        ));

                        if (indexCount == 8) {
                            if (sub.GetPerVertexMaxWeightCount(2) > 4) {
                                prim.Attributes[$"WEIGHTS_{shapekeyAttrIndex+1}"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.FLOAT, sub.ShapeKeyWeights, (w) => new Vector4(
                                    w.GetWeight(4) * weightScale,
                                    w.GetWeight(5) * weightScale,
                                    w.GetWeight(6) * weightScale,
                                    w.GetWeight(7) * weightScale
                                ));
                                prim.Attributes[$"JOINTS_{shapekeyAttrIndex+1}"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.UNSIGNED_SHORT, sub.ShapeKeyWeights, (w) => new Short4(
                                    meshToGltfJointIndexMap[(1, w.GetIndex(4))],
                                    meshToGltfJointIndexMap[(1, w.GetIndex(5))],
                                    meshToGltfJointIndexMap[(1, w.GetIndex(6))],
                                    meshToGltfJointIndexMap[(1, w.GetIndex(7))]
                                ));
                            }
                        } else { // indexCount == 6
                            if (sub.GetPerVertexMaxWeightCount(2) > 4) {
                                prim.Attributes[$"WEIGHTS_{shapekeyAttrIndex+1}"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.FLOAT, sub.ShapeKeyWeights, (w) => new Vector4(
                                    w.GetWeight(4) * weightScale,
                                    w.GetWeight(5) * weightScale,
                                    0,
                                    0
                                ));
                                prim.Attributes[$"JOINTS_{shapekeyAttrIndex+1}"] = context.AddBuffer(Accessor.TypeEnum.VEC4, Accessor.ComponentTypeEnum.UNSIGNED_SHORT, sub.ShapeKeyWeights, (w) => new Short4(
                                    meshToGltfJointIndexMap[(1, w.GetIndex(4))],
                                    meshToGltfJointIndexMap[(1, w.GetIndex(5))],
                                    0,
                                    0
                                ));
                            }
                        }
                    }
                }

                if (mesh.Buffer.IntegerFaces != null) {
                    prim.Indices = context.AddBuffer(Accessor.TypeEnum.SCALAR, Accessor.ComponentTypeEnum.UNSIGNED_INT, sub.IntegerIndices);
                } else {
                    prim.Indices = context.AddBuffer(Accessor.TypeEnum.SCALAR, Accessor.ComponentTypeEnum.UNSIGNED_SHORT, sub.Indices);
                }

                // each submesh needs to have a unique node so they don't get merged together
                var meshNode = context.AddChild($"{namePrefix}Group_{mesh.groupId.ToString(CultureInfo.InvariantCulture)}_sub{subId++}__{matName}", context.RootNode);
                context.GetNodeImpl(meshNode).Transform = transform;
                gMesh.Name = meshNode.Name;
                var meshIndex = context.model.Meshes?.Length ?? 0;
                if (context.model.Meshes == null) {
                    context.model.Meshes = [gMesh];
                    if (skin != null) {
                        var bindMatrices = skin.Joints
                            .Select(idx => context.bonesLookup[context.nodes[idx].Name.Replace(ShapekeyPrefix, "").Replace(SecondaryWeightDummyBonePrefix, "")].inverseTransform)
                            .ToArray();
                        skin.InverseBindMatrices = context.AddBuffer(Accessor.TypeEnum.MAT4, Accessor.ComponentTypeEnum.FLOAT, bindMatrices);
                    }
                } else {
                    context.model.Meshes = (context.model.Meshes ?? []).Append(gMesh).ToArray();
                }
                meshNode.Mesh = meshIndex;
                if (skin != null) {
                    meshNode.Skin = 0;
                }
            }
        }
    }
}
