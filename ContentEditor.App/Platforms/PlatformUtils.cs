using System.Runtime.InteropServices;
using NativeFileDialogNET;
using Silk.NET.Windowing;

namespace ContentEditor.App;

public static class PlatformUtils
{
    /// <summary>
    /// Show a native file picker dialog, non-blocking. The callback will be executed from a separate thread - make sure to invoke anything that requires the main thread, on the main thread.
    /// </summary>
    public static void ShowFileDialog(Action<string[]> callback, string? initialFile = null, FileFilter[]? fileExtension = null, bool allowMultiple = false)
    {
        var thread = new Thread(() => {
            using var selectFileDialog = new NativeFileDialog()
                .SelectFile();
            if (fileExtension != null) {
                foreach (var (name, exts) in fileExtension) {
                    selectFileDialog.AddFilter(name, string.Join(",", exts));
                }
            }
            if (allowMultiple) selectFileDialog.AllowMultiple();

            var result = selectFileDialog.Open(out string[]? output, !string.IsNullOrEmpty(initialFile) ? Path.GetDirectoryName(initialFile) : Environment.CurrentDirectory);
            if (result == DialogResult.Okay && output?.Length > 0) {
                callback.Invoke(output);
            }
        });
#if WINDOWS
        thread.SetApartmentState(ApartmentState.STA);
#endif
        thread.Start();
    }

    /// <summary>
    /// Show a native folder picker dialog, non-blocking. The callback will be executed from a separate thread - make sure to invoke anything that requires the main thread, on the main thread.
    /// </summary>
    public static void ShowFolderDialog(Action<string> callback, string? initialFolder = null)
    {
        var thread = new Thread(() => {
            using var selectFolderDialog = new NativeFileDialog()
                .SelectFolder();

            var result = selectFolderDialog.Open(out string? output, initialFolder);
            if (result == DialogResult.Okay && output?.Length > 0)
            {
                callback.Invoke(output);
            }
        });
#if WINDOWS
        thread.SetApartmentState(ApartmentState.STA);
#endif
        thread.Start();
    }

    private static bool _saveDlgOpen;
    /// <summary>
    /// Show a native file save dialog, non-blocking. The callback will be executed from a separate thread - make sure to invoke anything that requires the main thread, on the main thread.
    /// </summary>
    public static void ShowSaveFileDialog(Action<string> callback, string? initialFile = null, params FileFilter[] filter)
    {
#if !WINDOWS
        if (_saveDlgOpen) {
            // prevent a possible crash
            Logger.Error("Save dialog is already open, confirm or close it first.");
            return;
        }
#endif
        _saveDlgOpen = true;
        var thread = new Thread(() => {
            try {
                using var selectFileDialog = new NativeFileDialog()
                    .SaveFile();
                foreach (var (name, exts) in filter) {
                    foreach (var ext in exts) {
                        selectFileDialog.AddFilter(name, ext);
                    }
                }

                var result = selectFileDialog.Open(
                    out string? output,
                    !string.IsNullOrEmpty(initialFile) ? Path.GetDirectoryName(initialFile) : Environment.CurrentDirectory,
                    Path.GetFileName(initialFile));

                if (result == DialogResult.Okay && !string.IsNullOrEmpty(output)) {
                    callback.Invoke(output);
                }
            } finally {
                _saveDlgOpen = false;
            }
        });
#if WINDOWS
        thread.SetApartmentState(ApartmentState.STA);
#endif
        thread.Start();
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
