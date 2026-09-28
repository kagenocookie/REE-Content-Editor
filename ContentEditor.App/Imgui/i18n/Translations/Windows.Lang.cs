using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class Windows
    {
        public static readonly IconString Settings = new IconString("{0} Settings", AppIcons.SI_Settings);
        public static readonly IconString ThemeEditor = new IconString("{0} Theme Editor", AppIcons.Pencil);
        public static readonly FixedString RetargetDesigner = "Retarget Designer";
        public static readonly IconString PakBrowser = new IconString("{0} PAK File Browser", AppIcons.SI_FileType_PAK);
        public static readonly IconString BundleManager = new IconString("{0} Bundle Manager", AppIcons.SI_Bundle);

        public static readonly FixedString FileSearch = "File Search";
        public static readonly FixedString TexturePacker = "Texture Channel Packer";
        public static readonly FixedString BatchConvert = "Batch File Conversion";
        public static readonly FixedString Entities = "Entities";
        public static readonly IconString MacroShelf = new IconString("{0} Macro Shelf", AppIcons.SI_LUA);

        public static readonly FixedString ResetLayout = "Reset Layout";
        public static readonly FixedString Slot_Hierarchy = "Hierarchy";
        public static readonly FixedString Slot_HierarchyHint = "Open a scene or prefab file to see its objects here";
        public static readonly FixedString Slot_Viewport = "Viewport";
        public static readonly FixedString Slot_ViewportHint = "The active scene and any opened files are shown here";
        public static readonly FixedString Slot_Inspector = "Inspector";
        public static readonly FixedString Slot_InspectorHint = "Select an object to edit its properties";
        public static readonly FixedString Slot_AssetBrowser = "Asset Browser";
        public static readonly FixedString Slot_AssetBrowserHint = "Open the PAK File Browser from the Windows menu";
    }
}
