using Silk.NET.Windowing;

namespace ContentEditor.App;

public static class PlatformUtils
{
    /// <summary>
    /// Show a native file picker dialog, non-blocking. The callback will likely be executed from a separate thread - make sure to invoke anything that requires the main thread, on the main thread.
    /// </summary>
    public static void ShowFileDialog(Action<string[]> callback, string? initialFile = null, string? fileExtension = null, bool allowMultiple = false)
    {
#if WINDOWS
        ContentEditor.App.Windows.PlatformUtils.ShowOpenFile(callback, initialFile, fileExtension, allowMultiple);
#else
        Logger.Error("Native file dialogs not supported for the current platform");
#endif
    }

    /// <summary>
    /// Show a native folder picker dialog, non-blocking. The callback will likely be executed from a separate thread - make sure to invoke anything that requires the main thread, on the main thread.
    /// </summary>
    public static void ShowFolderDialog(Action<string> callback, string? initialFolder = null)
    {
#if WINDOWS
        ContentEditor.App.Windows.PlatformUtils.ShowFolderPicker(callback, initialFolder);
#else
        Logger.Error("Native file dialogs not supported for the current platform");
#endif
    }

    /// <summary>
    /// Show a native file save dialog, non-blocking. The callback will likely be executed from a separate thread - make sure to invoke anything that requires the main thread, on the main thread.
    /// </summary>
    public static void ShowSaveFileDialog(Action<string> callback, string? initialFile = null, string? filter = null)
    {
#if WINDOWS
        ContentEditor.App.Windows.PlatformUtils.ShowSaveFile(callback, initialFile, filter);
#else
        Logger.Error("Native file dialogs not supported for the current platform");
#endif
    }

    public static void SetupDragDrop(IDragDropTarget target, IWindow window)
    {
#if WINDOWS
        ContentEditor.App.Windows.PlatformUtils.InitDragDrop(target, window.Native!.Win32!.Value.Hwnd);
#else
        window.FileDrop += (files) => {
            uint state = 0;
            target.Drop(new DragDropContextObject() { filenames = files }, 0, new System.Drawing.Point(), ref state);
        };
#endif
    }
}
