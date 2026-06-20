using System.Text;
using ContentEditor.Core;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class Settings
    {
        public static readonly FixedString Warn_Transparency = "Window transparency change will only be applied after restarting the app";
        public static readonly FixedString Warn_UndoRedoBinding = "While focused, text inputs will not correctly take this setting into account and still use the default layout keys for undo/redo";
        public static readonly FixedString Note_CustomGame = "*This is a custom defined game. The app may need an upgrade to fully support all files, some files may not load correctly.";
        public static readonly FixedString Note_FullySupported = "*This is a fully supported game, game specific data can be fetched automatically.";
        public static readonly FixedString Note_ChangesNeedRestart = "*Changes to these settings may require a restart of the app before they get applied";

        public static readonly FixedString Button_UpdateUI = "Update UI";
        public static readonly FixedString Button_CheckUpdatesNow = "Check now";
        public static readonly FixedString Button_OpenThemeEditor = "Open Theme Editor";

        public static readonly FixedString Section_Performance = "Performance";
        public static readonly FixedString Section_Debug = "Debug";

        public static readonly FixedString Section_Cache = "Cache";
        public static readonly FixedString Section_BundleDefaults = "Default Bundle Settings";
        public static readonly FixedString Section_Fields = "Fields";
        public static readonly FixedString Section_FPS = "FPS";
        public static readonly FixedString Section_DateTime = "Date & Time";
        public static readonly FixedString Section_Lang = "Language";
        public static readonly FixedString Section_Animator = "Animator";
        public static readonly FixedString Section_GroupList = "GroupList";

        public static readonly FixedString Group_Preferences = "Preferences";
        public static readonly FixedString Group_Display = "Display";
        public static readonly FixedString Group_Hotkeys = "Hotkeys";
        public static readonly FixedString Group_Games = "Games";

        public static readonly FixedString Group_General = "General";
        public static readonly FixedString Group_Editing = "Editing";
        public static readonly FixedString Group_Bundles = "Bundles";
        public static readonly FixedString Group_Theme = "Theme";
        public static readonly FixedString Group_Global = "Global";
        public static readonly FixedString Group_Pak = "Pak Browser";
        public static readonly FixedString Group_Scene = "Scene";
        public static readonly FixedString Group_Mesh = "Mesh Viewer";
        public static readonly FixedString Group_Texture = "Texture Viewer";
        public static readonly FixedString Group_UVS = "UVS Editor";
        public static readonly FixedString Group_Resident = "Resident Evil";
        public static readonly FixedString Group_Monster = "Monster Hunter";
        public static readonly FixedString Group_Other = "Other";
        public static readonly FixedString Group_Custom = "Custom";

        public static readonly FixedString Section_AddCustom = "Add Custom Game";
        public static readonly FixedString CustomGames = "Custom Games";
        public static readonly FixedString Custom_ShortName = "Short Name";
        public static readonly TextTooltip GamePath = new ("Game Path", "The full path to the game. Should point to the folder containing the .exe and .pak files");
        public static readonly TextTooltip ExePath = new ("Game Executable", "The full path to the game executable.");
        public static readonly TextTooltip FileList = new ("File List", "Defining a custom path here may not be required if it's at least a partially supported game.\nCan also be used in case of issues with automatic downloads.");
        public static readonly TextTooltip FileList_Custom = new ("File List", "This setting should point to a filepath containing a list of all files used by the game.");
        public static readonly TextTooltip ExtractPath = new ("Game Extract Path", "This is the default path used when extracting files. Can be left empty.");
        public static readonly TextTooltip RszPath = new ("RSZ Template JSON Path", "This setting should point to the correct rsz*.json for the chosen game.\nFor not yet fully supported games, you may need to manually provide the path to a valid RSZ JSON template before some files can be opened.");
        public static readonly TextTooltip RszPath_Custom = new ("Custom RSZ JSON Path", "The default RSZ json file is fetched automatically.\nChange this only if you know what you're doing - mainly for accessing files from older game versions");
        public static readonly TextTooltip ExternalTextEditor = new ("External Text Editor", "The preferred external tool to use for editing text files");

        public static readonly InterpolatedString<TranslatableBase> ToolPathArgsLabel = "{0} Command Arguments";
        public static readonly FixedString ToolPathArgsTooltip = """
            The command line arguments to use when running the tool
            {COMMAND} will be replaced with the predefined arguments.
            If {COMMAND} is not present, it will be auto-appended at the end.
            """;

        public static readonly FixedString ShowFPS = "Show FPS";
        public static readonly FixedString DateFormat = "Date Format";

        public static readonly FixedString Key_Ctrl = "Ctrl";
        public static readonly FixedString Key_Shift = "Shift";
        public static readonly FixedString Key_Alt = "Alt";

        public static readonly FixedString Author = "Author";
        public static readonly FixedString Homepage = "Homepage";
        public static readonly FixedString Description = "Description";
        public static readonly FixedString Theme = "Theme";
        public static readonly FixedString BackgroundColor = "Background Color";

        public static readonly FixedString PreferredLanguage = "Preferred Language";
        public static readonly FixedString MinLogLevel = "Minimum logging level";
        public static readonly FixedString LogToFile = "Output logs to file";
        public static readonly FixedInterpolatedString<string> LogToFile_Tooltip = new("If checked, any logging will also be output to file {0}.\nChanging this setting requires a restart of the app.", FileLogger.DefaultLogFilePath);

        public static readonly TextTooltip AutoUpdate = new TextTooltip("Automatically check for Updates", "Will occasionally check GitHub for new releases.");
        public static readonly TextTooltip EnableKeyboardNav = new TextTooltip("Enable keyboard navigation", "Whether to enable navigating between fields using arrow keys.");
        public static readonly TextTooltip DisableFileCloseWarning = new TextTooltip("Disable Open File Warning When Closing Editor Windows", "Whether to disable the warning notification when a window is closed that references an open file.");
        public static readonly TextTooltip LoadFromNatives = new TextTooltip("Load files from natives/ folder", "If checked, the app will prefer to load loose files from the active game's natives folder instead of packed files, similar to how the game would.");
        public static readonly TextTooltip UseSubPakForLooseTextures = new TextTooltip("Store textures into sub pak files (>= MHWilds)", "Whether to store textures in sub paks even for loose file output.\nShouldn't be needed anymore with current REFramework versions, but might be needed in case of issues with newer games");
        public static readonly TextTooltip UseSymlinkPatching = new TextTooltip("Use symbolic links for patching", "Whether to allow using symbolic links for patched files when possible instead of direct file copy.\nThis can make the patching faster and reduce unnecessary disk writes and storage.\nRequires Content Editor to be launched with admin permissions on Windows.");
        public static readonly TextTooltip RemoteDataSource = new TextTooltip("Resource data source", "The source from which to check for updates and download game-specific resource cache files.\nWill use the default GitHub repository if unspecified.");
        public static readonly TextTooltip EnableGpuTexCompression = new TextTooltip("Enable GPU texture compression", "Whether to enable using the much faster GPU-based compression method.\nCurrently only available on Windows.\nCan be disabled in case of issues, so that CPU-based compression is used instead.");

        public static readonly TextTooltip GameConfigBasePath = new TextTooltip("Game Config Base Path", "The folder path that contains the game specific entity configurations. Will use relative path config/ by default if unspecified.");
        public static readonly TextTooltip ResourcesFilepath = new TextTooltip("Resource data storage path", "The folder to use for storing the auto-downloaded game specific resource files.");
        public static readonly TextTooltip CacheFilepath = new TextTooltip("Cache file path", "The folder to use for general file caching. Must not be empty.");
        public static readonly TextTooltip ThumbnailCacheFilepath = new TextTooltip("Thumbnail cache file path", "The folder that cached thumbnails should be stored in. Must not be empty.");
        public static readonly TextTooltip BookmarksFilepath = new TextTooltip("User data file path", "The folder in which user created bookmarks and other data should be stored. Must not be empty.");
        public static readonly TextTooltip BlenderPath = new TextTooltip("Blender Path", "The path to an installed blender executable. Will be used for interacting with .blend files.");
        public static readonly TextTooltip FontSize = new TextTooltip("UI Font Size", "The default font size for drawing text.");
        public static readonly TextTooltip MaxUnpackThreads = new TextTooltip("Max unpack threads", "The maximum number of threads to be used when unpacking.\nThe actual thread count is determined automatically by the .NET runtime.");
        public static readonly TextTooltip AutoExpand = new TextTooltip("Auto-expand field count", "RSZ object fields with less than the defined number of fields will initially auto expand.");
        public static readonly TextTooltip MaxFPS = new TextTooltip("Max FPS", "The maximum FPS for rendering.");
        public static readonly TextTooltip MaxFPSBackground = new TextTooltip("Max FPS in background", "The maximum FPS when the editor window is not focused.");
        public static readonly TextTooltip ClockFormat = new TextTooltip("12-hour Clock", "Switch the time format from 24-hour to 12-hour clock.");
        public static readonly TextTooltip UseFullscreenAnimPlayback = new TextTooltip("Fullscreen Animation Playback Overlay", "Whether to keep the animation playback overlay in the top-right corner of the Mesh Viewer or make it fullscreen.");
        public static readonly TextTooltip ExpandSettings = new TextTooltip("Auto-Expand Settings", "Whether the setting groups should be expanded by default.");
        public static readonly TextTooltip PrettyFieldLabels = new TextTooltip("Simplify field labels", "Whether to simplify field labels instead of showing the raw field names (e.g. \"Target Object\" instead of \"_TargetObject\").");
        public static readonly TextTooltip ShowQuaternionsAsEuler = new TextTooltip("Use Euler angles for quaternions", "Whether quaternions should be displayed as euler angles.");
        public static readonly TextTooltip QuaternionsDisableAutoNormalize = new TextTooltip("Disable quaternion auto-normalization", "Disables automatic normalization of quaternions for transforms.\nThis can make the object appear skewed and will cause issues with some functionality.\nUse at your own risk.");
        public static readonly TextTooltip PauseAnimPlayerOnSeek = new TextTooltip("Pause Animation Player on seek", "Whether to pause the animation player while seeking with the slider.");
        public static readonly TextTooltip BundleDefaultSaveFullPath = new TextTooltip("Save bundle files with full path", "When checked, will always default to saving with the full relative path instead of the root bundle folder when adding new files to the active bundle.");
        public static readonly TextTooltip MaxUndoSteps = new TextTooltip("Max undo steps", "The maximum number of steps you can undo. Higher number means a bit higher memory usage after longer sessions.");
        public static readonly TextTooltip CustomBundleBasePath = new TextTooltip("Base Bundle File Path", """
            The default base path added to every custom file when saving a new file to bundle.
            Accepts replace parameters for {AUTHOR} or {BUNDLE_NAME}
            For example, "{AUTHOR}/{BUNDLE_NAME}/" for a bundle called Johnmods with a bundle named BigMod would use "Johnmods/BigMod/" as the target path for every new file.
            This makes it easier to set up paths so they're not just all at the root folder in the published mod.
            """);

        public static readonly FixedString Bind_Undo = "Undo";
        public static readonly FixedString Bind_Redo = "Redo";
        public static readonly FixedString Bind_Save = "Save Open Files";
        public static readonly FixedString Bind_Open = "Open File";
        public static readonly FixedString Bind_Back = "Back";
        public static readonly FixedString Bind_Close = "Close Current Window";
        public static readonly FixedString Bind_HomePage = "Toggle Home Page";
        public static readonly FixedString Bind_OpenPakBrowser = "Open PAK File Browser";
        public static readonly FixedString Bind_OpenMacroShelf = "Open Macro Shelf";
        public static readonly FixedString Bind_PakBrowser_OpenBookmarks = "Open Bookmarks";
        public static readonly FixedString Bind_PakBrowser_Bookmark = "Bookmark Current Path";
        public static readonly FixedString Bind_PakBrowser_JumpToPageTop = "Jump to page Top";
        public static readonly FixedString Bind_MeshViewer_PauseAnim = "Pause/Play";
        public static readonly FixedString Bind_MeshViewer_NextAnimFrame = "Next Frame";
        public static readonly FixedString Bind_MeshViewer_PrevAnimFrame = "Previous Frame";
        public static readonly FixedString Bind_MeshViewer_IncreaseAnimSpeed = "Increase Playback Speed";
        public static readonly FixedString Bind_MeshViewer_DecreaseAnimSpeed = "Decrease Playback Speed";
        public static readonly FixedString Bind_TextureViewer_ResetView = "Reset View";
        public static readonly FixedString Bind_TextureViewer_ZoomIn = "Zoom In";
        public static readonly FixedString Bind_TextureViewer_ZoomOut = "Zoom Out";
        public static readonly FixedString Bind_TextureViewer_NextChannel = "Next Channel";
        public static readonly FixedString Bind_TextureViewer_PrevChannel = "Previous Channel";
        public static readonly FixedString Bind_Scene_Focus3D = "Focus Selected";
        public static readonly FixedString Bind_Scene_FocusUI = "Show Selected in UI";
        public static readonly FixedString Bind_Scene_Hide = "Hide Selected";
        public static readonly FixedString Bind_Scene_UnhideAll = "Unhide All";
        public static readonly FixedString Bind_Scene_Delete = "Delete Selected";
        public static readonly FixedString Bind_UVS_Pause = "Pause/Play";
        public static readonly FixedString Bind_UVS_NextPattern = "Next Pattern";
        public static readonly FixedString Bind_UVS_PrevPattern = "Previous Pattern";
        public static readonly FixedString Bind_UVS_IncreaseSpeed = "Increase Playback Speed";
        public static readonly FixedString Bind_UVS_DecreaseSpeed = "Decrease Playback Speed";

        public static readonly TranslatedEnum<ImGuiKey> Keys = new TranslatedEnum<ImGuiKey>();

        static Settings()
        {
            AppConfig.Instance.PreferredLanguage.ValueChanged += (_) => Keys.ResetNames();
        }
    }
}
