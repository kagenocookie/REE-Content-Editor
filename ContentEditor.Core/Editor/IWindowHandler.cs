using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor;

public interface IWindowHandler
{
    string HandlerName { get; }
    bool HasUnsavedChanges { get; }
    int FixedID => 0;
    char Icon => '\0';

    void OnOpen() { }
    void OnClosed() { }
    void Init(UIContext context);
    void OnWindow();
    void OnIMGUI();
    /// <summary>
    /// Called when the window is requested to close. Should return true if it should be blocked from closing (e.g. to confirm unsaved changes)
    /// </summary>
    /// <returns></returns>
    bool RequestClose();
}

public static class DefaultWindowHandler
{
    public static void ShowDefaultWindow<THandler>(this THandler handler, UIContext context, string? name = null, ImGuiWindowFlags flags = ImGuiWindowFlags.None) where THandler : IWindowHandler
    {
        var data = context.Get<WindowData>();
        if (!ImguiHelpers.BeginWindow(data, name: handler.Icon == '\0' ? name : handler.Icon.ToString() + " " + name, flags)) {
            WindowManager.Instance.CloseWindow(data);
            return;
        }

        handler.OnIMGUI();
        ImGui.End();
    }
}
