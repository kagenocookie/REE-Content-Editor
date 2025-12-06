using System.Numerics;
using System.Text.Json.Serialization;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Common;

namespace ContentEditor.App.Widgets;

internal class MotFileActionHandler(IObjectUIHandler inner) : IObjectUIHandler
{
    private MotlistRetargetWindow? retargetWindow;

    public void OnIMGUI(UIContext context)
    {
        var show = ImguiHelpers.TreeNodeSuffix(context.label, context.GetRaw()?.ToString() ?? string.Empty);
        if (ImGui.BeginPopupContextItem(context.label)) {
            if (ShowContextMenuItems(context)) {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (show) {
            inner.OnIMGUI(context);
            ImGui.TreePop();
        }
        if (retargetWindow?.Show() == false) {
            retargetWindow = null;
        }
    }

    private bool ShowContextMenuItems(UIContext context)
    {
        if (ImGui.Selectable("Copy motion data")) {
            var mot = context.Get<MotFileBase>();
            MotionDataResource motData;
            if (mot is MotFile mf) {
                motData = new MotionDataResource(mf);
            } else {
                Logger.Error("Unsupported mot type");
                return true;
            }

            var ws = context.GetWorkspace();
            if (Logger.ErrorIf(ws == null)) return true;
            try {
                // try and add a failsafe in case somehow the paste later fails for reasons unknown
                var motCopy = motData.ToMotFile();
                if (motCopy == null) throw new NullReferenceException("File is null");
                motData.VerificationId = VirtualClipboard.Store(motCopy);
            } catch (Exception e) {
                Logger.Error("Failed to copy motion data: " + e.Message);
                return true;
            }
            EditorWindow.CurrentWindow?.CopyToClipboard(motData.ToJsonString(ws.Env), "Copied motion " + motData.MotName + " to clipboard (JSON)");
            return true;
        }

        var clipData = EditorWindow.CurrentWindow?.GetClipboard();
        if (!string.IsNullOrEmpty(clipData)) {
            var paste = ImGui.Selectable("Paste motion data");
            var pasteRetarget = ImGui.Selectable("Paste motion data (Retargeting) ...");
            paste = paste || pasteRetarget;
            if (paste) {
                if (MotionDataResource.TryDeserialize(clipData, out var motData, out var error)) {
                    var editor = context.FindHandlerInParents<MotlistEditor>();
                    var motlist = editor?.File;
                    if (Logger.ErrorIf(motlist == null, "Could not find parent motlist")) return true;

                    var prevMot = context.Get<MotFileBase>();
                    MotFileBase newMot;
                    try {
                        newMot = motData.ToMotFile()!;
                        if (newMot == null) throw new NullReferenceException("File is null");
                    } catch (Exception e) {
                        Logger.Warn($"Pasted data is invalid ({e.Message}), attempting virtual clipboard...");
                        if (!VirtualClipboard.TryGetById(motData.VerificationId, out newMot!)) {
                            Logger.Error($"Pasted data is invalid.");
                            return true;
                        }
                    }
                    if (newMot == null) return true;

                    if (pasteRetarget) {
                        retargetWindow = new MotlistRetargetWindow(motlist, prevMot, newMot, editor);
                    } else {
                        ConfirmPaste(motlist, prevMot, newMot, editor);
                    }
                } else {
                    Logger.Error("Failed to deserialize motion data: " + error);
                }
                return true;
            }
        }

        if (ImGui.Selectable("Retargeting ...")) {
            var editor = context.FindHandlerInParents<MotlistEditor>();
            var motlist = editor?.File;
            if (Logger.ErrorIf(motlist == null, "Could not find parent motlist")) return true;
            var mot = context.Get<MotFileBase>();
            retargetWindow = new MotlistRetargetWindow(motlist, mot, mot, editor);
        }

        if (ImGui.Selectable("Save as single mot ...")) {
            var mot = context.Get<MotFileBase>();
            var ext = ".mot." + (int)((mot as MotFile)?.Header.version ?? 0);
            PlatformUtils.ShowSaveFileDialog((path) => {
                mot.WriteTo(path);
            }, filter: $"{ext}|{ext}");
        }

        return false;
    }

    private class MotlistRetargetWindow(MotlistFile motlist, MotFileBase replacedFile, MotFileBase newFile, MotlistEditor? editor)
    {
        public static readonly string WindowName = "Motion Retargeting";
        private bool hasShown;

        private Dictionary<string, MotRetargetConfig> configs = null!;
        private string[] MapTypes = [];

        private string selectedArmatureType = "";
        private string sourceType = "";
        private string targetRenameConfig = "";
        // private string parentRemapType = "";
        private bool mapToBone1;

        private static string? lastSelectedType;
        private static string? lastSelectedSource;
        private static string? lastSelectedTarget;

        private void LoadConfigs()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "configs/mot_retargeting");
            configs = RetargetDesigner.LoadRetargetingConfigs();
            selectedArmatureType = lastSelectedType ?? "";
            sourceType = lastSelectedSource ?? "";
            targetRenameConfig = lastSelectedTarget ?? "";
            MapTypes = configs.Select(c => c.Key).Distinct().ToArray();
        }

        public bool Show()
        {
            if (!hasShown) {
                hasShown = true;
                LoadConfigs();
                ImGui.OpenPopup(MotlistRetargetWindow.WindowName);
            }

            var keepShowing = true;
            ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, ImguiHelpers.GetColor(ImGuiCol.ModalWindowDimBg) with { W = 0.5f });
            if (ImGui.BeginPopupModal(WindowName, ImGuiWindowFlags.AlwaysAutoResize)) {
                if (newFile == replacedFile) {
                    ImGui.Text("Retarget mot " + newFile);
                } else {
                    ImGui.Text("Attempting to retarget mot " + newFile + " over mot " + replacedFile);
                }
                ImGui.Spacing();
                ImGui.Spacing();
                ImGui.Spacing();
                if (ImguiHelpers.ValueCombo("Armature Type", MapTypes, MapTypes, ref selectedArmatureType)) {
                    lastSelectedType = selectedArmatureType;
                }

                if (!string.IsNullOrEmpty(selectedArmatureType) && configs.TryGetValue(selectedArmatureType, out var config)) {
                    var renameSourceGames = config.Renames.SelectMany(r => r.Version1).Concat(config.Renames.SelectMany(r => r.Version2)).Distinct().ToArray();
                    if (renameSourceGames.Length == 0) {
                        ImGui.TextColored(Colors.Info, "No rename configs available for selected armature type");
                    } else {
                        ImGui.SeparatorText("Bone name remapping");
                        if (ImguiHelpers.ValueCombo("Source", renameSourceGames, renameSourceGames, ref sourceType)) {
                            lastSelectedSource = sourceType;
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Select the game from which the clip was copied from");
                        if (!string.IsNullOrEmpty(sourceType)) {
                            var targets = new List<string>();
                            foreach (var c in config.Renames) {
                                if (c.Version1.Contains(sourceType) || c.Version2.Contains(sourceType)) targets.Add(c.Name);
                            }

                            var targetsArray = targets.Distinct().ToArray();
                            if (ImguiHelpers.ValueCombo("Remap Config", targetsArray, targetsArray, ref targetRenameConfig)) {
                                lastSelectedTarget = targetRenameConfig;
                            }

                            var remapConfig = config.Renames.FirstOrDefault(r => r.Name == targetRenameConfig);
                            if (remapConfig?.Version1.Contains(sourceType) == true && remapConfig?.Version2.Contains(sourceType) == true) {
                                ImGui.Checkbox("Map from bone set 2 to set 1", ref mapToBone1);
                                if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("The selected configuration can work within the same game, you need to choose which name transfer to use");
                            } else {
                                mapToBone1 = false;
                            }
                            if (!string.IsNullOrEmpty(targetRenameConfig) && targetsArray.Contains(targetRenameConfig) && remapConfig != null && ImGui.Button("Execute Rename")) {
                                ExecuteRename(remapConfig, sourceType, mapToBone1 ? 1 : 2);
                            }
                        }
                    }
                    // var parentOptions = config.Parents.Select(p => p.Name).Distinct().ToArray();
                    // if (parentOptions.Length == 0) {
                    //     ImGui.TextColored(Colors.Info, "No remap configs available for selected armature type");
                    // } else {
                    //     ImGui.SeparatorText("Bone parent remapping");

                    //     ImguiHelpers.ValueCombo("Parent remap config", parentOptions, parentOptions, ref parentRemapType);
                    //     if (!string.IsNullOrEmpty(parentRemapType) && ImGui.Button("Execute Reparenting")) {
                    //         var remapConfig = config.Parents.FirstOrDefault(p => p.Name == parentRemapType);
                    //         if (remapConfig != null) {
                    //             ExecuteRemapParents(remapConfig);
                    //         }
                    //     }
                    // }
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                if (replacedFile == newFile) {
                    if (ImGui.Button("Close")) {
                        keepShowing = false;
                    }
                } else {
                    if (motPreviewContext == null) {
                        motPreviewContext = UIContext.CreateRootContext("File preview", newFile);
                        motPreviewContext.uiHandler = new MotFileHandler();
                    }
                    if (ImGui.TreeNode("MOT preview")) {
                        motPreviewContext.ShowUI();
                        ImGui.TreePop();
                    }
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImguiHelpers.TextColoredWrapped(Colors.Note, "Mot replacement does not support undo/redo. If you need to revert applied changes, reopen the file or restore it from a backup.");

                    ImGui.Spacing();
                    if (ImGui.Button("Confirm Replace", new Vector2(170, 0))) {
                        if (replacedFile != newFile) {
                            ConfirmPaste(motlist, replacedFile, newFile, editor);
                        }
                        keepShowing = false;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel", new Vector2(170, 0))) {
                        keepShowing = false;
                    }
                }

                if (!keepShowing) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            ImGui.PopStyleColor();
            return keepShowing;
        }

        private UIContext? motPreviewContext;

        private void ExecuteRename(MotRetargetNamesConfig remapConfig, string source, int directionTieBreaker)
        {
            if (newFile is MotFile mot) {
                var renames = RetargetDesigner.ExecuteRemap(mot, remapConfig, source, directionTieBreaker);
                Logger.Info($"Renamed {renames.Count} bones:\n" + string.Join("\n", renames));
                motPreviewContext = null;
            }
        }

        private void ExecuteRemapParents(MotRetargetParentsConfig remapConfig)
        {
            // TODO do we need to worry about bone indices?
            // NOTE: we're not modifying the mot.Bones/mot.RootBones - most motlist versions only have the list on mot 1
            // which also means that there's no point in reparenting anything here
            // may be used for offset adjustments later
            var boneHashes = remapConfig.Parents.ToDictionary(kv => MurMur3HashUtils.GetHash(kv.Key), kv => kv.Value);
            var renames = new List<string>();

            if (newFile is MotFile mot) {
                if (mot.BoneHeaders == null) {
                    Logger.Error("Mot file contains no bone data");
                    return;
                }

                foreach (var bone in mot.BoneHeaders) {
                    if (bone.boneName != null && remapConfig.Parents.TryGetValue(bone.boneName, out var newName) || boneHashes.TryGetValue(bone.boneHash, out newName)) {
                        renames.Add((bone.boneName ?? bone.boneHash.ToString()) + " => " + newName);
                        // bone.boneName = newName;
                        // bone.boneHash = MurMur3HashUtils.GetHash(newName);
                    }
                }

                foreach (var clip in mot.BoneClips) {
                    if (clip.ClipHeader.boneName != null && remapConfig.Parents.TryGetValue(clip.ClipHeader.boneName, out var newName) || boneHashes.TryGetValue(clip.ClipHeader.boneHash, out newName)) {
                        renames.Add((clip.ClipHeader.boneName ?? clip.ClipHeader.boneHash.ToString()) + " => " + newName);
                        clip.ClipHeader.boneName = newName;
                        clip.ClipHeader.boneHash = MurMur3HashUtils.GetHash(newName);
                    }
                }
            }
        }
    }

    private static void ConfirmPaste(MotlistFile motlist, MotFileBase prevMot, MotFileBase newMot, MotlistEditor? editor)
    {
        if (prevMot.GetType() != newMot.GetType()) {
            // fully replace instance
            motlist.ReplaceMotFile(prevMot, newMot);
            if (editor != null) {
                editor.Handle.Modified = true;
                editor.RefreshUI();
            }
        } else if (newMot is MotFile motSrc && prevMot is MotFile motTarget) {
            // replace values, keep instance
            foreach (var clip in motTarget.BoneClips) {
                // ensure that for any names that were manually modified, the hashes also update to match
                if (!string.IsNullOrEmpty(clip.ClipHeader.boneName) && clip.ClipHeader.boneName != clip.ClipHeader.OriginalName) {
                    clip.ClipHeader.boneHash = MurMur3HashUtils.GetHash(clip.ClipHeader.boneName);
                }
            }
            motTarget.CopyValuesFrom(motSrc, false);
            if (motlist != null) {
                // ensure unique name
                motTarget.Header.motName = motTarget.Header.motName
                    .GetUniqueName((newName) => motlist.MotFiles.Any(m => m != motTarget && m is MotFile mm && mm.Header.motName == newName));
            }
            if (editor != null) {
                editor.Handle.Modified = true;
                editor.RefreshUI();
            }
        } else {
            Logger.Error("Unsupported mot type");
        }
    }
}
