using System.Numerics;
using System.Runtime.InteropServices;
using Assimp;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mesh;
using ReeLib.Mot;
using ReeLib.Motlist;
using ReeLib.via;

namespace ContentEditor.App.FileLoaders;

public partial class CommonMeshResource : IResourceFile
{
    private const string ShapekeyPrefix = "SHAPEKEY_";
    private const string SecondaryWeightDummyBonePrefix = "WEIGHT2_DUMMY_";

    private static readonly Quaternion ZUpToYUpRotation = new Quaternion(-0.70710677f, 0, 0, 0.70710677f);

    private static MeshFile ImportMeshFromAssimp(Assimp.Scene scene, string versionConfig)
    {
        var serializerVersion = MeshFile.GetSerializerVersion(versionConfig);
        var mesh = new MeshFile(new FileHandler());
        var srcMeshes = scene.Meshes;
        var scale = AppConfig.Settings.Import.Scale;
        if (scale <= 0) scale = 1;

        mesh.Header.BufferCount = 1;

        var mainBuffer = new MeshBuffer();
        mesh.MeshBuffer = mainBuffer;

        var occMeshes = srcMeshes.Where(m => m.Name.StartsWith("occ_")).ToList();
        var shadowMeshes = srcMeshes.Where(m => m.Name.StartsWith("shadow_lod")).ToList();
        var mainMeshes = srcMeshes.Where(m => !occMeshes.Contains(m) && !shadowMeshes.Contains(m)).ToList();
        if (shadowMeshes.Count > 0 || mainMeshes.Count > 0) {
            mesh.MeshData = new MeshData(mainBuffer);
            mesh.MeshData.boundingBox = AABB.Combine(scene.Meshes.Select(m => new AABB(m.BoundingBox.Min, m.BoundingBox.Max)));
            // TODO: sphere bounds not fully accurate
            mesh.MeshData.boundingSphere = new Sphere(mesh.MeshData.boundingBox.Center, Math.Max(mesh.MeshData.boundingBox.Size.X, Math.Max(mesh.MeshData.boundingBox.Size.Y, mesh.MeshData.boundingBox.Size.Z)) / 2);
            PreAllocateMeshBuffer(versionConfig, mesh, mainMeshes, mainBuffer);
        }
        bool reuseBufferForShadows = false;
        if (shadowMeshes.Count > 0) {
            var shadowBuffer = new MeshBuffer();
            PreAllocateMeshBuffer(versionConfig, mesh, shadowMeshes, shadowBuffer);
            if (shadowBuffer.Positions.Length == mainBuffer.Positions.Length && shadowBuffer.Faces?.Length == mainBuffer.Faces?.Length && shadowBuffer.IntegerFaces?.Length == mainBuffer.IntegerFaces?.Length) {
                Logger.Info("Shadow meshes contain same amount of vertices and faces as main mesh. Assuming identical and reusing main mesh buffer instead.");
                reuseBufferForShadows = true;
            } else {
                mainBuffer.AdditionalBuffers.Add(shadowBuffer);
            }
        }

        if (occMeshes.Count > 0) {
            var occVertCount = occMeshes.Sum(m => m.VertexCount);
            var occFaceCount = occMeshes.Sum(m => m.FaceCount);
            var occBuffer = new MeshBuffer();
            occBuffer.Positions = new Vector3[occVertCount];
            occBuffer.Faces = new ushort[occFaceCount * 3];
            mainBuffer.AdditionalBuffers.Add(occBuffer);
        }

        var maxWeights = MeshFile.GetWeightLimit(versionConfig);
        var isSixWeight = maxWeights % 6 == 0;
        bool allowExtraWeights = maxWeights > 8;

        var orderedMeshes = srcMeshes.OrderBy(m => (MeshLoader.GetMeshIndexFromName(m.Name), MeshLoader.GetMeshGroupFromName(m.Name), MeshLoader.GetSubMeshIndexFromName(m.Name)));

        foreach (var mat in scene.Materials) {
            if (string.IsNullOrEmpty(mat.Name)) continue;
            mesh.MaterialNames.Add(mat.Name);
        }
        if (occMeshes.Count > 0 && shadowMeshes.Count == 0 && mainMeshes.Count == 0) {
            mesh.MaterialNames.Clear();
        }

        var boneIndexMap = new Dictionary<string, int>();
        var deformBones = new SortedList<int, MeshBone>();
        if (mainBuffer.Weights.Length > 0) {
            var boneNames = srcMeshes.SelectMany(m => m.Bones.Select(b => b.Name)).ToHashSet();
            static void AddRecursiveBones(MeshFile file, NodeCollection children, HashSet<string> boneNames, MeshBone? parentBone, ImportSettings settings)
            {
                foreach (var node in children) {
                    if (node.Name.StartsWith(ShapekeyPrefix) || node.Name.StartsWith(SecondaryWeightDummyBonePrefix)) continue;

                    if (boneNames.Contains(node.Name)) {
                        file.BoneData ??= new();
                        var tx = Matrix4x4.Transpose(node.Transform);
                        if (settings.Scale != 0 && settings.Scale != 1) {
                            tx.Translation *= settings.Scale;
                        }
                        var bone = new MeshBone() {
                            name = node.Name,
                            index = file.BoneData.Bones.Count,
                            localTransform = tx,
                            childIndex = -1,
                            nextSibling = -1,
                            symmetryIndex = file.BoneData.Bones.Count,
                        };
                        file.BoneData.Bones.Add(bone);
                        bone.useSecondaryWeight = node.Children.Any(ch => ch.Name.StartsWith(SecondaryWeightDummyBonePrefix));
                        if (parentBone == null) {
                            if (settings.ForceRootIdentity) {
                                bone.localTransform = Matrix4x4.Identity;
                            }
                            bone.globalTransform = bone.localTransform;
                            bone.parentIndex = -1;
                            file.BoneData.RootBones.Add(bone);
                        } else {
                            bone.globalTransform = Matrix4x4.Multiply(bone.localTransform.ToSystem(), parentBone.globalTransform.ToSystem());
                            bone.Parent = parentBone;
                            bone.parentIndex = parentBone.index;
                            if (parentBone.Children.Count == 0) {
                                parentBone.childIndex = bone.index;
                            } else {
                                parentBone.Children[^1].nextSibling = bone.index;
                            }
                            parentBone.Children.Add(bone);
                        }
                        bone.inverseGlobalTransform = Matrix4x4.Invert(bone.globalTransform.ToSystem(), out var inverse) ? inverse : throw new Exception("Failed to calculate inverse bone matrix " + bone.name);
                        AddRecursiveBones(file, node.Children, boneNames, bone, settings);
                    } else if (node.Children.Count > 0) {
                        // just in case there's some weird hierarchy shenanigans going on?
                        AddRecursiveBones(file, node.Children, boneNames, parentBone, settings);
                    }
                }
            }
            AddRecursiveBones(mesh, scene.RootNode.Children, boneNames, null, AppConfig.Settings.Import);
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
            boneIndexMap = mesh.BoneData!.Bones.ToDictionary(b => b.name, b => b.index);
        }

        int vertOffset = 0;
        int indicesOffset = 0;

        var warnedBones = new HashSet<string>();
        var sortedMeshes = srcMeshes.Order(new FuncComparer<Mesh>((a, b) => {
            var type1 = occMeshes.Contains(a) ? 2 : shadowMeshes.Contains(a) ? 1 : 0;
            var type2 = occMeshes.Contains(b) ? 2 : shadowMeshes.Contains(b) ? 1 : 0;

            return type1.CompareTo(type2) * 10000 + a.Name.CompareTo(b.Name);
        })).ToList();

        foreach (var aiMesh in sortedMeshes) {
            var groupIdx = MeshLoader.GetMeshGroupFromName(aiMesh.Name);
            var meshIdx = MeshLoader.GetMeshIndexFromName(aiMesh.Name);
            var subIdx = MeshLoader.GetSubMeshIndexFromName(aiMesh.Name);

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
                var subscorePos = aiMesh.Name.IndexOf('_');
                if (subscorePos == -1 || !int.TryParse(aiMesh.Name.AsSpan().Slice("shadow_lod".Length, subscorePos), out lod)) {
                    lod = 0;
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
            } else if (aiMesh.Name.StartsWith("lod")) {
                var subscorePos = aiMesh.Name.IndexOf('_');
                if (subscorePos == -1 || !int.TryParse(aiMesh.Name.AsSpan().Slice("lod".Length, subscorePos), out lod)) {
                    lod = 0;
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

            var totalVertCount = buffer.Positions.Length;
            var vertCount = aiMesh.VertexCount;
            var faceCount = aiMesh.FaceCount;
            var indicesCount = faceCount * 3;

            // note: vert limit check shouldn't be needed here, we're letting assimp handling splitting automatically
            if (meshIdx > 0) throw new NotImplementedException($"Only one mesh per file is currently supported.");

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
            newSub.materialIndex = (ushort)aiMesh.MaterialIndex;

            group.submeshCount++;
            group.Submeshes.Add(newSub);

            if (scale != 1) {
                for (int i = 0; i < aiMesh.Vertices.Count; ++i) aiMesh.Vertices[i] *= scale;
            }

            CollectionsMarshal.AsSpan(aiMesh.Vertices).CopyTo(buffer.Positions.AsSpan(vertOffset));

            if (buffer.Normals.Length > 0) {
                for (int i = 0; i < vertCount; ++i) {
                    buffer.Normals[vertOffset + i] = aiMesh.Normals[i];
                    buffer.Tangents[vertOffset + i] = aiMesh.Tangents[i];
                    buffer.BiTangentSigns[i] = Vector3.Dot(aiMesh.BiTangents[i], Vector3.Cross(aiMesh.Normals[i], aiMesh.Tangents[i])) > 0 ? sbyte.MaxValue : sbyte.MinValue;
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

            if (buffer.Colors.Length > 0) {
                var colors = aiMesh.VertexColorChannels[0];
                for (int i = 0; i < vertCount; ++i) buffer.Colors[vertOffset + i] = Color.FromVector4(colors[i]);
            }

            for (int i = 0; i < faceCount; ++i) {
                var face = aiMesh.Faces[i];
                // note: assimp should've forced triangulation already, therefore always assume 3 indices

                buffer.Faces![indicesOffset + i * 3 + 0] = (ushort)face.Indices[0];
                buffer.Faces[indicesOffset + i * 3 + 1] = (ushort)face.Indices[1];
                buffer.Faces[indicesOffset + i * 3 + 2] = (ushort)face.Indices[2];
            }

            if (buffer.Weights.Length > 0) {
                for (int i = 0; i < aiMesh.Bones.Count; i++) {
                    var aiBone = aiMesh.Bones[i];
                    if (aiBone.Name.StartsWith(SecondaryWeightDummyBonePrefix)) continue;

                    var isShapeKey = aiBone.Name.StartsWith(ShapekeyPrefix);
                    int boneIndex;
                    if (isShapeKey) {
                        boneIndex = boneIndexMap[aiBone.Name.Replace(ShapekeyPrefix, "")];
                    } else {
                        boneIndex = boneIndexMap[aiBone.Name];
                    }
                    var hasWeight = false;
                    var targetBone = mesh.BoneData!.Bones[boneIndex];
                    var weightBuffer = isShapeKey ? buffer.ShapeKeyWeights : buffer.Weights;
                    foreach (var entry in aiBone.VertexWeights) {
                        if (entry.Weight == 0) continue;

                        var outWeight = (weightBuffer[vertOffset + entry.VertexID] ??= new(serializerVersion));
                        var weightIndex = Array.FindIndex(outWeight.boneWeights, bb => bb == 0);
                        if (weightIndex == -1 || weightIndex >= outWeight.boneIndices.Length) {
                            var fail = true;
                            if (allowExtraWeights && !isShapeKey) {
                                if (buffer.ExtraWeights == null) {
                                    buffer.ExtraWeights = new VertexBoneWeights[totalVertCount];
                                    for (int ew = 0; ew < totalVertCount; ew++) buffer.ExtraWeights[ew] = new VertexBoneWeights(serializerVersion);
                                }
                                outWeight = buffer.ExtraWeights[vertOffset + entry.VertexID];
                                weightIndex = Array.FindIndex(outWeight.boneWeights, bb => bb == 0);
                                fail = (weightIndex == -1 || weightIndex >= outWeight.boneIndices.Length);
                            }
                            if (fail) {
                                if (warnedBones.Add(aiBone.Name)) Log.Warn($"Too many weights (> {maxWeights}) for {(isShapeKey ? "SHAPEKEY" : "")} bone {aiBone.Name}. Ignoring.");
                                continue;
                            }
                        }
                        outWeight.boneIndices[weightIndex] = boneIndex;
                        outWeight.boneWeights[weightIndex] = entry.Weight;
                        if (targetBone.boundingBox.IsEmpty) targetBone.boundingBox = AABB.MaxMin;
                        targetBone.boundingBox = targetBone.boundingBox.AsAABB.Extend(Vector3.Transform(buffer.Positions[vertOffset + entry.VertexID], targetBone.inverseGlobalTransform.ToSystem()));
                        hasWeight = true;
                    }
                    if (hasWeight) deformBones.TryAdd(boneIndex, targetBone);
                }

                var hasLooseVerts = Array.IndexOf(buffer.Weights, null, vertOffset, vertCount) != -1;
                if (hasLooseVerts) throw new Exception($"Found {buffer.Weights.AsSpan(vertOffset, vertCount).ToArray().Count(w => w == null)} unweighted vertices in imported mesh {aiMesh.Name} - this is not OK");

                foreach (var wee in buffer.Weights.AsSpan(vertOffset, vertCount)) {
                    // ensure normalized weights
                    wee.NormalizeWeights();
                }
            }

            if ((indicesCount % 2) != 0) indicesOffset++; // handle padding
            vertOffset += vertCount;
            indicesOffset += indicesCount;
        }

        if (mainBuffer.Weights.Length > 0) {
            int remapIndex = 0;
            foreach (var (remap, bone) in deformBones) {
                bone.remapIndex = remapIndex++;
                mesh.BoneData!.DeformBones.Add(bone);
            }

            RemapDeformBones(mainBuffer.Weights, deformBones);

            if (mainBuffer.ExtraWeights != null) RemapDeformBones(mainBuffer.ExtraWeights, deformBones);
        }

        if (reuseBufferForShadows && mesh.ShadowMesh != null) {
            for (int lod = 0; lod < mesh.ShadowMesh.LODs.Count; ++lod) {
                mesh.ShadowMesh.LODs[lod] = mesh.MeshData!.LODs[lod];
            }
        }

        mesh.ChangeVersion(versionConfig);

        return mesh;
    }

    private static void PreAllocateMeshBuffer(string versionConfig, MeshFile mesh, List<Mesh> srcMeshes, MeshBuffer mainBuffer)
    {
        var totalVertCount = srcMeshes.Sum(m => m.VertexCount);
        var totalFaceCount = srcMeshes.Sum(m => m.FaceCount);
        var totalTriCount = totalFaceCount * 3;
        var paddedTriCount = totalTriCount + srcMeshes.Count(m => (m.FaceCount * 3) % 2 != 0);
        mainBuffer.Positions = new Vector3[totalVertCount];
        mesh.Header.flags |= ContentFlags.EnableRebraiding2;
        if (srcMeshes.All(m => m.HasNormals && m.HasTangentBasis)) {
            mainBuffer.Normals = new Vector3[totalVertCount];
            mainBuffer.Tangents = new Vector3[totalVertCount];
            mainBuffer.BiTangentSigns = new sbyte[totalVertCount];
        }
        if (srcMeshes.All(m => m.HasTextureCoords(0))) mainBuffer.UV0 = new Vector2[totalVertCount];
        if (srcMeshes.All(m => m.HasTextureCoords(1))) mainBuffer.UV1 = new Vector2[totalVertCount];
        if (srcMeshes.All(m => m.Bones.Count > 0 && m.Bones.Any(b => b.HasVertexWeights))) {
            mainBuffer.Weights = new VertexBoneWeights[totalVertCount];
            mesh.Header.flags |= ContentFlags.IsSkinning | ContentFlags.HasJoint;
        }
        if (srcMeshes.Any(m => m.Bones.Any(b => b.Name.StartsWith(ShapekeyPrefix)))) {
            mainBuffer.ShapeKeyWeights = new VertexBoneWeights[totalVertCount];
            mesh.Header.flags |= ContentFlags.HasVertexGroup;
        }
        if (srcMeshes.All(m => m.HasVertexColors(0))) {
            mainBuffer.Colors = new Color[totalVertCount];
            mesh.Header.flags |= ContentFlags.HasVertexColor;
        }
        mainBuffer.Faces = new ushort[paddedTriCount];
    }

    private static void RemapDeformBones(VertexBoneWeights[] weights, SortedList<int, MeshBone> deformBones)
    {
        foreach (var weight in weights) {
            for (int i = 0; i < weight.boneIndices.Length; ++i) {
                var index = weight.boneIndices[i];
                if (index == 0) {
                    if (i > 0 && weight.boneWeights[i] == 0) {
                        weight.boneIndices[i] = weight.boneIndices[i - 1];
                    }
                } else if (deformBones.TryGetValue(index, out var remap)) {
                    weight.boneIndices[i] = remap.remapIndex;
                }
            }
        }
    }

    private static string CleanBoneName(string name)
    {
        // sometimes assimp is an ass and adds random shit to the bone names
        return name.Replace("_$AssimpFbx$_Translation", "").Replace("_$AssimpFbx$_Rotation", "").Replace("_$AssimpFbx$_Scaling", "");
    }

    private MotlistFile ImportAnimationsFromAssimp(Assimp.Scene scene, GameName gameVersion)
    {
        workspace.TryGetFileExtensionVersion("motlist", out var version);
        var motlist = new MotlistFile(new FileHandler() { FileVersion = version });
        motlist.Header.version = (ReeLib.Motlist.MotlistVersion)version;
        motlist.Header.MotListName = Name;
        var motver = motlist.Header.version.GetMotVersion();
        var meta = scene.Metadata;

        float scale = AppConfig.Settings.Import.Scale;

        // setup mot bone hierarchy
        var boneNames = scene.Animations
            .SelectMany(a => a.NodeAnimationChannels.Select(ann => CleanBoneName(ann.NodeName)))
            .Where(name => !name.StartsWith(SecondaryWeightDummyBonePrefix) && !name.StartsWith(ShapekeyPrefix))
            .ToHashSet();
        static void AddRecursiveBones(List<MotBone> bones, NodeCollection children, HashSet<string> boneNames, MotBone? parentBone, float scale)
        {
            foreach (var node in children) {
                if (boneNames.Contains(node.Name)) {
                    var bone = new MotBone(){ boneName = node.Name, boneHash = MurMur3HashUtils.GetHash(node.Name), Index = bones.Count };
                    var localMatrix = Matrix4x4.Transpose(node.Transform);
                    if (!Matrix4x4.Decompose(localMatrix, out _, out bone.quaternion, out bone.translation)) {
                        Logger.Error("Failed to decompose bone offset");
                    }
                    bone.translation *= scale;
                    if (bone.quaternion.W < 0) {
                        bone.quaternion = Quaternion.Negate(bone.quaternion);
                    }

                    bones.Add(bone);
                    bone.Parent = parentBone;
                    parentBone?.Children.Add(bone);
                    AddRecursiveBones(bones, node.Children, boneNames, bone, scale);
                } else if (node.Children.Count > 0) {
                    // just in case there's some weird hierarchy shenanigans going on?
                    AddRecursiveBones(bones, node.Children, boneNames, parentBone, scale);
                }
            }
        }
        List<MotBone> motBones = new();
        AddRecursiveBones(motBones, scene.RootNode.Children, boneNames, null, scale);
        var rootBones = motBones.Where(b => b.Parent == null).ToList();
        List<string> orderedBoneNames = motBones.Select(b => b.boneName).ToList();

        foreach (var aiAnim in scene.Animations) {
            if (!aiAnim.HasNodeAnimations) continue;

            var mot = new MotFile(motlist.FileHandler);
            mot.Name = aiAnim.Name;
            mot.Header.version = motver;
            mot.Bones.AddRange(motBones);
            mot.RootBones.AddRange(rootBones);
            if (mot.Name.Contains("_loop")) {
                mot.Header.blending = 0;
            }

            double sourceFps = aiAnim.TicksPerSecond;
            ushort targetFps = 60;
            if (sourceFps == 0) sourceFps = 60;

            var duration = aiAnim.DurationInTicks / sourceFps;
            mot.Header.frameCount = (float)Math.Floor(duration * targetFps);
            mot.Header.endFrame = mot.Header.frameCount;
            mot.Header.FrameRate = targetFps;
            motlist.MotFiles.Add(mot);
            var motIndex = new MotIndex(motlist.Header.version) { MotFile = mot, motNumber = (ushort)motlist.MotFiles.Count };
            motlist.Motions.Add(motIndex);

            var settings = AppConfig.Settings.Import;

            double timeScale = targetFps / sourceFps;

            // TODO add compression calculation method somewhere
            foreach (var channel in aiAnim.NodeAnimationChannels.OrderBy(ch => orderedBoneNames.IndexOf(CleanBoneName(ch.NodeName)))) {
                var boneName = CleanBoneName(channel.NodeName);
                if (!orderedBoneNames.Contains(boneName)) continue;

                var existingClip = mot.BoneClips.FirstOrDefault(c => c.ClipHeader.boneName == boneName);
                var clipHeader = existingClip?.ClipHeader ?? new BoneClipHeader(motver);
                var clip = existingClip ?? new BoneMotionClip(clipHeader);

                if (channel.NodeName.Contains("_$AssimpFbx$_")) {
                    // if the bone is supposed to be a specific channel type, drop all other types because they're clearly not supposed to be there for this assimp channel
                    if (channel.NodeName.Contains("_$AssimpFbx$_Translation")) {
                        clip.Translation = null;
                        if (clip.HasRotation) channel.RotationKeys.Clear();
                        if (clip.HasScale) channel.ScalingKeys.Clear();
                    } else if (channel.NodeName.Contains("_$AssimpFbx$_Rotation")) {
                        clip.Rotation = null;
                        if (clip.HasTranslation) channel.PositionKeys.Clear();
                        if (clip.HasScale) channel.ScalingKeys.Clear();
                    } else if (channel.NodeName.Contains("_$AssimpFbx$_Scaling")) {
                        clip.Scale = null;
                        if (clip.HasTranslation) channel.PositionKeys.Clear();
                        if (clip.HasRotation) channel.RotationKeys.Clear();
                    }
                }

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
                if (channel.HasPositionKeys) {
                    var firstValue = bone != null ? bone.translation : channel.PositionKeys[0].Value;
                    var allEqual = bone != null && !channel.PositionKeys.Any(k => Vector3.DistanceSquared(k.Value, firstValue) > 0.000001f);
                    var track = new Track(motver, TrackValueType.Vector3);
                    clipHeader.trackFlags |= TrackFlag.Translation;
                    track.maxFrame = (float)(channel.PositionKeys.Last().Time * timeScale);
                    track.frameRate = targetFps;
                    track.frameIndexes = new int[channel.PositionKeyCount];
                    track.translations = new Vector3[channel.PositionKeyCount];
                    track.keyCount = channel.PositionKeyCount;
                    for (int i = 0; i < channel.PositionKeyCount; ++i) {
                        var key = channel.PositionKeys[i];
                        track.frameIndexes[i] = (int)Math.Round(key.Time * timeScale);
                        track.translations[i] = key.Value * scale;
                    }
                    // additional hack because some fbx files have duplicate root bones but all of them have all key types and we don't want to overwrite them
                    if (!allEqual || !clip.HasTranslation) clip.Translation = track;
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

                if (channel.HasRotationKeys) {
                    if (settings.ConvertZToYUpRootRotation && (bone != null ? bone.Parent == null : boneName.Equals("root", StringComparison.InvariantCultureIgnoreCase))) {
                        for (int i = 0; i < channel.RotationKeyCount; ++i) {
                            var quat = Quaternion.Multiply(ZUpToYUpRotation, channel.RotationKeys[i].Value);
                            channel.RotationKeys[i] = new QuaternionKey(channel.RotationKeys[i].Time, quat);
                        }
                    }

                    var firstValue = bone != null ? bone.quaternion : channel.RotationKeys[0].Value;
                    var allEqual = bone != null && !channel.RotationKeys.Any(k => k.Value != firstValue);
                    clipHeader.trackFlags |= TrackFlag.Rotation;
                    var track = clip.Rotation = new Track(motver, TrackValueType.Quaternion);
                    track.maxFrame = (float)(channel.RotationKeys.Last().Time * timeScale);
                    track.frameRate = targetFps;
                    track.frameIndexes = new int[channel.RotationKeyCount];
                    track.rotations = new Quaternion[channel.RotationKeyCount];
                    track.RotationCompressionType = QuaternionDecompression.LoadQuaternions3Component;
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
                    if (!allEqual || !clip.HasRotation) clip.Rotation = track;
                }

                if (clip.ClipHeader.trackFlags != 0 && existingClip == null) {
                    mot.BoneClips.Add(clip);
                }
            }
        }
        return motlist;
    }
}
