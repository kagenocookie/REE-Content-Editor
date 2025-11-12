using System.Runtime.InteropServices;
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

    public static bool IsAppInForeground()
    {
#if WINDOWS
        return ContentEditor.App.Windows.PlatformUtils.IsAppInForeground();
#else
        return true;
#endif
    }

    private static int[] D3DFeatureLevels = [0xB000, 0xA100, 0xA000]; // 11.0, 10.1, 10.0
    public static unsafe bool CreateD3D11Context(out IntPtr device)
    {
#if WINDOWS
        // invoke d3d11.dll directly though the windows api
        // this way we avoid shipping the app with useless extra dependencies just for gpu tex conversion, since we're primarily doing opengl
        // non-windows platforms will have to deal with CPU conversion
        var res = D3D11CreateDevice(
            adapter: IntPtr.Zero,
            driverType: 1, // D3D_DRIVER_TYPE.Hardware
            softwareRasterizer: IntPtr.Zero,
            flags: 0,
            featureLevels: D3DFeatureLevels,
            featureLevelCount: D3DFeatureLevels.Length,
            sdkVersion: 7, // D3D11_SDK_VERSION
            out device,
            out var featureLevel,
            IntPtr.Zero
        );
        if (res < 0) {
            MainLoop.Instance.InvokeFromUIThread(() => Logger.Error($"Failed to create D3D context, falling back to CPU. Error code: {res:X}"));
            return false;
        }
        return true;
#else
        device = IntPtr.Zero;
        return false;
#endif
    }

#if WINDOWS
    [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", CallingConvention = CallingConvention.StdCall, PreserveSig = true)]
    private extern static int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr softwareRasterizer,
        int flags,
        [In] int[]? featureLevels,
        int featureLevelCount,
        int sdkVersion,
        out IntPtr device,
        out IntPtr featureLevel,
        IntPtr immediateContext);
#endif
}
