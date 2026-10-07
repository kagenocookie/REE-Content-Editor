using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ContentEditor.Core;
using ContentPatcher;
using glTFLoader;
using glTFLoader.Schema;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mesh;
using ReeLib.Mot;
using ReeLib.Motlist;
using ReeLib.MplyMesh;
using ReeLib.via;
using SixLabors.ImageSharp.PixelFormats;

namespace ContentEditor.App.FileLoaders;

public partial class CommonMeshResource : IResourceFile
{
    public static CommonMeshResource? CreateFromGltf(FileHandle handle, string name, Workspace env, string? filepath)
    {
        var stream = filepath != null ? File.OpenRead(filepath).ToMemoryStream() : handle.Stream;
        var versionConfig = MeshFile.GetGameVersionConfigs(env.Config.Game.GameEnum)[0];
        var resource = new CommonMeshResource(name, env) {
            GameVersion = env.Config.Game.GameEnum,
        };
        ImportMeshFromGltf(resource, stream, versionConfig, env, filepath, out var warnings);

        return resource;
    }

    /// <param name="stream">The main gltf file stream.</param>
    /// <param name="versionConfig"></param>
    /// <param name="filepath">File path to use for resolving external files (GLTF).</param>
    /// <param name="warnings"></param>
    public static MeshFile ImportMeshFromGltf(CommonMeshResource targetMesh, Stream stream, string versionConfig, Workspace workspace, string? filepath, out MeshImportWarnings warnings)
    {
        warnings = MeshImportWarnings.None;
        var mesh = new MeshFile(new FileHandler());
        targetMesh.NativeMesh = mesh;
        mesh.Header.BufferCountFlag = 1;

        var mainBuffer = new ReeLib.Mesh.MeshBuffer();
        mesh.MeshBuffer = mainBuffer;

        // Interface.LoadModel disposes the stream, but we need to keep it active so force a copy here
        var model = Interface.LoadModel(stream.ToReadOnlyMemoryStream());
        var rootNodes = (model.Scenes.Length > 0
            ? model.Scenes[model.Scene ?? 0].Nodes.Select(n => model.Nodes[n])
            : model.Nodes).ToArray();

        if (model.Scenes.Length > 1) {
            Logger.Warn("GLTF Importer: Only default scene is supported, rest will be ignored.");
        }

        if (rootNodes.Length == 0) {
            Logger.Error("GLTF Importer: Invalid mesh file, no root nodes found");
            return mesh;
        }

        var buffers = new List<byte[]>();
        for (int i = 0; i < model.Buffers.Length; i++) {
            var bufferData = model.Buffers[i];
            if (i == 0 && string.IsNullOrEmpty(bufferData.Uri)) {
                var buf = Interface.LoadBinaryBuffer(stream.ToReadOnlyMemoryStream());
                buffers.Add(buf);
                continue;
            }

            try {
                var buf = model.LoadBinaryBuffer(i, filepath);
                buffers.Add(buf);
            } catch (Exception e) {
                Logger.Error($"GLTF Importer: Failed to resolve buffer file {model.Buffers[i].Name}: {e.Message}");
                return mesh;
            }
        }

        var hasMeshes = model.Meshes.Length > 0;
        var hasSkeleton = model.Skins?.Length > 0;
        var hasAnimations = model.Animations?.Length > 0;
        var hasMaterials = model.Materials?.Length > 0;

        var meshNodes = model.Nodes.Where(n => n.Mesh != null).ToArray();

        if (hasSkeleton) {
            mesh.BoneData = new ReeLib.Mesh.MeshBoneHierarchy();
            var skins = meshNodes.Where(c => c.Skin != null).Select(c => c.Skin!.Value).Distinct().ToArray();

            if (skins.Length > 1) {
                Logger.Warn($"GLTF Importer: Only one skeleton is supported for imported meshes, ignoring the rest ({filepath})");
            }

            var secNodes = model.Nodes
                .Where(n => n.Name.StartsWith(SecondaryWeightDummyBonePrefix) && n.Name != SecondaryWeightDummyBonePrefix).Select(n => n.Name.Replace(SecondaryWeightDummyBonePrefix, ""))
                .ToHashSet();

            var skin = model.Skins![skins[0]];
            var invBindMatrices = skin.InverseBindMatrices == null ? default : ReadAccessor<Matrix4x4, float>(model, skin.InverseBindMatrices.Value, buffers);
            meshNodes = meshNodes.Where(m => m.Skin == skins[0]).ToArray();

            // initialize bone list
            for (int i = 0; i < skin.Joints.Length; i++) {
                var joint = model.Nodes[skin.Joints[i]];
                if (joint.Name.StartsWith(ShapekeyPrefix) || joint.Name.StartsWith(SecondaryWeightDummyBonePrefix)) continue;

                var mat = MemoryMarshal.Cast<float, Matrix4x4>(joint.Matrix)[0];
                if (mat.IsIdentity) {
                    var pos = MemoryMarshal.Cast<float, Vector3>(joint.Translation)[0];
                    var scale = MemoryMarshal.Cast<float, Vector3>(joint.Scale)[0];
                    var quat = MemoryMarshal.Cast<float, Quaternion>(joint.Rotation)[0];
                    mat = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(quat) * Matrix4x4.CreateTranslation(pos);
                }

                var bone = new ReeLib.Mesh.MeshBone() {
                    index = mesh.BoneData.Bones.Count,
                    name = joint.Name,
                    useSecondaryWeight = secNodes.Contains(joint.Name),
                    localTransform = new ReeLib.via.mat4(mat),
                    childIndex = -1,
                    nextSibling = -1,
                    symmetryIndex = mesh.BoneData.Bones.Count
                };

                if (invBindMatrices.Length > 0) {
                    // note: standard says inverseBindMatrices is not required; in that case we need to manually compute these later
                    bone.inverseGlobalTransform = invBindMatrices[i];
                }
                mesh.BoneData.Bones.Add(bone);
            }

            // setup bone hierarchy
            foreach (var jointId in skin.Joints) {
                var boneNode = model.Nodes[jointId];
                if (boneNode.Name.StartsWith(ShapekeyPrefix) || boneNode.Name.StartsWith(SecondaryWeightDummyBonePrefix)) continue;
                if (boneNode.Children == null) continue;

                var bone = mesh.BoneData.GetByName(boneNode.Name)!;

                foreach (var gltfChildBoneIndex in boneNode.Children) {
                    var childName = model.Nodes[gltfChildBoneIndex].Name;
                    if (childName.StartsWith(ShapekeyPrefix) || childName.StartsWith(SecondaryWeightDummyBonePrefix)) continue;
                    var childBone = mesh.BoneData.GetByName(childName);
                    childBone!.Parent = bone;
                    childBone!.parentIndex = bone.index;
                    if (bone.Children.Count == 0) {
                        bone.childIndex = childBone.index;
                    } else {
                        bone.Children[^1].nextSibling = childBone.index;
                    }
                    bone.Children.Add(childBone);
                }
            }

            // ensure all transforms are defined
            foreach (var bone in mesh.BoneData.Bones) {
                // TODO should we worry about bone order here?
                if (bone.Parent == null) {
                    bone.globalTransform = bone.localTransform;
                } else {
                    bone.globalTransform = Matrix4x4.Multiply(bone.localTransform.ToSystem(), bone.Parent.globalTransform.ToSystem());
                }

                if (invBindMatrices.Length == 0) {
                    bone.inverseGlobalTransform = Matrix4x4.Invert(bone.globalTransform.ToSystem(), out var inverse) ? inverse : throw new Exception("Failed to calculate inverse bone matrix " + bone.name);
                }
            }

            // handle symmetry bones
            foreach (var bone in mesh.BoneData!.Bones) {
                if (bone.name.StartsWith("l_", StringComparison.InvariantCultureIgnoreCase)) {
                    var rightName = string.Concat("r_", bone.name.AsSpan(2));
                    var right = mesh.BoneData!.Bones.FirstOrDefault(b => b.name.Equals(rightName, StringComparison.InvariantCultureIgnoreCase));
                    if (right == null) {
                        Logger.Warn("Found left bone without corresponding right bone: " + bone.name);
                    } else {
                        bone.Symmetry = right;
                        bone.symmetryIndex = right.index;
                        right.Symmetry = bone;
                        right.symmetryIndex = bone.index;
                    }
                }
            }

            mesh.BoneData.RootBones.AddRange(mesh.BoneData.Bones.Where(b => b.Parent == null));
        }
        var data = new GltfData(model, buffers);

        data.meshes = meshNodes
            .SelectMany(node => model.Meshes[node.Mesh!.Value].Primitives.Select(primitive => (node, primitive)))
            .Where(p => p.primitive.Attributes.ContainsKey("POSITION"))
            .Select(x => new GltfMeshInfo(data, x.node, x.primitive))
            .ToArray();
        data.hasShapekeys = model.Nodes.Any(m => m.Name != null && m.Name.StartsWith(ShapekeyPrefix));

        var occMeshes = data.meshes.Where(m => m.Name.StartsWith("occ_")).ToList();
        var shadowMeshes = data.meshes.Where(m => m.Name.Contains("shadow_lod")).ToList();
        var mainMeshes = data.meshes.Where(m => !occMeshes.Contains(m) && !shadowMeshes.Contains(m)).ToList();
        if (shadowMeshes.Count > 0 || mainMeshes.Count > 0) {
            mesh.MeshData = new MeshData(mainBuffer);
            mesh.MeshData.boundingBox = AABB.Combine(data.meshes.Select(m => new AABB(m.MinPosition, m.MaxPosition)));
            // TODO: sphere bounds not fully accurate
            mesh.MeshData.boundingSphere = new Sphere(mesh.MeshData.boundingBox.Center, Math.Max(mesh.MeshData.boundingBox.Size.X, Math.Max(mesh.MeshData.boundingBox.Size.Y, mesh.MeshData.boundingBox.Size.Z)) / 2);
            PreAllocateMeshBuffer(versionConfig, mesh, mainBuffer, data, mainMeshes);
        }
        bool reuseBufferForShadows = false;
        if (shadowMeshes.Count > 0) {
            var shadowBuffer = new MeshBuffer();
            PreAllocateMeshBuffer(versionConfig, mesh, shadowBuffer, data, shadowMeshes);
            if (shadowBuffer.Positions.Length == mainBuffer.Positions.Length && shadowBuffer.Faces?.Length == mainBuffer.Faces?.Length && shadowBuffer.IntegerFaces?.Length == mainBuffer.IntegerFaces?.Length) {
                Logger.Info("Shadow meshes contain same amount of vertices and faces as main mesh. Assuming identical and reusing main mesh buffer instead.");
                reuseBufferForShadows = true;
            } else {
                mainBuffer.AdditionalBuffers.Add(shadowBuffer);
            }
        }

        if (occMeshes.Count > 0) {
            var occVertCount = occMeshes.Sum(m => m.VertexCount);
            var occFaceCount = occMeshes.Sum(m => m.IndicesCount);
            var occBuffer = new MeshBuffer();
            occBuffer.Positions = new Vector3[occVertCount];
            occBuffer.Faces = new ushort[occFaceCount];
            mainBuffer.AdditionalBuffers.Add(occBuffer);
        }

        var materialNames = model.Materials?.Select(m => m.Name).ToArray() ?? [];
        var useNameMaterials = AppConfig.Settings.Import.ImportMaterialsFromMeshName;
        if (useNameMaterials) {
            materialNames = data.meshes.Select((m) => {
                if (m.Name.StartsWith("occ_")) return "";
                return MeshLoader.GetMeshMaterialFromName(m.Name);
            }).Distinct().ToArray();
        }
        foreach (var matName in materialNames) {
            if (string.IsNullOrEmpty(matName)) continue;
            mesh.MaterialNames.Add(matName);
        }

        if (occMeshes.Count > 0 && shadowMeshes.Count == 0 && mainMeshes.Count == 0) {
            mesh.MaterialNames.Clear();
        }

        mesh.ChangeVersion(versionConfig);

        var serializerVersion = MeshFile.GetSerializerVersion(versionConfig);
        var maxWeightsPerVert = MeshFile.GetWeightLimit(versionConfig);
        bool allowExtraWeights = maxWeightsPerVert > 8;
        var maxWeighedBones = MeshFile.GetDeformBoneLimit(versionConfig);
        var weightsBufferIndexCount = maxWeightsPerVert % 6 == 0 ? 6 : 8;

        var boneIndexMap = mesh.BoneData?.Bones.ToDictionary(b => b.name, b => b.index) ?? [];
        var deformBones = new SortedList<int, MeshBone>();

        var warnedBones = new HashSet<string>();
        var sortedMeshes = data.meshes.Order(new FuncComparer<GltfMeshInfo>((a, b) => {
            var type1 = occMeshes.Contains(a) ? 2 : shadowMeshes.Contains(a) ? 1 : 0;
            var type2 = occMeshes.Contains(b) ? 2 : shadowMeshes.Contains(b) ? 1 : 0;

            return type1.CompareTo(type2) * 10000 + MeshLoader.GetMeshGroupFromName(a.Name).CompareTo(MeshLoader.GetMeshGroupFromName(b.Name));
        })).ToList();

        void UpdateDeformBone(int boneIndex, MeshBuffer buffer, int vertIndex)
        {
            var targetBone = mesh.BoneData!.Bones[boneIndex];
            if (targetBone.boundingBox.IsEmpty) targetBone.boundingBox = AABB.MaxMin;
            targetBone.boundingBox = targetBone.boundingBox.AsAABB.Extend(Vector3.Transform(buffer.Positions[vertIndex], targetBone.inverseGlobalTransform.ToSystem()));
            deformBones.TryAdd(boneIndex, targetBone);
        }

        int vertOffset = 0;
        int indicesOffset = 0;

        foreach (var aiMesh in sortedMeshes) {
            var groupIdx = MeshLoader.GetMeshGroupFromName(aiMesh.Name);
            var buffer = mainBuffer;

            MeshLOD meshLod;
            int lod = 0;
            if (shadowMeshes.Contains(aiMesh)) {
                if (mesh.ShadowMesh == null) {
                    vertOffset = 0;
                    indicesOffset = 0;
                    mesh.ShadowMesh = new ShadowMesh(mainBuffer);
                }
                if (reuseBufferForShadows) continue;

                buffer = mainBuffer.AdditionalBuffers.First();
                var markerPos = aiMesh.Name.IndexOf("shadow_lod");
                if (markerPos == -1) {
                    lod = 0;
                } else {
                    var numStartPos = markerPos + "shadow_lod".Length;
                    var numEndPos = aiMesh.Name.IndexOf('_', numStartPos);
                    if (numEndPos == -1) numEndPos = aiMesh.Name.Length;
                    if (!int.TryParse(aiMesh.Name.AsSpan()[numStartPos..numEndPos], out lod)) {
                        lod = 0;
                    }
                }
                while (lod >= mesh.ShadowMesh.LODs.Count) {
                    mesh.ShadowMesh.LODs.Add(new MeshLOD(mainBuffer));
                }
                meshLod = mesh.ShadowMesh.LODs[lod];

            } else if (occMeshes.Contains(aiMesh)) {
                buffer = mainBuffer.AdditionalBuffers.Last();
                if (mesh.OccluderMesh == null) {
                    // restart offsets since this is a different buffer now
                    // we can safely do this because we sorted the input meshes by type (main>shadow>occ)
                    vertOffset = 0;
                    indicesOffset = 0;
                    mesh.OccluderMesh = new OccluderMesh(mainBuffer) { TargetBuffer = buffer };
                }
                meshLod = mesh.OccluderMesh;
            } else if (aiMesh.Name.StartsWith("lod") || aiMesh.Name.Contains("_lod")) {
                var markerPos = aiMesh.Name.IndexOf("_lod");
                if (markerPos == -1) markerPos = aiMesh.Name.IndexOf("lod");
                if (markerPos == -1) {
                    lod = 0;
                } else {
                    var numStartPos = aiMesh.Name[markerPos] == '_' ? markerPos + 4 : markerPos + 3;
                    var numEndPos = aiMesh.Name.IndexOf('_', numStartPos);
                    if (numEndPos == -1) numEndPos = aiMesh.Name.Length;
                    if (!int.TryParse(aiMesh.Name.AsSpan()[numStartPos..numEndPos], out lod)) {
                        lod = 0;
                    }
                }
                while (lod >= mesh.MeshData!.LODs.Count) {
                    mesh.MeshData.LODs.Add(new MeshLOD(mainBuffer));
                }
                meshLod = mesh.MeshData.LODs[lod];
            } else {
                if (mesh.MeshData!.LODs.Count == 0) {
                    mesh.MeshData.LODs.Add(new MeshLOD(mainBuffer));
                }
                meshLod = mesh.MeshData.LODs[0];
            }

            if (mainBuffer.Faces != null && (indicesOffset % 2) != 0) {
                indicesOffset++; // handle padding
            }

            var totalVertCount = buffer.Positions.Length;
            var vertCount = aiMesh.VertexCount;
            var indicesCount = aiMesh.IndicesCount;
            var faceCount = aiMesh.IndicesCount / 3;

            // note: vert limit check shouldn't be needed here, we're letting assimp handling splitting automatically

            var group = meshLod.MeshGroups.FirstOrDefault(grp => grp.groupId == groupIdx);
            if (group == null) {
                meshLod.MeshGroups.Add(group = new MeshGroup(buffer));
                group.groupId = (byte)groupIdx;
            }

            group.vertexCount += vertCount;
            group.indicesCount += indicesCount;
            var newSub = new Submesh(buffer);
            newSub.facesIndexOffset = indicesOffset;
            newSub.vertsIndexOffset = vertOffset;
            newSub.vertCount = aiMesh.VertexCount;
            newSub.indicesCount = indicesCount;
            newSub.materialIndex = (ushort)(useNameMaterials ? materialNames.IndexOf(MeshLoader.GetMeshMaterialFromName(aiMesh.Name)) : aiMesh.MaterialIndex);
            if (newSub.materialIndex < 0 || newSub.materialIndex >= materialNames.Length) {
                newSub.materialIndex = 0;
            }

            group.submeshCount++;
            group.Submeshes.Add(newSub);

            aiMesh.GetPositions().CopyTo(buffer.Positions.AsSpan(vertOffset));

            if (buffer.NormalsTangents.Length > 0) {
                var nor = aiMesh.GetNormals();
                var tan = aiMesh.GetTangents();
                if (tan.IsEmpty) {
                    // TODO When tangents are not specified, client implementations SHOULD calculate tangents using
                    // default MikkTSpace (https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#mikktspace) algorithms with
                    // the specified vertex positions, normals, and texture coordinates associated with the normal texture.
                    var tanarr = new SByte4[nor.Length];
                    tan = tanarr;
                    for (int i = 0; i < vertCount; ++i) {
                        // this is nowhere close to correct, putting something in so it's not just some fixed wrong value
                        tanarr[i] = SByte4.QuantizeNormal(Vector3.Cross(Vector3.UnitY, nor[i].DequantizeNormal()), sbyte.MaxValue);
                    }
                    Logger.Error("GLTF Importer: Manual tangent calculation not yet supported. The mesh might look wrong ingame, make sure you export your mesh with tangents enabled.");
                }
                for (int i = 0; i < vertCount; ++i) {
                    buffer.NormalsTangents[vertOffset + i] = new QuantizedNorTan(nor[i], tan[i]);
                }
            }

            if (buffer.UV0.Length > 0) {
                var uv = aiMesh.GetTexcoord0();
                // note: gltf upper left = 0,0 bottom right = 1,1; I think this is identical to REE so we can take it as is
                uv.CopyTo(buffer.UV0.AsSpan(vertOffset));
            }

            if (buffer.UV1.Length > 0) {
                var uv = aiMesh.GetTexcoord1();
                if (uv.IsEmpty) {
                    for (int i = 0; i < vertCount; ++i) buffer.UV1[vertOffset + i] = new HFloat2();
                } else {
                    uv.CopyTo(buffer.UV1.AsSpan(vertOffset));
                }
            }

            if (buffer.UV2.Length > 0) {
                var uv = aiMesh.GetTexcoord2();
                if (uv.IsEmpty) {
                    for (int i = 0; i < vertCount; ++i) buffer.UV2[vertOffset + i] = new HFloat2();
                } else {
                    uv.CopyTo(buffer.UV2.AsSpan(vertOffset));
                }
            }

            if (buffer.Colors.Length > 0) {
                aiMesh.GetColors().CopyTo(buffer.Colors.AsSpan(vertOffset));
            }

            if (mainBuffer.Faces != null) {
                aiMesh.CopyIndicesTo(mainBuffer.Faces.AsSpan(indicesOffset));
            } else {
                aiMesh.CopyIndicesTo(mainBuffer.IntegerFaces.AsSpan(indicesOffset));
            }

            if (buffer.Weights.Length > 0) {
                var skin = model.Skins![0];
                var (mergedWeights, extBuffer) = MergeGltfWeightData(data, aiMesh, skin, boneIndexMap, false, weightsBufferIndexCount, buffer);
                var (mergedWeightsShapekey, extShapeBuffer) = MergeGltfWeightData(data, aiMesh, skin, boneIndexMap, true, weightsBufferIndexCount, buffer);
                List<(int index, float weight)>[]?[] bothWeights = [mergedWeights, mergedWeightsShapekey];
                if (extBuffer && allowExtraWeights) {
                    buffer.ExtraWeights = new VertexBoneWeights[totalVertCount];
                    for (int ew = 0; ew < totalVertCount; ew++) buffer.ExtraWeights[ew] = new VertexBoneWeights(serializerVersion);
                }
                for (int wtype = 0; wtype < 2; wtype++) {
                    var isShapekey = wtype == 1;
                    if (bothWeights[wtype] == null) continue;
                    var weights = bothWeights[wtype]!;

                    var wtBuffer = isShapekey ? buffer.ShapeKeyWeights : buffer.Weights!;
                    for (int vert = 0; vert < weights.Length; vert++) {
                        var mw = weights[vert];
                        var outWeight = wtBuffer[vertOffset + vert];
                        var indexCount = outWeight.IndexCount;
                        var bone_i = 0;
                        for (; bone_i < mw.Count && bone_i < indexCount; bone_i++) {
                            var (boneIdx, weight) = mw[bone_i];
                            outWeight.SetIndex(bone_i, boneIdx);
                            outWeight.SetWeight(bone_i, weight);
                            UpdateDeformBone(boneIdx, buffer, vertOffset + vert);
                        }

                        if (mw.Count > indexCount) {
                            var indexLimit = indexCount;
                            if (allowExtraWeights && !isShapekey) {
                                outWeight = buffer.ExtraWeights![vertOffset + vert];
                                indexLimit = indexCount * 2;
                                for (; bone_i < mw.Count && bone_i < indexLimit; bone_i++) {
                                    var (boneIdx, weight) = mw[bone_i];
                                    outWeight.SetIndex(bone_i - indexCount, boneIdx);
                                    outWeight.SetWeight(bone_i - indexCount, weight);
                                    UpdateDeformBone(boneIdx, buffer, vertOffset + vert);
                                }
                            }

                            if (mw.Count > indexLimit && warnedBones.Add(mesh.BoneData!.Bones[mw[bone_i].index].name)) {
                                Log.Warn($"Too many weights (> {maxWeightsPerVert}) for{(isShapekey ? " SHAPEKEY" : "")} bone {mesh.BoneData!.Bones[mw[bone_i].index].name}. Ignoring extra weights.");
                            }
                        }
                    }
                }

                var hasLooseVerts = Array.IndexOf(buffer.Weights, null, vertOffset, vertCount) != -1;
                if (hasLooseVerts) throw new Exception($"Found {buffer.Weights.AsSpan(vertOffset, vertCount).ToArray().Count(w => w == null)} unweighted vertices in imported mesh {aiMesh.Name} - this is not OK");
            }

            vertOffset += vertCount;
            indicesOffset += indicesCount;
        }

        if (mainBuffer.Weights.Length > 0) {
            int remapIndex = 0;
            var boundingBoxMismatches = 0;
            foreach (var (remap, bone) in deformBones) {
                bone.remapIndex = remapIndex++;
                mesh.BoneData!.DeformBones.Add(bone);

                var boneOrigin = bone.localTransform.ToSystem().Translation;
                if (!bone.boundingBox.AsAABB.Contains(boneOrigin)) {
                    boundingBoxMismatches++;
                }
            }
            if (boundingBoxMismatches > deformBones.Count * 0.8f) {
                warnings |= MeshImportWarnings.QuestionableSkeletonRotation;
                Logger.Warn($"{boundingBoxMismatches} out of {deformBones.Count} bones were detected to be outside of their affected mesh bounding box. Did you perhaps forget to apply rotations in the original mesh?");
            }
            if (deformBones.Count > maxWeighedBones) {
                warnings |= MeshImportWarnings.TooManyDeformBones;
                Logger.Error($"Imported mesh contains {deformBones.Count} deform bones (bones that have weights assigned). Only up to {maxWeighedBones} are supported for mesh format {versionConfig}");
            }

            RemapDeformBones(mainBuffer.Weights, deformBones, null);

            if (mainBuffer.ExtraWeights == null && MeshFile.RequireNullWeights(versionConfig)) {
                // if you're wondering why this exists, I'm not quite sure either
                // since Pragmata Demo, the weights don't weight correctly unless the bone indices with weight = 0 are filled with the least-weighted index
                // for meshes that only have at most the whole 6/8 weights weighted, this means an additional weight buffer also (sometimes) needs to exist, even though it's all 0 there
                // there exist meshes that have full 6 weights but no extra weight buffer and I'm not sure why either, so clearly this check here isn't fully correct either
                // do some meshes with only 5 weighted bones need the extra empty weight buffer too? is there some other magical reason for needing this?
                // for now this will do until proven otherwise
                var needExtraBuffer = false;
                var lastIndex = maxWeightsPerVert / 2 - 1;
                foreach (var w in mainBuffer.Weights) {
                    if (w.GetWeight(lastIndex) > 0) {
                        needExtraBuffer = true;
                        break;
                    }
                }
                if (needExtraBuffer) {
                    var totalVertCount = mainBuffer.Weights.Length;

                    mainBuffer.ExtraWeights = new VertexBoneWeights[totalVertCount];
                    for (int i = 0; i < totalVertCount; i++) mainBuffer.ExtraWeights[i] ??= new VertexBoneWeights(serializerVersion);
                }
            }

            if (mainBuffer.ExtraWeights != null) RemapDeformBones(mainBuffer.ExtraWeights, deformBones, mainBuffer.Weights);
            if (mainBuffer.ShapeKeyWeights.Length > 0) RemapDeformBones(mainBuffer.ShapeKeyWeights, deformBones, null);
        }

        if (reuseBufferForShadows && mesh.ShadowMesh != null) {
            for (int lod = 0; lod < mesh.ShadowMesh.LODs.Count; ++lod) {
                mesh.ShadowMesh.LODs[lod] = mesh.MeshData!.LODs[lod];
            }
        }

        mesh.ChangeVersion(versionConfig);
        ImportAnimationsFromGltf(targetMesh, data, workspace);
        return mesh;
    }

