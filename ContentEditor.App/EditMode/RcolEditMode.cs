using ContentEditor.App.ImguiHandling.Rcol;
using ContentEditor.App.Windowing;
using ReeLib;
using ReeLib.Rcol;

namespace ContentEditor.App;

public class RcolEditMode : EditModeHandler
{
    public static readonly string TypeID = nameof(RequestSet);
    public override string EditTypeID => TypeID;

    public RcolEditor? PrimaryEditor { get; private set; }

    protected override void OnTargetChanged(Component? previous)
    {
        if (previous is RequestSetColliderComponent prst) prst.activeGroup = null;
    }

    public void OpenEditor(string rcolFilepath)
    {
        if (!(Target is RequestSetColliderComponent component)) {
            return;
        }
        if (Scene.Workspace.ResourceManager.TryResolveFile(rcolFilepath, out var file)) {
            PrimaryEditor = new RcolEditor(Scene.Workspace, file, component);
            EditorWindow.CurrentWindow!.AddSubwindow(PrimaryEditor);
        }
    }

    public override void OnIMGUI()
    {
        if (Target is RequestSetColliderComponent comp) {
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
}
