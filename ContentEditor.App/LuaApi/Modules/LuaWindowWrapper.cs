using ContentEditor.App.Windowing;
using Lua;

namespace ContentEditor.App.Lua;

[LuaObject]
public partial class LuaWindowWrapper(EditorWindow window)
{
    [LuaMember("close_all")]
    public void Close() => window.RequestCloseAllSubwindows(false);

    [LuaMember("exit")]
    public void Exit() => window.RequestClose(false);

    [LuaMember("open_editor")]
    public void OpenEditor(LuaValue file)
    {
        if (file.TryRead<LuaFileHandleWrapper>(out var handle)) {
            window.AddFileEditor(handle.File);
        } else if (file.TryRead<string>(out var filepath)) {
            if (window.Workspace.ResourceManager.TryGetOrLoadFile(filepath, out var rawHandle)) {
                window.AddFileEditor(rawHandle);
            } else {
                window.AddSubwindow(new ErrorModal(UiText.T("Unsupported file"), UiText.T("File is not supported:\n") + filepath));
            }
        }
    }
}
