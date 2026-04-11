#if WINDOWS
using System.Runtime.InteropServices;
#endif
using NativeFileDialogNET;
using Silk.NET.Windowing;

namespace ContentEditor.App;

public static class PlatformUtils
{
    private static string? GetWindowsFilterString(FileFilter[]? filters, bool forSave)
    {
        string? filterString = null;
        if (filters?.Length > 0) {
            foreach (var filter in filters) {
                if (forSave) {
                    foreach (var ext in filter.extensions) {
                        if (filterString != null) {
                            filterString += "|";
                        }
                        filterString += $"{filter.name} (*.{ext})|*.{ext}";
                    }
                } else {
                    var substr = string.Join(", ", filter.extensions.Select(ext => $"*.{ext}"));
                    var substr2 = string.Join(";", filter.extensions.Select(ext => $"*.{ext}"));
                    if (filterString != null) {
                        filterString += "|";
                    }
                    filterString += $"{filter.name} ({substr})|{substr2}";
                }
            }
        }
        return filterString;
    }

    /// <summary>
    /// Show a native file picker dialog. The callback can be executed from a separate thread - make sure to invoke anything that requires the main thread, on the main thread.
    /// </summary>
    public static void ShowFileDialog(Action<string[]> callback, string? initialFile = null, FileFilter[]? filters = null, bool allowMultiple = false)
    {
        var dir = !string.IsNullOrEmpty(initialFile) ? Path.GetDirectoryName(initialFile) : null;

#if WINDOWS
        var thread = new Thread(() => {
            var filterString = GetWindowsFilterString(filters, false);
            ContentEditor.App.Windows.PlatformUtils.ShowFileDialog(callback, allowMultiple, dir, initialFile, filterString);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
#else
        using var selectFileDialog = new NativeFileDialog()
            .SelectFile();
        if (filters != null) {
            foreach (var (name, exts) in filters) {
                selectFileDialog.AddFilter(name, string.Join(",", exts));
            }
        }
        if (allowMultiple) selectFileDialog.AllowMultiple();

        var result = selectFileDialog.Open(out string[]? output, dir);
        if (result == DialogResult.Okay && output?.Length > 0) {
            callback.Invoke(output);
        }
#endif
    }

    /// <summary>
    /// Show a native folder picker dialog. The callback can be executed from a separate thread - make sure to invoke anything that requires the main thread, on the main thread.
    /// </summary>
    public static void ShowFolderDialog(Action<string> callback, string? initialFolder = null)
    {
#if WINDOWS
        var thread = new Thread(() => {
            ContentEditor.App.Windows.PlatformUtils.ShowFolderDialog(callback, initialFolder);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
#else
        if (!MainLoop.IsMainThread) {
            MainLoop.Instance.InvokeFromUIThread(() => ShowFolderDialog(callback, initialFolder));
            return;
        }
        using var selectFolderDialog = new NativeFileDialog()
            .SelectFolder();

        var result = selectFolderDialog.Open(out string? output, initialFolder);
        if (result == DialogResult.Okay && output?.Length > 0)
        {
            callback.Invoke(output);
        }
#endif
    }

    /// <summary>
    /// Show a native file save dialog. The callback can be executed from a separate thread - make sure to invoke anything that requires the main thread, on the main thread.
    /// </summary>
    public static void ShowSaveFileDialog(Action<string> callback, string? initialFile = null, params FileFilter[] filters)
    {
        var dir = !string.IsNullOrEmpty(initialFile) ? Path.GetDirectoryName(initialFile) : null;
        initialFile = Path.GetFileName(initialFile);

#if WINDOWS
        var thread = new Thread(() => {
            var filterString = GetWindowsFilterString(filters, true);
            ContentEditor.App.Windows.PlatformUtils.ShowSaveFileDialog(callback, dir, initialFile, filterString);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
#else
        if (!MainLoop.IsMainThread) {
            MainLoop.Instance.InvokeFromUIThread(() => ShowSaveFileDialog(callback, initialFile));
            return;
        }

        using var selectFileDialog = new NativeFileDialog()
            .SaveFile();
        foreach (var (name, exts) in filters) {
            foreach (var ext in exts) {
                selectFileDialog.AddFilter(name, ext);
            }
        }

        var result = selectFileDialog.Open(out string? output, dir, initialFile);

        if (result == DialogResult.Okay && !string.IsNullOrEmpty(output)) {
            callback.Invoke(output);
        }
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
