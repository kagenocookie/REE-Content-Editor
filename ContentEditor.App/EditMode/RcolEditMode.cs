using System.Diagnostics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.ImguiHandling.Rcol;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Rcol;

namespace ContentEditor.App;

public class RcolEditMode : EditModeHandler
{
    public override string DisplayName => $"{AppIcons.Mesh} RCOL";

    public RcolEditor? PrimaryEditor { get; private set; }

    private UIContext? filePicker;
    private string rcolPath = "";
    protected UIContext? context;

    protected override void OnTargetChanged(Component? previous)
    {
        if (previous is RequestSetColliderComponent prst) {
            prst.activeGroup = null;
            prst.SetOverrideFile(null);
        }
    }

    public void OpenEditor(string rcolFilepath)
    {
        if (!(Target is RequestSetColliderComponent component)) {
            return;
        }
        if (Scene.Workspace.ResourceManager.TryGetOrLoadFile(rcolFilepath, out var file)) {
            OpenEditor(file);
        }
    }

    public void OpenEditor(FileHandle file)
    {
        Debug.Assert(file.GetFile<RcolFile>() != null);
        if (!(Target is RequestSetColliderComponent component)) {
            return;
        }
        PrimaryEditor = new RcolEditor(Scene.Workspace, file, component);
        EditorWindow.CurrentWindow!.AddSubwindow(PrimaryEditor);
    }

    public override void DrawMainUI()
    {
        if (context == null) {
            context = UIContext.CreateRootContext("RcolEdit", this);
        }
        if (filePicker == null) {
            filePicker = context.AddChild<RcolEditMode, string>(
                "Edited RCOL File",
                this,
                new ResourcePathPicker(Scene.Workspace, KnownFileFormats.RequestSetCollider) { UseNativesPath = true, IsPathForIngame = false },
                (v) => v!.rcolPath,
                (v, p) => v.rcolPath = p ?? "");
        }
        filePicker.ShowUI();

        if (Target is RequestSetColliderComponent rcolTarget) {
            var storedSuggestions = rcolTarget.StoredResources?.ToArray() ?? [];
            if (storedSuggestions.Length > 0) {
                if (ImguiHelpers.ValueCombo("Component Files", storedSuggestions, storedSuggestions, ref rcolPath)) {
                    AppConfig.Instance.AddRecentRcol(rcolPath);
                    filePicker?.ResetState();
                }
            }
        }

        var settings = AppConfig.Settings;
        if (settings.RecentRcols.Count > 0) {
            var options = settings.RecentRcols.ToArray();
            if (ImguiHelpers.ValueCombo("Recent", options, options, ref rcolPath)) {
                AppConfig.Instance.AddRecentRcol(rcolPath);
                filePicker?.ResetState();
            }
        }

        if (Scene.Workspace.ResourceManager.TryGetOrLoadFile(rcolPath, out var file)) {
            var rcol = file.GetFile<RcolFile>();
            if (ImGui.Button("Open Editor")) {
                OpenEditor(file);
            }
        } else if (!string.IsNullOrEmpty(rcolPath)) {
            ImGui.TextColored(Colors.Warning, "File not found");
        }
    }

    public override void OnIMGUI()
    {
        if (!(Target is RequestSetColliderComponent comp)) return;

        if (Scene.Workspace.ResourceManager.TryGetOrLoadFile(rcolPath, out var file)) {
            var rcol = file.GetFile<RcolFile>();
            if (!comp.ActiveRcolFiles.Contains(rcol)) {
                comp.SetOverrideFile(rcol);
                AppConfig.Instance.AddRecentRcol(rcolPath);
            }
        } else {
            comp.SetOverrideFile(null);
        }

        var editedTarget = PrimaryEditor?.PrimaryTarget;
        if (editedTarget == null) {
            comp.activeGroup = null;
            return;
        }

        comp.activeGroup = editedTarget as RcolGroup
            ?? (editedTarget as RequestSet)?.Group;
        if (editedTarget is RcolGroup gg) {
            comp.activeGroup = gg;
        } else if (editedTarget is RequestSet rs) {
            comp.activeGroup = rs.Group;
        } else if (editedTarget is RequestSetInfo rsi) {
            comp.activeGroup = PrimaryEditor!.File.RequestSets.FirstOrDefault(rs => rs.Info == rsi)?.Group;
        } else if (editedTarget is RszInstance rsz) {
            comp.activeGroup = PrimaryEditor!.File.RequestSets.FirstOrDefault(rs => rs.Instance == rsz)?.Group;
        } else {
            comp.activeGroup = null;
        }
    }
}
