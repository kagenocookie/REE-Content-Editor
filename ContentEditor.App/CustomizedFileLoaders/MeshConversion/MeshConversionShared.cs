using System.Numerics;
using ReeLib;
using ReeLib.Mesh;

namespace ContentEditor.App.FileLoaders;

public partial class CommonMeshResource
{
    internal abstract class MeshExportInterface : IDisposable
    {
        public string format = "";
        public bool includeAllLods;
        public bool includeShadows;
        public bool includeOcclusion;
        public bool includeShapeKeys;
        public FbxSkelFile? skeleton;
        public string? rootBoneName;

        public bool writeWeight2FlagAsBones = AppConfig.Settings.Import.ExportSecondaryWeightAsBones;

        public void AddMesh(MeshFile file, string rootName, Matrix4x4 transform = default)
        {
            if (transform == default) transform = Matrix4x4.Identity;

            foreach (var name in file.MaterialNames) {
                AddAndGetMaterialIndex(name);
            }

            if (file.MeshData != null) {
                for (int i = 0; i < file.MeshData.LODs.Count; i++) {
                    var lod = file.MeshData.LODs[i];
                    if (i == 0) {
                        ExportMeshLod(file, lod, includeAllLods ? $"{rootName}_lod0_" : $"{rootName}_", transform);
                        if (!includeAllLods) break;
                    } else {
                        ExportMeshLod(file, lod, $"{rootName}_lod{i}_", transform);
                    }
                }
            }
            if (includeShadows && file.ShadowMesh != null) {
                for (int i = 0; i < file.ShadowMesh.LODs.Count; i++) {
                    var lod = file.ShadowMesh.LODs[i];
                    ExportMeshLod(file, lod, $"{rootName}_shadow_lod{i}_", transform);
                }
            }
            if (includeOcclusion && file.OccluderMesh != null) {
                if (file.MaterialNames.Count == 0) {
                    AddAndGetMaterialIndex("default");
                }
                ExportMeshLod(file, file.OccluderMesh, $"occ_{rootName}_", transform);
            }
        }

        public abstract void AddMot(MotFile file);
        protected abstract void ExportMeshLod(MeshFile file, MeshLOD lod, string namePrefix, Matrix4x4 transform = default);

        public abstract void ExportToFile(string targetFilepath);
        public abstract int AddAndGetMaterialIndex(string materialName);

        public abstract void PrepareSkeleton(MeshFile mesh);
        public abstract void Dispose();
    }

    public interface IMeshNodeInterface
    {
        public Matrix4x4 Transform { get; set; }
        public string Name { get; set; }
        public bool HasChild(string name);
    }

    internal abstract class MeshInterface<TNode, TNodeImpl> : MeshExportInterface
        where TNode : class
        where TNodeImpl : IMeshNodeInterface
    {
        public Dictionary<string, (TNode node, bool? deforming, Matrix4x4 inverseTransform)> bonesLookup = new();

        public TNode? secondaryWeightNodeContainer;
        public Dictionary<string, TNode> secondaryWeightBones = new();

        public abstract TNode RootNode { get; }

        protected abstract bool AllowShapeKeys { get; }

        private bool shapeKeyWarned;
        internal abstract TNodeImpl GetNodeImpl(TNode node);

        public void AddWeight2Bone(string name)
        {
            if (!bonesLookup.TryGetValue(name, out var boneData)) {
                return;
            }

            if (writeWeight2FlagAsBones) {
                secondaryWeightBones[name] = AddChild(SecondaryWeightDummyBonePrefix + name, boneData.node);
            } else {
                if (secondaryWeightNodeContainer == null) {
                    secondaryWeightNodeContainer = AddChild(SecondaryWeightDummyBonePrefix, RootNode);
                }
                secondaryWeightBones[name] = AddChild(SecondaryWeightDummyBonePrefix + name, secondaryWeightNodeContainer);
            }
        }

        public abstract TNode AddChild(string name, TNode parent);

        public override void PrepareSkeleton(MeshFile mesh)
        {
            var boneRoot = RootNode;
            if (skeleton != null && bonesLookup.Count == 0) {
                foreach (var refBone in skeleton.Bones) {
                    TNode? parent = null;
                    if (refBone.parentIndex != -1) {
                        var parentRef = skeleton.Bones[refBone.parentIndex];
                        parent = bonesLookup.GetValueOrDefault(parentRef.name).node;
                        if (parent == null) {
                            throw new NotImplementedException("Unordered ref skel bones currently not supported");
                        }
                    }
                    rootBoneName ??= refBone.name;
                    var node = AddChild(refBone.name, parent ?? boneRoot);

                    var localTransform = Transform.GetMatrixFromTransforms(refBone.position, refBone.rotation, refBone.scale);

                    GetNodeImpl(node).Transform = localTransform;

                    Matrix4x4.Invert(localTransform, out var inverseLocal);
                    var inverseGlobal = parent == null ? inverseLocal : bonesLookup[GetNodeImpl(parent).Name].inverseTransform * inverseLocal;
                    bonesLookup[refBone.name] = (node, null, inverseGlobal);
                }
            }

            if (mesh.BoneData == null) return;

            // insert bones by queue and requeue them if we don't have their parent yet
            var pendingBones = new Queue<MeshBone>(
                mesh.BoneData.Bones.Where(b => b.parentIndex == -1)
                .Concat(mesh.BoneData.Bones.Where(b => b.parentIndex != -1)));

            while (pendingBones.TryDequeue(out var srcBone)) {
                if (bonesLookup.TryGetValue(srcBone.name, out var boneData)) {
                    if (srcBone.IsDeformBone && boneData.deforming != true) {
                        bonesLookup[srcBone.name] = (boneData.node, true, boneData.inverseTransform);
                    }
                    continue;
                }

                var parentNode = boneRoot;
                if (srcBone.Parent != null) {
                    if (bonesLookup.TryGetValue(srcBone.Parent.name, out var parent)) {
                        parentNode = parent.node;
                    } else {
                        pendingBones.Enqueue(srcBone);
                        continue;
                    }
                }
                rootBoneName ??= srcBone.name;

                var boneNode = AddChild(srcBone.name, parentNode);
                GetNodeImpl(boneNode).Transform = srcBone.localTransform.ToSystem();
                bonesLookup[srcBone.name] = (boneNode, srcBone.IsDeformBone, srcBone.inverseGlobalTransform.ToSystem());
                if (srcBone.useSecondaryWeight) {
                    AddWeight2Bone(srcBone.name);
                }
            }

            if (mesh.MeshBuffer?.ShapeKeyWeights.Length > 0) {
                if (!AllowShapeKeys) {
                    includeShapeKeys = false;
                    if (!shapeKeyWarned) {
                        shapeKeyWarned = true;
                        Logger.Warn($"GLTF exporter does not support enough bones to include shape keys. Mesh will not behave correctly when re-imported. Consider using FBX instead.");
                    }
                } else {
                    includeShapeKeys = true;

                    // add shape key specific bone nodes
                    foreach (var (name, data) in bonesLookup) {
                        var shapeKeyName = ShapekeyPrefix + GetNodeImpl(data.node).Name;
                        if (data.deforming != true || GetNodeImpl(data.node).HasChild(shapeKeyName)) continue;

                        AddChild(shapeKeyName, data.node);
                    }
                }
            }
        }
    }

}