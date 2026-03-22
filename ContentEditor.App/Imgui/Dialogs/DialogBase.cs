using System.Diagnostics.CodeAnalysis;

namespace ContentEditor.App.Widgets;

public abstract class DialogBase(string PopupName)
{
    private bool isOpen = false;
    protected bool Closable { get; set; } = true;

    /// <summary>
    /// Show the dialog and automatically set the dialog instance to null when it's closed.
    /// </summary>
    /// <returns>True if the dialog was closed this frame, false otherwise.</returns>
    public bool ShowPopup<T>([MaybeNullWhen(true)] ref T dialogRef) where T : DialogBase
    {
        if (ShowPopup()) {
            dialogRef = null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Show the dialog.
    /// </summary>
    /// <returns>True if the dialog was closed this frame, false otherwise.</returns>
    public bool ShowPopup()
    {
        if (!isOpen) {
            TriggerShow();
        }
        if (Closable) {
            isOpen = ImGui.BeginPopupModal(PopupName, ref isOpen);
            if (!isOpen) {
                return true;
            }

        } else {
            isOpen = ImGui.BeginPopupModal(PopupName);
        }
        if (isOpen) {
            if (Show()) {
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
                isOpen = false;
                return true;
            }
            ImGui.EndPopup();
        }
        return false;
    }

    public void TriggerShow()
    {
        ImGui.OpenPopup(PopupName);
        isOpen = true;
    }

    protected abstract bool Show();
}
