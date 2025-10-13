using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Common;

namespace ContentEditor.App;

public class RetargetDesigner : BaseWindowHandler
{
    public override string HandlerName => "Retarget Designer";

    public override bool HasUnsavedChanges => false;

    // private string mesh1 = "";
    // private string mesh1 = "E:/mods/re2r/chunks/natives/stm/sectionroot/character/player/pl0000/pl0000/pl0000.mesh.2109108288";
    private string mesh1 = "E:/mods/mhrise/Ver.R Knight Heavy/natives/stm/player/mod/f/pl371/f_body371.mesh.2109148288";
    // private string mesh1 = "E:/mods/mhrise/Ver.R Knight Heavy/natives/stm/player/mod/f/pl371/f_leg371.mesh.2109148288";
    private string loadedMesh1 = "";
    // private string mesh2 = "character/_kit/body/body_000_m/body_000_m.mesh";
    // private string mesh2 = "";
    // private string mesh2 = "E:/mods/re4/chunks/natives/stm/_chainsaw/character/ch/cha0/cha000/00/cha000_00.mesh.221108797";
    private string mesh2 = "E:/mods/dd2/REtool/re_chunk_000/natives/stm/character/_kit/body/body_000_m/body_000_m.mesh.240423143";
    private string loadedMesh2 = "";
    // private string sampleMotlist = "";
    // private string sampleMotlist = "E:/mods/re2r/chunks/natives/stm/sectionroot/animation/player/pl20/list/cmn/base_cmn_move.motlist.524";
    private string sampleMotlist = "C:/Program Files (x86)/Steam/steamapps/common/Dragons Dogma 2/reframework/data/usercontent/bundles/Mot testing/plw_insectglaive_100.motlist.528";
    private string loadedMotlist = "";
    // TODO need to add a target motlist to use as a base for the bones list (rest poses)
    // alternatively hack one up from the mesh?
    // private string motFile = "";
    private string motFile = "pl10_0170_KFF_Gazing_Idle_F_Loop";
    private string remapConfig = "";
    private string boneFilter = "";

    private MotFile? selectedMotion;
    private MotFile? retargetedMotion;

    private MeshViewer? meshViewer1;
    private MeshViewer? meshViewer2;

    private string[]? remapOptions;
    private string[]? motFileOptions;
    private Dictionary<string, MotRetargetConfig>? remaps;
    private bool hideSetup;
    private bool forceRefreshConfig = true;
    private static readonly string[] unmappedName = new string[]{"<unmapped>"};

