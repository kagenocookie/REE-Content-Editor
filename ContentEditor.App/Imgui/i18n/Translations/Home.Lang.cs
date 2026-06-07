using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class Home
    {
        public static readonly IconString LaunchGame = new IconString("{0} Launch Game", AppIcons.Play);
        public static readonly FixedString LaunchGame_LoosePatch = "Launch Game with Loose File patch";
        public static readonly FixedString LaunchGame_PakPatch = "Launch Game with Pak File patch";
        public static readonly FixedString ApplyPatches_Pak = "Apply patches (PAK)";
        public static readonly FixedString ApplyPatches_Loose = "Apply patches (Loose File)";
        public static readonly FixedString ApplyPatches_CustomPath = "Patch to...";
        public static readonly FixedString ApplyPatches_Revert = "Revert patches";
        public static readonly FixedString SupportDevelopment = "Support development (Ko-Fi)";
        public static readonly FixedString NewVersion_Unspecific = "New version available!";
        public static readonly InterpolatedString<string> NewVersion_Specific = "New version ({0}) available!";

        public static readonly FixedString ShowFavoritesOnly = "Show favorite files";
        public static readonly FixedString ShowFavoritesOnly_NoFavorites = "There are currently no files marked as favorite.\nThis can be done through the right click context menu on a recently opened file.";
        public static readonly IconString File_MarkAsFavorite = new IconString("{0} Mark as favorite", AppIcons.StarEmpty);
        public static readonly IconString File_RemoveFromFavorites = new IconString("{0} Remove from favorites", AppIcons.Star);

        public static readonly FixedString Menu_File = "File";
        public static readonly FixedString Menu_CreateNew = "Create New";
        public static readonly FixedString Menu_Open = "Open ...";
        public static readonly FixedString Menu_SaveAll = "Save modified files";
        public static readonly FixedString Menu_RevertAll = "Revert modified files";
        public static readonly FixedString Menu_TooltipNoModifiedFiles = "No files have been modified yet.";
        public static readonly FixedString Menu_OpenedFiles = "Opened files";
        public static readonly FixedString Menu_NoFilesOpen = "No files open";
        public static readonly FixedString Menu_CloseAll = "Close all";
        public static readonly FixedString Menu_RecentFiles = "Recent files";
        public static readonly FixedString RecentFiles_None = "No recent files";
        public static readonly FixedString Menu_Edit = "Edit";
        public static readonly FixedString Menu_DataGeneration = "Data Generation";
        public static readonly FixedString Menu_Windows = "Windows";
        public static readonly FixedString Menu_Scenes = "Scenes";

        public static readonly InterpolatedString<string> ActiveGame = "Game: {0}";
        public static readonly InterpolatedString<string> ActivePlatform = "Platform: {0}";
        public static readonly InterpolatedString<string> ActiveBundle = "Active Bundle: {0}";
        public static readonly InterpolatedString<string> NamedBundle = "Bundle: {0}";
        public static readonly FixedString OtherPlatforms = "Other platforms (untested)";
        public static readonly FixedString UninitializedBundles = "* Uninitialized bundle folders";

        public static readonly FixedString BundleDialog_Title = "Bundle Creation";
        public static readonly InterpolatedString<string> BundleDialog_Text_PAK = "Select name for the bundle to be created from the loose mod:\n{0}";
        public static readonly InterpolatedString<string> BundleDialog_Text_Loose = "Select name for the bundle to be created from the PAK file:\n{0}";
    }
}
