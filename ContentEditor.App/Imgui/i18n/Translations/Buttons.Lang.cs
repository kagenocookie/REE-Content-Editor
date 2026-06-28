using ContentEditor.Core;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class Buttons
    {
        public static readonly FixedString Add = "Add";
        public static readonly FixedString Open = "Open";
        public static readonly FixedString Save = "Save";
        public static readonly FixedString Rename = "Rename";
        public static readonly FixedString SaveAs = "Save As...";
        public static readonly FixedString SaveCopy = "Save Copy To...";
        public static readonly FixedString SaveToBundle = "Save to Bundle";
        public static readonly FixedString SaveToBundleNewFile = "Save to Bundle as New File";
        public static readonly FixedString SeeChanges = "See Changes";
        public static readonly FixedString Revert = "Revert";
        public static readonly FixedString Retry = "Retry";
        public static readonly FixedString Exit = "Exit";
        public static readonly FixedString Open_Folder = "Open folder";
        public static readonly FixedString Open_ContainingFolder = "Open Containing Folder";
        public static readonly FixedString Open_GameFolder = "Open Game Folder";
        public static readonly FixedString Open_BundleFolder = "Open Bundle Folder";
        public static readonly IconString Open_CurrentBundleFolder = new IconString("{0} Open Current Bundle Folder", AppIcons.SI_FolderOpen);
        public static readonly FixedString Open_CurrentBundleTextEditor = "Open Current Bundle Folder in Text Editor";
        public static readonly FixedString ClearRecent = "Clear recent files";
        public static readonly FixedString Close = "Close";
        public static readonly FixedString Confirm = "Confirm";
        public static readonly FixedString Cancel = "Cancel";
        public static readonly FixedString Duplicate = "Duplicate";
        public static readonly FixedString Copy = "Copy";
        public static readonly FixedString Copy_Batch = "Batch copy";
        public static readonly FixedString Copy_Json = "Copy as JSON";
        public static readonly FixedString Paste = "Paste";
        public static readonly FixedString Paste_Replace = "Paste (replace values)";
        public static readonly FixedString Paste_Hierarchy = "Paste (replace hierarchy)";
        public static readonly FixedString Paste_Json = "Paste from JSON";
        public static readonly FixedString Delete = "Delete";
        public static readonly FixedString DeleteFile = "Delete File";
        public static readonly FixedString Clear = "Clear";
        public static readonly FixedString Clear_Tags = "Clear All Tags";
        public static readonly IconString Undo = new IconString("{0} Undo", AppIcons.SI_Undo);
        public static readonly IconString Redo = new IconString("{0} Redo", AppIcons.SI_Redo);
        public static readonly FixedString Create = "Create";
        public static readonly FixedString Reload = "Reload";
        public static readonly FixedString ForceReload = "Force Reload";
        public static readonly IconString ForceReimport = new("{0} Re-Import", AppIcons.SI_Update);
        public static readonly FixedString UpdateSceneCache = "Update Scene Cache";
        public static readonly FixedString NewWorkspace = "Open New Workspace";
        public static readonly FixedString Show_GameObject = "Show GameObject";

        public static readonly FixedString CheckUpdates = "Check For Updates";
        public static readonly FixedString CheckForDataUpdate = "Check for Updated Game Data Cache";
        public static readonly FixedString ConfigureGames = "Configure Games...";
        public static readonly FixedString NewBundle = "New Bundle";
        public static readonly FixedString NewBundleFromPAK = "Create from Archive/PAK File";
        public static readonly FixedString NewBundleFromLoose = "Create from Loose File Mod";
        public static readonly InterpolatedString<string, char> BundleUnload = "{1} Unload Current Bundle ({0})";
        public static readonly FixedString BundleFileRescan = "Rescan Bundle Files";
        public static readonly FixedString BundlePublish = "Publish Mod";
    }
}
