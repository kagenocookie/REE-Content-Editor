using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class Tools
    {
        public static readonly FixedString DataGeneration_RebuildRSZ = "Rebuild RSZ patch data";
        public static readonly FixedString DataGeneration_RebuildEFX = "Rebuild EFX data";
        public static readonly FixedString DataGeneration_ExtensionCache = "Generate file extension cache";
        public static readonly FixedString DataGeneration_ListFile = "Generate list file";
        public static readonly FixedString DataGeneration_Bookmarks = "Generate bookmarks from entities";

        public static readonly FixedString ImguiTestWindow = "IMGUI Test Window";
        public static readonly FixedString FileTester = "File testing";
        public static readonly FixedString IconList = "Icon List";
        public static readonly FixedString DumpTranslations = "Dump translations";
        public static readonly FixedString DumpLuaTypes = "Dump editor LUA types";
    }
}