    public override void OnIMGUI()
    {
        if (context.children.Count == 0) {
            context.AddChild<RetargetDesigner, string>(
                "Source Mesh",
                this,
                new ResourcePathPicker(workspace, KnownFileFormats.Mesh) { UseNativesPath = true, IsPathForIngame = false },
                (v) => v!.mesh1,
                (v, p) => v.mesh1 = p ?? "");
            context.AddChild<RetargetDesigner, string>(
                "Target Mesh",
                this,
                new ResourcePathPicker(workspace, KnownFileFormats.Mesh) { UseNativesPath = true, IsPathForIngame = false },
                (v) => v!.mesh2,
                (v, p) => v.mesh2 = p ?? "");
            context.AddChild<RetargetDesigner, string>(
                "Reference motlist",
                this,
                new ResourcePathPicker(workspace, workspace.Env.TypeCache.GetResourceSubtypes(KnownFileFormats.MotionBase)) { UseNativesPath = true, IsPathForIngame = false },
                (v) => v!.sampleMotlist,
                (v, p) => {
                    v.sampleMotlist = p ?? "";
                    v.motFileOptions = null;
                });
        }

        if (!hideSetup) {
            var ctxSrc = context.children[0];
            var ctxDst = context.children[1];
            var ctxMlst = context.children[2];

            var w = ImGui.CalcItemWidth();
            ImGui.PushItemWidth(w / 2 - ImGui.GetStyle().FramePadding.X * 2);
            ctxSrc.ShowUI();
            ImGui.SameLine();
            ctxDst.ShowUI();
            ImGui.PopItemWidth();
            ctxMlst.ShowUI();
        }

        if (!string.IsNullOrEmpty(mesh1) && (meshViewer1 == null || mesh1 != loadedMesh1) && workspace.ResourceManager.TryResolveFile(mesh1, out var fh)) {
            loadedMesh1 = mesh1;
            if (meshViewer1 == null) {
                meshViewer1 = new MeshViewer(workspace, fh);
                meshViewer1.Init(context);
            } else {
                meshViewer1.ChangeMesh(mesh1);
            }
        }

        if (!string.IsNullOrEmpty(mesh2) && (meshViewer2 == null || mesh2 != loadedMesh2)) {
            loadedMesh2 = mesh2;
            if (workspace.ResourceManager.TryResolveFile(mesh2, out fh)) {
                if (meshViewer2 == null) {
                    meshViewer2 = new MeshViewer(workspace, fh);
                    meshViewer2.Init(context);
                } else {
                    meshViewer2.ChangeMesh(mesh2);
                }
            } else {
                meshViewer2?.Dispose();
                meshViewer2 = null;
            }
        }

        if (motFileOptions == null) {
            if (string.IsNullOrEmpty(sampleMotlist)) {
                ImGui.TextColored(Colors.Note, "Please select a valid mot or motlist file");
            } else if (meshViewer1 != null && loadedMotlist != sampleMotlist) {
                loadedMotlist = sampleMotlist;
                if (meshViewer1.Animator == null) {
                    meshViewer1.SetAnimation(sampleMotlist);
                } else {
                    meshViewer1.Animator!.LoadAnimationList(sampleMotlist);
                }

                motFileOptions = meshViewer1.Animator!.AnimationNames.ToArray();
            }
        }
        if (!hideSetup && motFileOptions?.Length > 0) {
            if (ImguiHelpers.ValueCombo("Sample motion", motFileOptions, motFileOptions, ref motFile)) {
                meshViewer1!.Animator!.SetActiveMotion(motFile);
                if (meshViewer1!.Animator.ActiveMotion != null) {
                    meshViewer2?.SetAnimation(meshViewer1!.Animator.ActiveMotion);
                }
            }
        }

        if (ImGui.ArrowButton("##hideSetup", hideSetup ? ImGuiDir.Down : ImGuiDir.Up)) {
            hideSetup = !hideSetup;
        }
        if (retargetedMotion != null && ImguiHelpers.SameLine() && ImGui.TreeNode("Mot data")) {
            new MotFileHandler().OnIMGUI(UIContext.CreateRootContext("DATA", retargetedMotion));
            ImGui.TreePop();
        }
        ImGui.SameLine();
        if (ImGui.Button("Reload configs")) {
            remaps = null;
            remapOptions = null;
        }
        ImGui.SameLine();
        var createNew = ImGui.Button("Create new config");
        if (remaps == null) {
            remaps = RetargetDesigner.LoadRetargetingConfigs();
        }
        if (remapOptions == null) {
            remapOptions = remaps.Values.SelectMany(v => v.Renames.Select(r => r.Name)).Order().ToArray();
        }

        ImGui.SameLine();
        if (ImguiHelpers.ValueCombo("Remap config", remapOptions, remapOptions, ref remapConfig)) {
            forceRefreshConfig = true;
        }
        if (createNew) {
            remaps[remaps.First().Key].Renames.Add(new MotRetargetNamesConfig() { Name = remapConfig = "New Remap " + System.Random.Shared.Next(0, 100), Version1 = ["src"], Version2 = ["target"], Type = remaps.First().Key });
            remapOptions = null;
        }
        var selectedRemap = remaps.Select(rr => rr.Value.Renames.FirstOrDefault(v => v.Name == remapConfig)).FirstOrDefault(x => x != null);
        var updateRetargets = ShowRetargetList(selectedRemap, meshViewer1?.Mesh, meshViewer2?.Mesh);

        var fullW = (ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X * 2) / 2;
        ImGui.BeginChild("MeshViewerSource", new System.Numerics.Vector2(fullW, 0));
        ImGui.BeginDisabled();
        meshViewer1?.OnIMGUI();
        ImGui.EndDisabled();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("MeshViewerTarget", new System.Numerics.Vector2(fullW, 0));
        meshViewer2?.OnIMGUI();
        ImGui.EndChild();

        if (meshViewer1 != null && meshViewer2 != null) {
            meshViewer1.SyncFromScene(meshViewer2, true);
            if (meshViewer1.Animator == null || meshViewer2.Animator == null) return;

            if (meshViewer1.Animator.ActiveMotion != null && (updateRetargets || selectedMotion != meshViewer1.Animator.ActiveMotion)) {
                selectedMotion = meshViewer1.Animator.ActiveMotion;
                retargetedMotion?.Dispose();
                retargetedMotion = null;
                retargetedMotion = selectedMotion.RewriteClone(workspace);
                if (selectedRemap != null) {
                    ExecuteRemap(retargetedMotion, selectedRemap, selectedRemap.Version1.First(), 2);
                }
                forceRefreshConfig = false;
                var time = meshViewer2.Animator.CurrentTime;
                meshViewer2.Animator.SetActiveMotion(retargetedMotion);
                meshViewer2.Animator.Seek(time);
                meshViewer2.Animator.Update(0);
            }
            meshViewer1.SyncFromScene(meshViewer2, true);
        }
    }