    private static void ImportAnimationsFromGltf(CommonMeshResource targetMesh, GltfData data, Workspace workspace)
    {
        var model = data.model;
        if (model.Animations == null || model.Animations.Length == 0 || targetMesh.NativeMesh.BoneData == null) return;

        workspace.TryGetFileExtensionVersion("motlist", out var version);
        var motlist = new MotlistFile(new FileHandler() { FileVersion = version });
        targetMesh.Motlist = motlist;
        motlist.Header.version = (ReeLib.Motlist.MotlistVersion)version;
        motlist.Header.MotListName = targetMesh.Name;
        var motver = motlist.Header.version.GetMotVersion();

        static void AddRecursiveBones(List<MotBone> bones, MeshBoneHierarchy data, List<MeshBone> children, MotBone? parentBone)
        {
            foreach (var node in children) {
                var bone = new MotBone(){ boneName = node.name, boneHash = MurMur3HashUtils.GetHash(node.name), Index = bones.Count };
                var localMatrix = node.localTransform.ToSystem();
                if (!Matrix4x4.Decompose(localMatrix, out _, out bone.quaternion, out bone.translation)) {
                    Logger.Error($"Failed to decompose bone {node.name} offset");
                }
                if (bone.quaternion.W < 0) {
                    bone.quaternion = Quaternion.Negate(bone.quaternion);
                }

                bones.Add(bone);
                bone.Parent = parentBone;
                parentBone?.Children.Add(bone);
                AddRecursiveBones(bones, data, node.Children, bone);
            }
        }

        List<MotBone> motBones = new();
        AddRecursiveBones(motBones, targetMesh.NativeMesh.BoneData, targetMesh.NativeMesh.BoneData.RootBones, null);
        var rootBones = motBones.Where(b => b.Parent == null).ToList();
        List<string> orderedBoneNames = motBones.Select(b => b.boneName).ToList();

        foreach (var aiAnim in model.Animations) {
            if (aiAnim.Channels.Length == 0 || !aiAnim.Channels.Any(ch => ch.Target.Node != null)) continue;

            var mot = new MotFile(motlist.FileHandler);
            mot.Name = aiAnim.Name;
            mot.Header.version = motver;
            mot.Bones.AddRange(motBones);
            mot.RootBones.AddRange(rootBones);
            if (mot.Name.Contains("_loop", StringComparison.OrdinalIgnoreCase)) {
                mot.Header.blending = 0;
            }

            const int FrameRate = 60;
            mot.Header.FrameRate = FrameRate;
            motlist.MotFiles.Add(mot);
            var motIndex = new MotIndex(motlist.Header.version) { MotFile = mot, motNumber = (ushort)motlist.MotFiles.Count };
            motlist.Motions.Add(motIndex);

            var settings = AppConfig.Settings.Import;

            float frameCount = 0;
            foreach (var channel in aiAnim.Channels) {
                if (channel.Target.Node == null) continue;

                var sampler = aiAnim.Samplers[channel.Sampler];
                var boneName = model.Nodes[channel.Target.Node.Value].Name;

                var existingClip = mot.BoneClips.FirstOrDefault(c => c.ClipHeader.boneName == boneName);
                var clipHeader = existingClip?.ClipHeader ?? new BoneClipHeader(motver);
                var clip = existingClip ?? new BoneMotionClip(clipHeader);

                MotBone? bone = null;
                if (boneName.StartsWith("_hash")) {
                    // not much else we can do about these
                    clipHeader.boneName = null;
                    clipHeader.boneHash = uint.TryParse(boneName.AsSpan("_hash".Length), out var hash) ? hash : 0;
                } else {
                    clipHeader.boneName = boneName;
                    clipHeader.boneHash = MurMur3HashUtils.GetHash(boneName);
                    bone = mot.GetBoneByHash(clipHeader.boneHash);
                    clipHeader.boneIndex = (ushort)(bone?.Index ?? 0); // would we need these to be remap index?
                }

                var input = ReadAccessor<float, float>(model, sampler.Input, data.buffers);
                Track? track = null;

                // note: we're currently ignoring the interpolation setting
                // we'd need to bake the inbetween frames if we wanted non-linear

                if (channel.Target.Path == AnimationChannelTarget.PathEnum.translation) {
                    clipHeader.trackFlags |= TrackFlag.Translation;
                    track = new Track(motver, TrackValueType.Vector3);
                    track.frameIndexes = new int[input.Length];
                    track.translations = new Vector3[input.Length];
                    clip.Translation = track;

                    var output = ReadAccessor<Vector3, float>(model, sampler.Output, data.buffers);
                    for (int i = 0; i < input.Length; ++i) {
                        track.frameIndexes[i] = (int)Math.Round(input[i] * FrameRate);
                        track.translations[i] = output[i];
                    }
                } else if (channel.Target.Path == AnimationChannelTarget.PathEnum.scale) {
                    clipHeader.trackFlags |= TrackFlag.Scale;
                    track = new Track(motver, TrackValueType.Vector3);
                    track.frameIndexes = new int[input.Length];
                    track.translations = new Vector3[input.Length];
                    clip.Scale = track;

                    var output = ReadAccessor<Vector3, float>(model, sampler.Output, data.buffers);
                    for (int i = 0; i < input.Length; ++i) {
                        track.frameIndexes[i] = (int)Math.Round(input[i] * FrameRate);
                        track.translations[i] = output[i];
                    }
                } else if (channel.Target.Path == AnimationChannelTarget.PathEnum.rotation) {
                    clipHeader.trackFlags |= TrackFlag.Rotation;
                    track = new Track(motver, TrackValueType.Quaternion);
                    track.frameIndexes = new int[input.Length];
                    track.rotations = new Quaternion[input.Length];
                    clip.Rotation = track;

                    var output = ReadAccessor<Quaternion, float>(model, sampler.Output, data.buffers);
                    for (int i = 0; i < input.Length; ++i) {
                        track.frameIndexes[i] = (int)Math.Round(input[i] * FrameRate);
                        track.rotations[i] = output[i];
                    }

                    // maintain imported quantization type for anim compression since we're not gonna lose more accuracy than we already did
                    var accessorComponentType = data.model.Accessors[sampler.Output].ComponentType;
                    switch (accessorComponentType) {
                        case Accessor.ComponentTypeEnum.BYTE:
                        case Accessor.ComponentTypeEnum.UNSIGNED_BYTE:
                            track.RotationCompressionType = QuaternionDecompression.LoadQuaternions8Bit;
                            break;
                        case Accessor.ComponentTypeEnum.SHORT:
                        case Accessor.ComponentTypeEnum.UNSIGNED_SHORT:
                            track.RotationCompressionType = QuaternionDecompression.LoadQuaternions16Bit;
                            break;
                        case Accessor.ComponentTypeEnum.FLOAT:
                            track.RotationCompressionType = QuaternionDecompression.LoadQuaternions3Component;
                            break;
                    }
                }

                if (track == null) continue;

                track.maxFrame = (int)(input[^1] * FrameRate);
                track.frameRate = FrameRate;
                track.keyCount = input.Length;
                frameCount = Math.Max(frameCount, track.maxFrame);

                if (clip.ClipHeader.trackFlags != 0 && existingClip == null && track.TrackType != 0) {
                    mot.BoneClips.Add(clip);
                }
            }

            mot.Header.frameCount = (float)frameCount;
            mot.Header.endFrame = mot.Header.frameCount;
        }
    }

