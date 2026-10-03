namespace ContentEditor.App.Widgets;

public abstract class ConfirmCancelDialog(string title) : DialogBase(title)
{
    private DialogResult result;

    public DialogResult ShowDialog()
    {
        var defaultClose = ShowPopup();

        if (defaultClose) {
            result = DialogResult.Cancel;
        }

        return result;
    }

    protected override void DrawBelowContent()
    {
        DrawConfirmCancel();
    }

    protected virtual bool IsConfirmDisabled() => false;

    protected void DrawConfirmCancel()
    {
        ImGui.Separator();
        ImGui.BeginDisabled(IsConfirmDisabled());
        if (ImGui.Button(Lang.Buttons.Confirm)) {
            result = DialogResult.Confirm;
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(Lang.Buttons.Cancel)) {
            result = DialogResult.Cancel;
        }
    }
}
