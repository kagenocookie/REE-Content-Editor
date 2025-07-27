using Silk.NET.Windowing;

namespace ContentEditor.App;

public static class PlatformUtils
{
    /// <summary>
    /// Show a native file picker dialog, non-blocking.
    /// </summary>
    public static void ShowFileDialog(Action<string[]> callback, string? initialFile = null, string? fileExtension = null, bool allowMultiple = false)
    {
#if WINDOWS
        ContentEditor.App.Windows.PlatformUtils.ShowOpenFile(callback, initialFile, fileExtension, allowMultiple);
#else
        throw new NotImplementedException();
#endif
    }

    /// <summary>
    /// Show a native folder picker dialog, non-blocking.
    /// </summary>
    public static void ShowFolderDialog(Action<string> callback, string? initialFolder = null)
    {
#if WINDOWS
        ContentEditor.App.Windows.PlatformUtils.ShowFolderPicker(callback, initialFolder);
#else
        throw new NotImplementedException();
#endif
    }

    /// <summary>
    /// Show a native file save dialog, non-blocking.
    /// </summary>
    public static void ShowSaveFileDialog(Action<string> callback, string? initialFile = null)
    {
#if WINDOWS
        ContentEditor.App.Windows.PlatformUtils.ShowSaveFile(callback, initialFile);
#else
        throw new NotImplementedException();
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