    private static (List<(int index, float weight)>[]?, bool needExtendedBuffer) MergeGltfWeightData(GltfData data, GltfMeshInfo aiMesh, Skin skin, Dictionary<string, int> boneIndexMap, bool shapeKeys, int indexCount, MeshBuffer buffer)
    {
        if (shapeKeys && buffer.ShapeKeyWeights.Length == 0) return default;

        // gltf gives us normalized weights (required per gltf standard)
        // but "shape keys" are also weights and therefore we end up getting split 0.5 main weights and 0.5 shape keys
        // we need to counteract that without losing precision, therefore read weights as float and then scale up both
        var weightScale = buffer.ShapeKeyWeights.Length > 0 ? 2f : 1f;

        var weightBlockCount = aiMesh.WeightBlockCount;
        if (weightBlockCount == 0) return default;

        var verts = aiMesh.VertexCount;
        var mergedWeights = new List<(int index, float weight)>[verts];
        for (int i = 0; i < verts; i++) mergedWeights[i] = new();

        for (int g = 0; g < weightBlockCount; g++) {
            var weights = aiMesh.GetWeights(g);
            var indices = aiMesh.GetJointIndices(g);

            for (int v = 0; v < verts; v++) {
                for (int i = 0; i < 4; i++) {
                    var w = weights[v * 4 + i];
                    var j = indices[v * 4 + i];

                    if (w > 0) {
                        var jointName = data.model.Nodes[skin.Joints[j]].Name;
                        if (jointName.StartsWith(ShapekeyPrefix) != shapeKeys) continue;
                        if (jointName.StartsWith(SecondaryWeightDummyBonePrefix)) continue;

                        var actualBoneIndex = boneIndexMap[jointName.Replace(ShapekeyPrefix, "")];
                        // verify we're not over-scaling with a bit of leeway
                        Debug.Assert((w * weightScale) <= 1.02f);
                        mergedWeights[v].Add((actualBoneIndex, w * weightScale));
                    }
                }
            }
        }

        int weightCount = mergedWeights.Max(ww => ww.Count);
        var needExtendedBuffer = weightCount > indexCount;

        return (mergedWeights, needExtendedBuffer);
    }

