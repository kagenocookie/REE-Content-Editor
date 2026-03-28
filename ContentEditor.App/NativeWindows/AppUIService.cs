using ContentEditor.App.ImguiHandling;
using ContentPatcher;

namespace ContentEditor.App.Windowing;

public class AppUIService(EditorWindow defaultWindow, ContentWorkspace workspace) : UIService
{
    public override void ShowMessage(string message, LogSeverity level = LogSeverity.Info, UIContext? context = null, params (string? label, Action action)[] buttons)
    {
        var window = (context?.GetNativeWindow() ?? defaultWindow);
        window.Overlays.ShowToast(message.Length * 0.06f, message, buttons);
        Logger.Log(level, message);
    }

    public override void SaveAs(FileHandleBase file, UIContext? context = null)
    {
        var realFile = (FileHandle)file;
        var window = (context?.GetNativeWindow() ?? defaultWindow);
        foreach (var imWindow in window.ActiveImguiWindows) {
            if (imWindow.Context.uiHandler is FileEditor fe && fe.Handle == file) {
                fe.SaveAs();
                return;
            }
        }

        PlatformUtils.ShowSaveFileDialog((path) => realFile.Save(workspace, path), realFile.Filepath);
    }

    public override void SaveToBundle(FileHandleBase file, UIContext? context = null)
    {
        var realFile = (FileHandle)file;
        var window = (context?.GetNativeWindow() ?? defaultWindow);
        foreach (var imWindow in window.ActiveImguiWindows) {
            if (imWindow.Context.uiHandler is FileEditor fe && fe.Handle == file) {
                fe.SaveToBundle(workspace);
                return;
            }
        }

        ResourcePathPicker.SaveFileToBundle(workspace, realFile, (savePath, localPath, nativePath) => {
            var saved = realFile.Save(workspace, savePath);
            // current file will get closed after this method, re-open the newly saved file so it stays under open files
            MainLoop.Instance.InvokeFromUIThread(() => {
                workspace.ResourceManager.TryGetOrLoadFile(nativePath, out _);
            });
            return saved;
        }, closeFile: true);
    }
}
