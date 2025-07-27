namespace ContentEditor.App.Windows;

using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

// https://github.com/ocornut/imgui/issues/2602

public class PlatformUtils
{
    public static void ShowOpenFile(Action<string[]> callback, string? initialFile = null, string? fileExtension = null, bool allowMultiple = false)
    {
        var thread = new Thread(() => {
            var dlg = new OpenFileDialog {
                InitialDirectory = !string.IsNullOrEmpty(initialFile) ? Path.GetDirectoryName(initialFile) : Environment.CurrentDirectory,
                Multiselect = allowMultiple,
                Filter = fileExtension,
            };
            var result = dlg.ShowDialog();
            if (result == DialogResult.OK) {
                callback.Invoke(dlg.FileNames);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static void ShowFolderPicker(Action<string> callback, string? initialFolder = null)
    {
        var thread = new Thread(() => {
            var dlg = new FolderBrowserDialog {
                InitialDirectory = !string.IsNullOrEmpty(initialFolder) ? initialFolder : Environment.CurrentDirectory,
            };
            var result = dlg.ShowDialog();
            if (result == DialogResult.OK) {
                callback.Invoke(dlg.SelectedPath);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static void ShowSaveFile(Action<string> callback, string? initialFile = null)
    {
        var thread = new Thread(() => {
            var dlg = new SaveFileDialog {
                InitialDirectory = !string.IsNullOrEmpty(initialFile) ? Path.GetDirectoryName(initialFile) : Environment.CurrentDirectory,
                FileName = Path.GetFileName(initialFile),
            };
            var result = dlg.ShowDialog();
            if (result == DialogResult.OK && dlg.FileName != null) {
                callback.Invoke(dlg.FileName);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    [DllImport("OLE32.DLL", ExactSpelling = true, PreserveSig = false)]
    private static extern void RegisterDragDrop(IntPtr hwnd, IDropTarget target);

    [DllImport("OLE32.DLL", ExactSpelling = true, PreserveSig = false)]
    private static extern void OleInitialize(IntPtr reserved);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Auto, PreserveSig = false)]
    private static extern int GetClipboardFormatNameW(uint format, [In, Out] StringBuilder pszPath, int maxlen);

    [DllImport("Kernel32.dll", ExactSpelling = true)]
    private static extern int GlobalFree([In] IntPtr hMem);

    [DllImport("OLE32.dll", ExactSpelling = true)]
    private static extern void ReleaseStgMedium([In] ref STGMEDIUM medium);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern IntPtr LoadLibraryW(string dllToLoad);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr LoadCursorW(IntPtr hInstance, UInt16 lpCursorName);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern void SetCursor([In] IntPtr cursor);

    private static IntPtr GetCursor(int id = 6)
    {
        var l = LoadLibraryW("ole32.dll");
        var h = LoadCursorW(l, (ushort)id);
        return h;
    }

    private static bool oleInitialized;

    public static void InitDragDrop(IDragDropTarget target, IntPtr windowHandle)
    {
        // Note  If you use CoInitialize or CoInitializeEx instead of OleInitialize to initialize COM, RegisterDragDrop will always return an E_OUTOFMEMORY error.
        // https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-registerdragdrop
        if (!oleInitialized) {
            OleInitialize(0);
            oleInitialized = true;
        }

        var wrapper = new DragDropWrapper(target);
        RegisterDragDrop(windowHandle, wrapper);
    }

    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000122-0000-0000-C000-000000000046")] // This is the value of IID_IDropTarget from the Platform SDK.
    [ComImport]
    private interface IDropTarget
    {
        void DragEnter([In] IDataObject dataObject, [In] uint keyState, [In] Point pt, [In, Out] ref uint effect);
        void DragOver([In] uint keyState, [In] Point pt, [In, Out] ref uint effect);
        void DragLeave();
        void Drop([In] IDataObject dataObject, [In] uint keyState, [In] Point pt, [In, Out] ref uint effect);
    }

    public class DragDropWrapper : IDropTarget
    {
        public readonly IDragDropTarget target;

        public DragDropWrapper(IDragDropTarget target)
        {
            this.target = target;
        }

        public unsafe void DragEnter([In] IDataObject dataObject, [In] uint keyState, [In] Point pt, [In, Out] ref uint effect)
        {
            var context = GetDragDropContextObject(dataObject);
            if (context != null) {
                target.DragEnter(context, keyState, pt, ref effect);
            }
        }

        public void DragLeave()
        {
            target.DragLeave();
        }

        public void DragOver([In] uint keyState, [In] Point pt, [In, Out] ref uint effect)
        {
            target.DragOver(keyState, pt, ref effect);
        }

        public void Drop([In] IDataObject dataObject, [In] uint keyState, [In] Point pt, [In, Out] ref uint effect)
        {
            var context = GetDragDropContextObject(dataObject);
            if (context != null) {
                target.Drop(context, keyState, pt, ref effect);
            }
        }

        private unsafe static DragDropContextObject? GetDragDropContextObject(IDataObject dataObject)
        {
            var formatsEnum = dataObject.EnumFormatEtc(DATADIR.DATADIR_GET);
            FORMATETC[] formats = new FORMATETC[1];
            int[] readCounts = new int[1];
            var sb = new StringBuilder(64);
            bool foundFilenameW = false;
            string? filename = null;
            while (true) {
                int res = formatsEnum.Next(1, formats, readCounts);
                if (res != 0) break;

                var readCount = readCounts[0];
                if (readCount == 0) break;

                sb.Clear();
                // note, we could probably skip the GetDataObjectType call and just check the actual values
                // but eh, it's fine
                var type = GetDataObjectType(ref formats[0], sb);
                // https://www.codeproject.com/Reference/1091137/Windows-Clipboard-Formats
                // FileContents => IStream

                // text drop: cfFormat == 13 (CF_UNICODETEXT), type = ""
                if (type == "" && formats[0].cfFormat == 13) {
                    return new DragDropContextObject() { text = GetString(dataObject, ref formats[0], true) };
                }

                switch (type) {
                    case "FileName":
                        if (!foundFilenameW) {
                            filename = GetString(dataObject, ref formats[0], false);
                        }
                        break;
                    case "FileNameW":
                        filename = GetString(dataObject, ref formats[0], true);
                        foundFilenameW = true;
                        break;
                }
            }
            if (filename != null) {
                return new DragDropContextObject() { filenames = [filename] };
            }

            return null;

            static string GetDataObjectType(ref FORMATETC format, StringBuilder sb)
            {
                var err = GetClipboardFormatNameW((uint)format.cfFormat, sb, sb.Capacity);
                if (err != 0) {
                    Console.Error.WriteLine("Failed to get clipboard format");
                    return "";
                }
                return sb.ToString();
            }
            static string GetString(IDataObject dataObject, ref FORMATETC format, bool isUnicode)
            {
                dataObject.GetData(ref format, out var medium);
                var str = isUnicode ? Marshal.PtrToStringUni(*(IntPtr*)medium.unionmember) : Marshal.PtrToStringAnsi(*(IntPtr*)medium.unionmember);
                FreeMedium(ref medium);
                return str ?? string.Empty;
            }
            // static MemoryStream GetStream(IDataObject dataObject, ref FORMATETC format)
            // {
            //     format.tymed = TYMED.TYMED_FILE|TYMED.TYMED_ISTREAM;
            //     format.lindex = 0;
            //     dataObject.GetData(ref format, out var medium);
            //     var stream = Marshal.PtrToStructure<IStream>(*(IntPtr*)medium.unionmember);
            //     if (stream == null) {
            //         FreeMedium(ref medium);
            //         return new MemoryStream(0);
            //     }
            //     var memstream = new MemoryStream();
            //     // Span<byte> bytes = stackalloc byte[4096];
            //     // stream.Read(bytes, 0, )
            //     // stream.CopyTo(memstream, );

            //     FreeMedium(ref medium);
            //     return memstream;
            // }
            static void FreeMedium(ref STGMEDIUM medium)
            {
                if (medium.pUnkForRelease == null) {
                    ReleaseStgMedium(ref medium);
                } else {
                    (medium.pUnkForRelease as IUnknown)?.Release();
                }
            }
        }
    }

    internal interface IUnknown
    {
        void AddRef();
        void Release();
    }
}