    private static ReadOnlySpan<TElement> ReadAccessor<TElement, TPrimitive>(Gltf model, int accessorId, List<byte[]> buffers)
        where TElement : unmanaged
        where TPrimitive : unmanaged
    {
        if (accessorId == -1) return default;

        var accessor = model.Accessors[accessorId];
        if (accessor.BufferView == null) {
            throw new Exception($"GLTF Importer: Accessor {accessorId} has no bufferView index");
        }

        var bufView = model.BufferViews[accessor.BufferView.Value];
        var buffer = buffers[bufView.Buffer];
        var bufData = buffer.AsSpan(bufView.ByteOffset, bufView.ByteLength);
        if (typeof(TElement) == typeof(Matrix4x4)) {
            Debug.Assert(accessor.Type == Accessor.TypeEnum.MAT4);
            // note: Accessors of matrix type have data stored in column-major order
            return MemoryMarshal.Cast<byte, TElement>(bufData);
        }

        if (typeof(TElement) == typeof(int) && accessor.ComponentType == Accessor.ComponentTypeEnum.UNSIGNED_INT) {
            // int face indices
            return MemoryMarshal.Cast<byte, TElement>(bufData);
        }

        if (typeof(TElement) == typeof(Color)) {
            // float, ubyte_norm, ushort_norm
            if (accessor.Type == Accessor.TypeEnum.VEC4) {
                switch (accessor.ComponentType) {
                    case Accessor.ComponentTypeEnum.FLOAT:
                        return CastArray<Vector4, Color, TElement>(bufData, static vec => Color.FromVector4(vec));
                    case Accessor.ComponentTypeEnum.UNSIGNED_SHORT:
                        return CastArray<Short4, Color, TElement>(bufData, static item => Color.FromVector4(item.ToVector4()));
                    case Accessor.ComponentTypeEnum.UNSIGNED_BYTE:
                        return CastArray<Byte4, Color, TElement>(bufData, static item => Color.FromVector4(item.ToVector4()));
                    default:
                        throw new Exception($"GLTF Importer: Unsupported component type {accessor.ComponentType} {accessor.Type} for {typeof(TElement)} array");
                }
            } else if (accessor.Type == Accessor.TypeEnum.VEC3) {
                switch (accessor.ComponentType) {
                    case Accessor.ComponentTypeEnum.FLOAT:
                        return CastArray<Vector3, Color, TElement>(bufData, static item => Color.FromVector4(new Vector4(item.X, item.Y, item.Z, 1)));
                    case Accessor.ComponentTypeEnum.UNSIGNED_SHORT:
                    case Accessor.ComponentTypeEnum.UNSIGNED_BYTE:
                    default:
                        throw new Exception($"GLTF Importer: Unsupported component type {accessor.ComponentType} {accessor.Type} for {typeof(TElement)} array");
                }
            } else {
                throw new Exception($"GLTF Importer: Unsupported component type {accessor.ComponentType} {accessor.Type} for {typeof(TElement)} array");
            }
        }

        if (typeof(TPrimitive) == typeof(sbyte) && accessor.ComponentType == Accessor.ComponentTypeEnum.FLOAT) {
            if (accessor.Type == Accessor.TypeEnum.VEC3) {
                // normals
                return CastArray<Vector3, SByte4, TElement>(bufData, static item => SByte4.QuantizeNormal(item));
            } else if (accessor.Type == Accessor.TypeEnum.VEC4) {
                // tangents
                return CastArray<Vector4, SByte4, TElement>(bufData, static item => SByte4.QuantizeNormal(item.AsVector3(), item.W > 0 ? sbyte.MaxValue : sbyte.MinValue));
            } else {
                throw new Exception($"GLTF Importer: Unsupported component type {accessor.ComponentType} {accessor.Type} for {typeof(TElement)} array");
            }
        }

        if (typeof(TElement) == typeof(HFloat2)) {
            // texcoords: float, ubyte_norm, ushort_norm
            switch (accessor.ComponentType) {
                case Accessor.ComponentTypeEnum.FLOAT:
                    return CastArray<Vector2, HFloat2, TElement>(bufData, static item => new HFloat2(item.X, item.Y));
                case Accessor.ComponentTypeEnum.UNSIGNED_SHORT:
                    return CastArray<uint, HFloat2, TElement>(bufData, static item => new HFloat2(
                        (item & 0xffff) * (1f/0xffff),
                        ((item >> 16) & 0xffff) * (1f/0xffff)
                    ));
                case Accessor.ComponentTypeEnum.UNSIGNED_BYTE:
                    return CastArray<ushort, HFloat2, TElement>(bufData, static item => new HFloat2(
                        (item & 0xff) * (1f/255),
                        ((item >> 8) & 0xff) * (1f/255)
                    ));
                default:
                    throw new Exception($"GLTF Importer: Unsupported component type {accessor.ComponentType} {accessor.Type} for {typeof(TElement)} array");
            }
        }

        if (typeof(TElement) == typeof(Short4)) {
            // weight indices: ubyte, ushort
            switch (accessor.ComponentType) {
                case Accessor.ComponentTypeEnum.UNSIGNED_BYTE:
                    return CastArray<Byte4, Short4, TElement>(bufData, static item => new Short4(item.ToVector4()));
                case Accessor.ComponentTypeEnum.UNSIGNED_SHORT:
                    return MemoryMarshal.Cast<byte, TElement>(bufData);
            }
        }

        if (typeof(TElement) == typeof(Vector4)) {
            // weights: float, ubyte_norm, ushort_norm
            // we want to read the weights at full size so the actual conversion logic has no accuracy loss from force normalized weights
            switch (accessor.ComponentType) {
                case Accessor.ComponentTypeEnum.FLOAT:
                    return MemoryMarshal.Cast<byte, TElement>(bufData);
                case Accessor.ComponentTypeEnum.UNSIGNED_BYTE:
                    return CastArray<Byte4, Vector4, TElement>(bufData, static item => item.ToVector4() * (1/0xff));
                case Accessor.ComponentTypeEnum.UNSIGNED_SHORT:
                    return CastArray<Short4, Vector4, TElement>(bufData, static item => item.ToScaledVector4() * (1/0xffff));
            }
        }

        if (typeof(TElement) == typeof(Quaternion)) {
            // anim rotations: float, sbyte_norm, byte_norm, short_norm, ushort_norm
            // not 100% on the non-float types because adding proper types is pain, should be reasonably close though
            switch (accessor.ComponentType) {
                case Accessor.ComponentTypeEnum.FLOAT:
                    return MemoryMarshal.Cast<byte, TElement>(bufData);
                case Accessor.ComponentTypeEnum.UNSIGNED_BYTE:
                    return CastArray<Byte4, Vector4, TElement>(bufData, static item => item.ToVector4() / 255f);
                case Accessor.ComponentTypeEnum.SHORT:
                    return CastArray<Short4, Vector4, TElement>(bufData, static item => Vector4.Max(-Vector4.One, item.ToVector4() / 32767f));
                case Accessor.ComponentTypeEnum.BYTE:
                    return CastArray<Byte4, Vector4, TElement>(bufData, static item => Vector4.Max(-Vector4.One, item.ToVector4() - new Vector4(128)) / 127f);
                case Accessor.ComponentTypeEnum.UNSIGNED_SHORT:
                    return CastArray<Short4, Vector4, TElement>(bufData, static item => (item.ToVector4() + new Vector4(32768)) / 65535f);
            }
        }

        var componentType = accessor.ComponentType switch {
            Accessor.ComponentTypeEnum.FLOAT => typeof(float),
            Accessor.ComponentTypeEnum.BYTE => typeof(sbyte),
            Accessor.ComponentTypeEnum.SHORT => typeof(short),
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE => typeof(byte),
            Accessor.ComponentTypeEnum.UNSIGNED_INT => typeof(uint),
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT => typeof(ushort),
            _ => throw new NotImplementedException("Unknown GLTF component type " + accessor.ComponentType),
        };

        if (componentType == typeof(TPrimitive)) {
            return MemoryMarshal.Cast<byte, TElement>(bufData);
        }

        throw new NotImplementedException($"GLTF Importer: Unimplemented component type {accessor.ComponentType} {accessor.Type} for {typeof(TElement)} array");
    }

