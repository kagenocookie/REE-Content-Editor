using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class Home
    {
        public static readonly IconString LaunchGame = new IconString("{0} Launch Game", AppIcons.Play);
        public static readonly FixedString LaunchGame_LoosePatch = "Launch Game with Loose File Patch";
        public static readonly FixedString LaunchGame_PakPatch = "Launch Game with Pak File Patch";
        public static readonly FixedString ApplyPatches_Pak = "Apply Patches (PAK)";
        public static readonly FixedString ApplyPatches_Loose = "Apply Patches (Loose File)";
        public static readonly FixedString ApplyPatches_CustomPath = "Patch to...";
        public static readonly IconString ApplyPatches_Revert = new IconString("{0} Revert Patches", AppIcons.SI_Reset);
        public static readonly FixedString SupportDevelopment = "Support development (Ko-Fi)";
        public static readonly FixedString NewVersion_Unspecific = "New version available!";
        public static readonly InterpolatedString<string> NewVersion_Specific = "New version ({0}) available!";

        public static readonly FixedString ShowFavoritesOnly = "Show favorite files";
        public static readonly FixedString ShowFavoritesOnly_NoFavorites = "There are currently no files marked as favorite.\nThis can be done through the right click context menu on a recently opened file.";
        public static readonly IconString File_MarkAsFavorite = new IconString("{0} Mark as favorite", AppIcons.StarEmpty);
        public static readonly IconString File_RemoveFromFavorites = new IconString("{0} Remove from favorites", AppIcons.Star);

        public static readonly FixedString Menu_File = "File";
        public static readonly FixedString Menu_CreateNew = "Create New";
        public static readonly FixedString Menu_Open = "Open...";
        public static readonly FixedString Menu_SaveAll = "Save Modified Files";
        public static readonly FixedString Menu_RevertAll = "Revert Modified Files";
        public static readonly FixedString Menu_TooltipNoModifiedFiles = "No files have been modified yet.";
        public static readonly FixedString Menu_OpenedFiles = "Opened Files";
        public static readonly FixedString Menu_NoFilesOpen = "No files open";
        public static readonly FixedString Menu_CloseAll = "Close all";
        public static readonly FixedString Menu_RecentFiles = "Recent Files";
        public static readonly FixedString RecentFiles_None = "No recent files";
        public static readonly FixedString Menu_Edit = "Edit";
        public static readonly FixedString Menu_DataGeneration = "Data Generation";
        public static readonly FixedString Menu_Windows = "Windows";
        public static readonly FixedString Menu_Scenes = "Scenes";

        public static readonly InterpolatedString<string> Tooltip_Undo = "Undo ({0})";
        public static readonly InterpolatedString<string> Tooltip_Redo = "Redo ({0})";

        public static readonly InterpolatedString<string> ActiveGame = "Game: {0}";
        public static readonly InterpolatedString<string, char> ActivePlatform = "{1} Platform: {0}";
        public static readonly InterpolatedString<string, char> ActiveBundle = "{1} Active Bundle: {0}";
        public static readonly InterpolatedString<string> NamedBundle = "Bundle: {0}";
        public static readonly IconString BundleFilter = new IconString("{0} Search Bundles", AppIcons.SI_GenericMagnifyingGlass);
        public static readonly FixedString OtherPlatforms = "Other platforms (untested)";
        public static readonly FixedString UninitializedBundles = "* Uninitialized bundle folders";

        public static readonly FixedString BundleDialog_Title = "Bundle Creation";
        public static readonly InterpolatedString<string> BundleDialog_Text_PAK = "Select name for the bundle to be created from the PAK file:\n{0}";
        public static readonly InterpolatedString<string> BundleDialog_Text_Loose = "Select name for the bundle to be created from the loose mod:\n{0}";

        public static readonly FixedString CreateNew_Lua = "Lua Script";
        public static readonly IconString Scenes_New = new IconString("{0} New scene", AppIcons.SI_GenericAdd);
        public static readonly IconString Hint_SearchRecentFiles = new IconString("{0} Search Recent Files", AppIcons.SI_GenericMagnifyingGlass);

        public static readonly FixedString Button_OpenFile = "Open File";
        public static readonly IconString Button_SupportDev = new IconString("{0} Support Development", AppIcons.SI_GenericHeart);
        public static readonly FixedString Tooltip_BrowseFiles = "Browse Game Files";
        public static readonly FixedString Tooltip_ThemeEditor = "Theme Editor";
        public static readonly FixedString Tooltip_Settings = "Settings";
        public static readonly FixedString Tooltip_GitHub = "GitHub";
        public static readonly FixedString Tooltip_Wiki = "Wiki";
        public static readonly FixedString Tooltip_Discord = "Discord";
        public static readonly FixedString Tooltip_NewVersion = "New version available!";
        public static readonly InterpolatedString<string> Tooltip_LatestVersion = "New version {0} available!";
        public static readonly FixedString Tooltip_FirstTimeSetup_Theme = "You can modify or create new custom themes through Edit > Theme Editor.";
        public static readonly FixedString Tooltip_FirstTimeSetup_SceneBGColor = "You can change this color at any time in Settings > Display > Theme.";
        public static readonly FixedString Tooltip_FirstTimeSetup_CustomGame = "Select this if you wish to configure a game outside of the predefined list.\nCustom games may not fully work.";
        public static readonly FixedString Tooltip_FirstTimeSetup_CustomGameNote = "This is a custom defined game. The app may need an upgrade to fully support all files, some files may not load correctly.";
        public static readonly FixedString Tooltip_FirstTimeSetup_GamePath = "This is the path to the game (where the .exe file is located).";
        public static readonly FixedString Tab_FirstTimeSetup = "First Time Setup";
        public static readonly FixedString Tab_Bundles = "Bundles";
        public static readonly FixedString Tab_Updates_A = "Updates *";
        public static readonly FixedString Tab_Updates_B = "Updates";
        public static readonly FixedString Tab_LatestChanges = "Latest Changes";
        public static readonly FixedString Tab_GameSetup = "Game Setup";
        public static readonly FixedString Sep_ThemeColor = "Choose a theme and color";
        public static readonly FixedString Sep_ChooseGame = "Choose the game you wish to mod";
        public static readonly FixedString FirstTimeSetup_Note = "Complete the First Time Setup to select a game.";
        public static readonly FixedString FirstTimeSetup_CustomGame = "Custom Game";
        public static readonly InterpolatedString<string> AppVersion = "Version: {0}";

    }
}