    private bool ShowRetargetList(MotRetargetNamesConfig? selectedRemap, CommonMeshResource? mesh1, CommonMeshResource? mesh2)
    {
        if (selectedRemap == null || mesh1?.NativeMesh.BoneData == null || mesh2?.NativeMesh.BoneData == null) return false;

        if (ImGui.Button("Force refresh")) {
            forceRefreshConfig = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Copy as JSON")) {
            var data = JsonSerializer.Serialize(selectedRemap, JsonConfig.jsonOptionsIncludeFields);
            EditorWindow.CurrentWindow!.CopyToClipboard(data);
        }
        ImGui.SameLine();
        if (ImGui.Button("Auto-compute Transformation Matrices")) {
            ComputeTranforms(selectedRemap, mesh1, mesh2);
        }

        ImGui.SameLine();
        ImGui.InputText("Filter bones...", ref boneFilter, 100);
        var changed = false;
        ImGui.BeginChild("retargetBones", new Vector2(0, 200));
        var targetBones = unmappedName.Concat(mesh2.NativeMesh.BoneData.Bones.Select(b => b.name)).ToArray();
        var srcBones = mesh1.NativeMesh.BoneData.Bones.Select(b => b.name);
        if (selectedMotion != null) srcBones = srcBones.Concat(selectedMotion.Bones.Select(b => b.Name)).Distinct();
        if (ImGui.BeginTable("##retarget_bones", 5, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterV | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY)) {
            ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 0.075f);
            ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch, 0.075f);
            ImGui.TableSetupColumn("Base Rotation", ImGuiTableColumnFlags.WidthStretch, 0.3f);
            ImGui.TableSetupColumn("Local Rotation", ImGuiTableColumnFlags.WidthStretch, 0.3f);
            ImGui.TableSetupColumn("Translation", ImGuiTableColumnFlags.WidthStretch, 0.25f);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            foreach (var srcBone in srcBones.Order()) {
                if (!string.IsNullOrEmpty(boneFilter) && !srcBone.Contains(boneFilter, StringComparison.InvariantCultureIgnoreCase)) continue;

                ImGui.TableNextColumn();
                ImGui.PushID(srcBone);
                ImGui.Text(srcBone);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                var map = selectedRemap.Maps.FirstOrDefault(m => m.Bone1 == srcBone);
                var targetName = map?.Bone2;
                // ImGui.SetNextItemWidth(200);
                if (ImguiHelpers.ValueCombo("##Target", targetBones, targetBones, ref targetName)) {
                    changed = true;
                    if (targetName == unmappedName[0]) targetName = null;
                    var alreadyMappedTarget = selectedRemap.Maps.FirstOrDefault(d => d.Bone2 == targetName);
                    if (alreadyMappedTarget != null) {
                        // verify if the bone was already mapped to a different one - they need to be 1:1
                        var msg = $"Can't map {srcBone} to {targetName}. Bone is already mapped from {alreadyMappedTarget.Bone1}";
                        Logger.Error(msg);
                        EditorWindow.CurrentWindow?.Overlays.ShowTooltip(msg, 3);
                    } else {
                        if (map == null) {
                            if (targetName != null) {
                                map = new MotRetargetNameMap() { Bone1 = srcBone, Bone2 = targetName };
                                UndoRedo.RecordCallback(null, () => {selectedRemap.Maps.Add(map); forceRefreshConfig = true; }, () => {selectedRemap.Maps.Remove(map); forceRefreshConfig = true;}, $"{map.GetHashCode()} a");
                            }
                        } else if (targetName == null) {
                            UndoRedo.RecordCallback(null, () => {selectedRemap.Maps.Remove(map); forceRefreshConfig = true;}, () => {selectedRemap.Maps.Add(map); forceRefreshConfig = true; }, $"{map.GetHashCode()} b");
                        } else {
                            UndoRedo.RecordCallbackSetter(null, map, map.Bone2, targetName, (mm, vv) => {mm.Bone2 = vv; forceRefreshConfig = true; }, $"{map.GetHashCode()} s");
                        }
                    }
                }
                if (map != null) {
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                    var r = map.rotation.ToVector4();
                    if (r == Vector4.Zero) r = new Vector4(0, 0, 0, 1);
                    if (ImGui.DragFloat4("##Rotation", ref r, 0.001f)) {
                        var q = Quaternion.Normalize(r.ToQuaternion());
                        if (float.IsNaN(q.X)) q = Quaternion.Identity;
                        UndoRedo.RecordCallbackSetter(null, map, map.rotation, q, (mm, vv) => {mm.rotation = vv; forceRefreshConfig = true;}, $"{map.GetHashCode()} r");
                        changed = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                    r = map.baseRotation.ToVector4();
                    if (r == Vector4.Zero) r = new Vector4(0, 0, 0, 1);
                    if (ImGui.DragFloat4("##BaseRotation", ref r, 0.001f)) {
                        var q = Quaternion.Normalize(r.ToQuaternion());
                        if (float.IsNaN(q.X)) q = Quaternion.Identity;
                        UndoRedo.RecordCallbackSetter(null, map, map.baseRotation, q, (mm, vv) => {mm.baseRotation = vv; forceRefreshConfig = true;}, $"{map.GetHashCode()} r2");
                        changed = true;
                    }
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                    var t = map.translation;
                    if (ImGui.DragFloat3("##Translation", ref t, 0.001f)) {
                        UndoRedo.RecordCallbackSetter(null, map, map.translation, t, (mm, vv) => {mm.translation = vv; forceRefreshConfig = true;}, $"{map.GetHashCode()} t");
                        changed = true;
                    }
                } else {
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                }
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();

        return changed || forceRefreshConfig;
    }

    private void ComputeTranforms(MotRetargetNamesConfig selectedRemap, CommonMeshResource mesh1, CommonMeshResource mesh2)
    {
        var computedOutTransforms = new Dictionary<string, Matrix4x4>();
        foreach (var map in selectedRemap.Maps) {
            var bone1 = mesh1.NativeMesh.BoneData?.GetByName(map.Bone1);
            var bone2 = mesh2.NativeMesh.BoneData?.GetByName(map.Bone2);
            if (bone1 == null || bone2 == null) continue;


        }
    }

    public static Dictionary<string, MotRetargetConfig> LoadRetargetingConfigs()
    {
        Dictionary<string, MotRetargetConfig> configs;
        var path = Path.Combine(AppContext.BaseDirectory, "configs/mot_retargeting");
        configs = new();
        foreach (var file in Directory.EnumerateFiles(path, "*.json")) {
            if (file.TryDeserializeJsonFile<MotRetargetConfig>(out var config, out var error, JsonConfig.jsonOptionsIncludeFields)) {
                foreach (var cfg in config.Renames) {
                    if (!configs.TryGetValue(cfg.Type, out var stored)) {
                        configs[cfg.Type] = stored = new MotRetargetConfig();
                    }
                    stored.Renames.Add(cfg);
                }
                foreach (var cfg in config.Parents) {
                    if (!configs.TryGetValue(cfg.Type, out var stored)) {
                        configs[cfg.Type] = stored = new MotRetargetConfig();
                    }

                    stored.Parents.Add(cfg);
                }
            } else {
                Logger.Error("Could not load retargeting config " + file);
            }
        }
        return configs;
    }

    public static List<string> ExecuteRemap(MotFile mot, MotRetargetNamesConfig remapConfig, string source, int directionTieBreaker)
    {
        // TODO do we need to worry about bone indices?
        // NOTE: we're not modifying the mot.Bones/mot.RootBones - most motlist versions only have the list on mot 1
        var mapToBone1 = remapConfig.Version2.Contains(source);
        Dictionary<string, MotRetargetNameMap> bonemap;
        int direction;
        if (remapConfig.Version2.Contains(source) && (!remapConfig.Version1.Contains(source) || directionTieBreaker == 1)) {
            bonemap = remapConfig.Maps.ToDictionary(kv => kv.Bone2, kv => kv);
            direction = 1;
        } else {
            bonemap = remapConfig.Maps.ToDictionary(kv => kv.Bone1, kv => kv);
            direction = 2;
        }

        var boneHashes = bonemap.ToDictionary(kv => MurMur3HashUtils.GetHash(kv.Key), kv => kv.Value);
        var renames = new List<string>();

        if (mot.BoneHeaders == null) {
            Logger.Error("Mot file contains no bone data");
            return renames;
        }

        foreach (var bone in mot.BoneHeaders) {
            if (bonemap.TryGetValue(bone.boneName, out var newName) || boneHashes.TryGetValue(bone.boneHash, out newName)) {
                renames.Add((bone.boneName ?? bone.boneHash.ToString()) + " => " + newName);
                if (direction == 1) {
                    bone.boneName = newName.Bone1;
                    bone.boneHash = MurMur3HashUtils.GetHash(newName.Bone1);
                    bone.translation -= newName.translation;
                    if (newName.baseRotation != Quaternion.Zero) {
                        bone.translation = Vector3.Transform(bone.translation, newName.baseRotation);
                    }
                    if (newName.rotation != Quaternion.Zero) {
                        bone.quaternion = Quaternion.Inverse(newName.rotation) * bone.quaternion;
                    }
                } else {
                    bone.boneName = newName.Bone2;
                    bone.boneHash = MurMur3HashUtils.GetHash(newName.Bone2);
                    bone.translation += newName.translation;
                    if (newName.baseRotation != Quaternion.Zero) {
                        bone.translation = Vector3.Transform(bone.translation, newName.baseRotation);
                    }
                    if (newName.rotation != Quaternion.Zero) {
                        bone.quaternion = newName.rotation * bone.quaternion;
                    }
                }
            }
        }

        foreach (var clip in mot.BoneClips) {
            if (clip.ClipHeader.boneName != null && bonemap.TryGetValue(clip.ClipHeader.boneName, out var newName) || boneHashes.TryGetValue(clip.ClipHeader.boneHash, out newName)) {
                renames.Add((clip.ClipHeader.boneName ?? clip.ClipHeader.boneHash.ToString()) + " => " + newName);
                Quaternion rotation = newName.rotation == Quaternion.Zero ? Quaternion.Identity : newName.rotation;
                Quaternion baseRotation = newName.baseRotation == Quaternion.Zero ? Quaternion.Identity : newName.baseRotation;
                Vector3 translation = newName.translation;
                if (direction == 1) {
                    clip.ClipHeader.boneName = newName.Bone1;
                    clip.ClipHeader.boneHash = MurMur3HashUtils.GetHash(newName.Bone1);
                    rotation = Quaternion.Inverse(rotation);
                    baseRotation = Quaternion.Inverse(rotation);
                    translation = -translation;
                } else {
                    clip.ClipHeader.boneName = newName.Bone2;
                    clip.ClipHeader.boneHash = MurMur3HashUtils.GetHash(newName.Bone2);
                }

                if (clip.HasRotation) {
                    for (int i = 0; i < clip.Rotation!.rotations!.Length; i++) {
                        clip.Rotation.rotations![i] = Quaternion.Inverse(rotation) * baseRotation * clip.Rotation.rotations![i];
                    }
                }
                // if (clip.HasTranslation && (translation != Vector3.Zero || baseRotation != Quaternion.Identity)) {
                if (clip.HasTranslation) {
                    // var effTranslation = Vector3.Transform(translation, effRotation);
                    for (int i = 0; i < clip.Translation!.translations!.Length; i++) {
                        // clip.Translation.translations![i] = Vector3.Transform(clip.Translation.translations![i], effRotation) + effTranslation;
                        clip.Translation.translations![i] = Vector3.Transform(clip.Translation.translations![i] + translation, baseRotation);
                        // clip.Translation.translations![i] = clip.Translation.translations![i] + effTranslation;
                    }
                }
            }
        }

        return renames;
    }
}

public class MotRetargetConfig
{
    [JsonPropertyName("renames")]
    public List<MotRetargetNamesConfig> Renames { get; set; } = [];

    [JsonPropertyName("reparenting")]
    public List<MotRetargetParentsConfig> Parents { get; set; } = [];
}

public class MotRetargetNamesConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("version1")]
    public string[] Version1 { get; set; } = [];
    [JsonPropertyName("version2")]
    public string[] Version2 { get; set; } = [];
    [JsonPropertyName("maps")]
    public List<MotRetargetNameMap> Maps { get; set; } = [];
}

public class MotRetargetNameMap
{
    [JsonPropertyName("bone1")]
    public string Bone1 { get; set; } = "";
    [JsonPropertyName("bone2")]
    public string Bone2 { get; set; } = "";
    [JsonPropertyName("rotation")]
    public Quaternion rotation { get; set; }
    [JsonPropertyName("baseRotation")]
    public Quaternion baseRotation { get; set; }
    [JsonPropertyName("translation")]
    public Vector3 translation { get; set; }

    public override string ToString() => $"{Bone1} => {Bone2}";
}

public class MotRetargetParentsConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("versions")]
    public string[] Versions { get; set; } = [];
    [JsonPropertyName("parents")]
    public Dictionary<string, string> Parents = new();
}