    private static Span<TFinal> CastArray<TSource, TStorage, TFinal>(ReadOnlySpan<byte> bytes, Func<TSource, TStorage> converter)
        where TSource : unmanaged
        where TStorage : unmanaged
        where TFinal : unmanaged
    {
        Debug.Assert(typeof(TStorage) == typeof(TFinal));
        var array = MemoryMarshal.Cast<byte, TSource>(bytes);
        var newArray = new TStorage[array.Length];
        for (int i = 0; i < array.Length; i++) {
            newArray[i] = converter.Invoke(array[i]);
        }
        return MemoryMarshal.Cast<TStorage, TFinal>(newArray.AsSpan());
    }

    private sealed record GltfData(
        Gltf model,
        List<byte[]> buffers
    )
    {
        public GltfMeshInfo[] meshes = [];
        public bool hasShapekeys;
    };

    private static void PreAllocateMeshBuffer(string versionConfig, MeshFile mesh, MeshBuffer buffer, GltfData data, List<GltfMeshInfo> meshes)
    {
        var totalVertCount = meshes.Sum(m => m.VertexCount);
        var totalTriCount = meshes.Sum(m => m.IndicesCount);
        var paddedTriCount = totalTriCount + meshes.Count(m => (m.IndicesCount) % 2 != 0);

        buffer.Positions = new Vector3[totalVertCount];
        mesh.Header.flags |= ContentFlags.EnableRebraiding2;
        var serializeVersion = MeshFile.GetSerializerVersion(versionConfig);
        if (meshes.All(m => m.HasNormals)) {
            buffer.NormalsTangents = new QuantizedNorTan[totalVertCount];
        }
        if (meshes.Any(m => m.HasUV0)) buffer.UV0 = new HFloat2[totalVertCount];
        if (meshes.Any(m => m.HasUV1)) buffer.UV1 = new HFloat2[totalVertCount];
        if (meshes.Any(m => m.HasUV2)) buffer.UV2 = new HFloat2[totalVertCount];
        if (meshes.All(m => m.HasWeights)) {
            buffer.Weights = new VertexBoneWeights[totalVertCount];
            mesh.Header.flags |= ContentFlags.IsSkinning | ContentFlags.HasJoint;
            for (int i = 0; i < totalVertCount; i++) buffer.Weights[i] = new VertexBoneWeights(serializeVersion);
        }
        if (data.hasShapekeys) {
            buffer.ShapeKeyWeights = new VertexBoneWeights[totalVertCount];
            mesh.Header.flags |= ContentFlags.HasVertexGroup;
            for (int i = 0; i < totalVertCount; i++) buffer.ShapeKeyWeights[i] = new VertexBoneWeights(serializeVersion);
        }
        if (meshes.All(m => m.HasColors)) {
            buffer.Colors = new Color[totalVertCount];
            mesh.Header.flags |= ContentFlags.HasVertexColor;
        }

        var needIntFaces = meshes.Any(m => m.IndicesType == Accessor.ComponentTypeEnum.UNSIGNED_INT && m.VertexCount >= ushort.MaxValue);

        if (needIntFaces) {
            buffer.IntegerFaces = new int[totalTriCount];
            mesh.MeshData ??= new(buffer);
            mesh.MeshData.integerFaces = true;
        } else {
            buffer.Faces = new ushort[paddedTriCount];
        }
    }

