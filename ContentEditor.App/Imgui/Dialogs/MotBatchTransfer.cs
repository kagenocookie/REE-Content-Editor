using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Motlist;

namespace ContentEditor.App.Widgets;

internal partial class MotBatchTransfer(ContentWorkspace workspace, MotlistFileBase sourceFile, MotlistFileBase targetFile, FileHandle targetFileHandle) : DialogBase("Batch Motion Transfer")
{
    private Dictionary<MotFileBase, MotFileBase?> transferMatches = new();
    private Dictionary<string, MotFileBase> sourceIdMap = new();
    private Dictionary<string, MotFileBase> targetIdMap = new();
    private List<MotFileBase> unmatchedSource = new();
    private List<MotFileBase> unmatchedTarget = new();
    private string[] TargetNames = [];

    private List<string> messages = new();

    private bool firstShow = true;
    private bool replaceBoneList = false;
    private bool maintainExistingChannelsOnly = false;

    private void ResetMapping()
    {
        transferMatches.Clear();
        sourceIdMap.Clear();
        targetIdMap.Clear();
        messages.Clear();
        TargetNames = targetFile.MotFiles.Select(m => m.Name).ToArray();

        foreach (var f in sourceFile.MotFiles) {
            var id = FindMotFileID(sourceFile, f.Name);
            if (id != null) {
                if (!sourceIdMap.TryAdd(id, f)) {
                    messages.Add($"Found duplicate mot file ID {id} between files {sourceIdMap[id].Name} and {f.Name}");
                }
            }
        }

        foreach (var f in targetFile.MotFiles) {
            var id = FindMotFileID(targetFile, f.Name);
            if (id != null) {
                if (!targetIdMap.TryAdd(id, f)) {
                    messages.Add($"Found duplicate mot file ID {id} between files {targetIdMap[id].Name} and {f.Name}");
                }
            }
        }

        foreach (var (id, src) in sourceIdMap) {
            if (targetIdMap.TryGetValue(id, out var targetMot)) {
                if (targetMot.GetType() != src.GetType()) {
                    messages.Add($"Mot files {src.Name} and {targetMot.Name} are not the same motion type, can't do batch transfer.");
                } else {
                    transferMatches[src] = targetMot;
                }
            } else {
                messages.Add($"Could not match source mot file {src.Name} with any file in the target motlist");
                unmatchedSource.Add(src);
            }
        }

        foreach (var (id, target) in targetIdMap) {
            if (!sourceIdMap.TryGetValue(id, out _)) {
                unmatchedTarget.Add(target);
            }
        }
    }

    private static string? FindMotFileID(MotlistFileBase list, string name)
    {
        if (name.StartsWith(list.Name)) {
            // vanila motlists are usually named as "{motlistName}_{motFileId}_{motName}"
            var nameId = name.AsSpan(list.Name.Length + 1); // skip "{name}_"
            var sub = nameId.IndexOf('_');
            var idStr = sub == -1 ? nameId : nameId.Slice(0, sub);
            if (int.TryParse(idStr, out var id)) {
                return idStr.ToString();
            }
        }

        var match = MotIdRegex().Match(name);
        if (match.Success) {
            return match.Groups[1].Value;
        }

        return null;
    }

    [GeneratedRegex("(?:^|_)(\\d{4})(?:$|_)")]
    private static partial Regex MotIdRegex();
    private Dictionary<MotFileBase, string?> filters = new();

    private void Transfer()
    {
        foreach (var (src, target) in transferMatches) {
            if (target == null) continue;

            if (targetFile is MotlistFile motlist) {
                MotFileBase? clone = src switch {
                    MotFile mot => mot.RewriteClone(workspace),
                    MotcamFile mcam => mcam.RewriteClone(workspace),
                    MotTreeFile mtre => mtre.RewriteClone(workspace),
                    MotFileLink mlink => mlink.RewriteClone(workspace),
                    _ => null,
                };
                if (clone == null) {
                    Logger.Error("Could not make clone of source motion " + src.Name);
                    continue;
                }

                MotFileActionHandler.ConfirmPaste(motlist, target, clone, null, replaceBoneList, maintainExistingChannelsOnly);
            }
        }
        targetFileHandle.Modified = true;
    }

    protected override bool Show()
    {
        if (ImGui.Button("Reset mapping") || firstShow) {
            ResetMapping();
            firstShow = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Confirm transfer")) {
            Transfer();
            return true;
        }

        ImGui.Checkbox("Overwrite bone list", ref replaceBoneList);
        ImguiHelpers.Tooltip("The bone list between the two animations is different. This option will overwrite the target bone list with the source one."u8);

        ImGui.Checkbox("Only paste already existing channels", ref maintainExistingChannelsOnly);
        ImguiHelpers.Tooltip("Only channels that already exist in the target motion will be kept, while the rest are ignored.\nCan be used to make pure edits and avoid modifying bones that should not be modified by the animation.");

        if (messages.Count > 0 && ImGui.TreeNode($"Messages ({messages.Count})")) {
            foreach (var msg in messages) {
                ImGui.Text(msg);
            }
            ImGui.TreePop();
        }

        ImGui.Separator();
        ImGui.Spacing();

        var targetFileSpan = CollectionsMarshal.AsSpan(targetFile.MotFiles);
        var avail = ImGui.GetContentRegionAvail();
        var colSelect = avail.X / 2;
        var selectWidth = avail.X / 2 - ImGui.GetFrameHeightWithSpacing();
        var colBtn = colSelect + selectWidth + ImGui.GetStyle().ItemSpacing.X;
        ImGui.PushItemWidth(avail.X);
        foreach (var src in sourceFile.MotFiles) {
            var target = transferMatches.GetValueOrDefault(src);
            ImGui.PushID(src.Name);
            var startX = ImGui.GetCursorPosX();
            ImGui.Text(src.Name);
            ImGui.SameLine();
            if (ImGui.GetCursorPosX() < colSelect) ImGui.SetCursorPosX(colSelect);
            var temptarget = target;
            var filter = filters.GetValueOrDefault(src) ?? "";
            ImGui.SetNextItemWidth(selectWidth);
            if (ImguiHelpers.FilterableCombo("##target", TargetNames, targetFileSpan, ref temptarget, ref filter)) {
                ChangeMapping(src, temptarget);
            }
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_GenericClose}")) {
                transferMatches[src] = null;
            }
            filters[src] = filter;
            ImGui.PopID();
        }

        ImGui.PopItemWidth();

        return false;
    }

    private void ChangeMapping(MotFileBase source, MotFileBase? newTarget)
    {
        var sourceCurTarget = transferMatches.GetValueOrDefault(source);
        var currentTarget = transferMatches.FirstOrDefault(kv => kv.Value == newTarget);
        if (currentTarget.Value != null) {
            transferMatches[source] = newTarget;
            transferMatches[currentTarget.Key] = sourceCurTarget;
        } else {
            transferMatches[source] = newTarget;
        }
    }
}
