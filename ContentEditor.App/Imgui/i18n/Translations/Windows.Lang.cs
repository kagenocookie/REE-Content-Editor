using ContentEditor.Core;

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
        public static readonly IconString FileSearch = new IconString("{0} File Search", AppIcons.Search);
        public static readonly IconString TexturePacker = new IconString("{0} Texture Channel Packer", AppIcons.SI_UpdateTexture);
        public static readonly IconString BatchConvert = new IconString("{0} Batch File Conversion", AppIcons.SI_GenericConvert);
        public static readonly IconString Entities = new IconString("{0} Entities", AppIcons.SI_SceneGameObject4);
        public static readonly IconString MacroShelf = new IconString("{0} Macro Shelf", AppIcons.SI_LUA);
    }
}