    private class GltfMeshInfo
    {
        public string Name { get; set; }
        public string MeshName { get; set; }
        private readonly MeshPrimitive primitive;
        public int VertexCount { get; }
        public int IndicesCount { get; }
        public Accessor.ComponentTypeEnum IndicesType => data.model.Accessors[primitive.Indices!.Value].ComponentType;
        public int MaterialIndex => primitive.Material ?? 0;
        private readonly GltfData data;

        public GltfMeshInfo(GltfData data, Node node, MeshPrimitive primitive)
        {
            this.data = data;
            MeshName = data.model.Meshes[node.Mesh!.Value].Name;
            Name = node.Name;
            VertexCount = data.model.Accessors[primitive.Attributes["POSITION"]].Count;
            IndicesCount = primitive.Indices == null ? 0 : data.model.Accessors[primitive.Indices.Value].Count;
            if (IndicesCount == 0) {
                throw new NotImplementedException("GLTF Importer: Meshes without indices not yet supported");
            }
            if (primitive.Mode != MeshPrimitive.ModeEnum.TRIANGLES) {
                throw new NotImplementedException("GLTF Importer: Only triangle meshes are supported");
            }

            this.primitive = primitive;
        }

        public bool HasNormals => primitive.Attributes.ContainsKey("NORMAL");
        public bool HasTangents => primitive.Attributes.ContainsKey("TANGENT");
        public bool HasUV0 => primitive.Attributes.ContainsKey("TEXCOORD_0");
        public bool HasUV1 => primitive.Attributes.ContainsKey("TEXCOORD_1");
        public bool HasUV2 => primitive.Attributes.ContainsKey("TEXCOORD_2");
        public bool HasColors => primitive.Attributes.ContainsKey("COLOR");
        public bool HasWeights => primitive.Attributes.ContainsKey("JOINTS_0");

        public Vector3 MinPosition => MemoryMarshal.Cast<float, Vector3>(data.model.Accessors[primitive.Attributes["POSITION"]].Min)[0];
        public Vector3 MaxPosition => MemoryMarshal.Cast<float, Vector3>(data.model.Accessors[primitive.Attributes["POSITION"]].Max)[0];

        public int WeightBlockCount
        {
            get {
                int count = 0;
                // support up to 32 weights per vertex (4 blocks for 16 weights and *2 to account for shape keys)
                for (int i = 0; i < 8; i++) {
                    if (!primitive.Attributes.ContainsKey("JOINTS_" + i)) {
                        break;
                    }
                    count++;
                }
                return count;
            }
        }

        public ReadOnlySpan<Vector3> GetPositions() => ReadAccessor<Vector3, float>(data.model, primitive.Attributes["POSITION"], data.buffers);
        public ReadOnlySpan<SByte4> GetNormals() => ReadAccessor<SByte4, sbyte>(data.model, primitive.Attributes["NORMAL"], data.buffers);
        public ReadOnlySpan<SByte4> GetTangents() => ReadAccessor<SByte4, sbyte>(data.model, primitive.Attributes.GetValueOrDefault("TANGENT", -1), data.buffers);
        public ReadOnlySpan<HFloat2> GetTexcoord0() => ReadAccessor<HFloat2, Half>(data.model, primitive.Attributes.GetValueOrDefault("TEXCOORD_0", -1), data.buffers);
        public ReadOnlySpan<HFloat2> GetTexcoord1() => ReadAccessor<HFloat2, Half>(data.model, primitive.Attributes.GetValueOrDefault("TEXCOORD_1", -1), data.buffers);
        public ReadOnlySpan<HFloat2> GetTexcoord2() => ReadAccessor<HFloat2, Half>(data.model, primitive.Attributes.GetValueOrDefault("TEXCOORD_2", -1), data.buffers);
        public ReadOnlySpan<Color> GetColors() => ReadAccessor<Color, byte>(data.model, primitive.Attributes.GetValueOrDefault("COLOR_0", -1), data.buffers);
        public ReadOnlySpan<ushort> GetIndices16() => ReadAccessor<ushort, ushort>(data.model, primitive.Indices!.Value, data.buffers);
        public ReadOnlySpan<int> GetIndices32() => ReadAccessor<int, uint>(data.model, primitive.Indices!.Value, data.buffers);

        public ReadOnlySpan<short> GetJointIndices(int groupNum) => MemoryMarshal.Cast<Short4, short>(ReadAccessor<Short4, ushort>(data.model, primitive.Attributes.GetValueOrDefault("JOINTS_" + groupNum, -1), data.buffers));
        public ReadOnlySpan<float> GetWeights(int groupNum) => MemoryMarshal.Cast<Vector4, float>(ReadAccessor<Vector4, float>(data.model, primitive.Attributes.GetValueOrDefault("WEIGHTS_" + groupNum, -1), data.buffers));

        public void CopyIndicesTo(Span<ushort> target)
        {
            if (IndicesType == Accessor.ComponentTypeEnum.UNSIGNED_INT) {
                var ind = GetIndices32();
                for (int i = 0; i < ind.Length; i++) target[i] = (ushort)ind[i];
            } else {
                GetIndices16().CopyTo(target);
            }
        }

        public void CopyIndicesTo(Span<int> target)
        {
            if (IndicesType == Accessor.ComponentTypeEnum.UNSIGNED_INT) {
                GetIndices32().CopyTo(target);
            } else {
                var ind = GetIndices16();
                for (int i = 0; i < ind.Length; i++) target[i] = ind[i];
            }
        }
    }
}
