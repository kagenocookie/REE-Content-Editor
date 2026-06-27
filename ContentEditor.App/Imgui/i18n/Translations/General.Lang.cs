using System.Text;
using System.Text.Json.Serialization;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class General
    {
        public static readonly FixedString ConfirmTitle = "Confirm Action";
        public static readonly FixedString UnsavedChanges = "Unsaved changes";
        public static readonly FixedString UnsavedChangesText = "Some files have unsaved changes. Are you sure you wish to close the window?";
        public static readonly FixedString UnsavedChangesText_ThisFile = "You have unsaved changes in this file, do you wish to save the file first?";
        public static readonly FixedString UnsavedChangesCloseAll = "Some files have unsaved changes. If you continue, all changes will be lost.\nYou can reopen the files through the File menu";
        public static readonly InterpolatedString<string> UnsavedChangesText_SingleFile = "The file {0} has unsaved changes.\nAre you sure you wish to close it?";

        public static readonly FixedString FileClose_KeptOpenMessage = """
            Window has been closed but the file is still kept open in case it's needed.
            If you want to re-open it or fully close it down, you can do that from the File > Open files menu.
            Attempting to re-open a file from the same path will not reload the file unless it's closed down first.
            """;
        public static readonly FixedString FileClose_MultipleEditorsMessage = """
            There are other windows referencing this file so the file is still kept open in memory.
            If you want to re-open it or fully close it down, you can do that from the File > Open files menu.
            Attempting to re-open a file from the same path will not reload the file unless it's closed down first.
            """;

        public static readonly FixedString UnsavedChanges_PreventReopen = """
            The file has unsaved changes. Before reopening it, make sure you saved any changes you need and or the file down through the opened files menu.
            """;

        public static readonly FixedString LinkCopied = "Link was copied!";
        public static readonly FixedString URLOpened = "URL was opened!";
        public static readonly FixedString FilterInput = "Filter";

        public static readonly IconString WikiLink = new IconString("{0} Documentation Wiki", AppIcons.SI_GenericWiki);
        [JsonIgnore] public static readonly FixedString WikiLink_NoIcon = $"{AppIcons.SI_GenericWiki}";
        public static readonly FixedString WikiLink_Tooltip = "Open wiki for usage documentation";

        [JsonIgnore] public static readonly InterpolatedString<TranslatableBase> BlankPrefix = $"{AppIcons.SI_Blank} {{0}}";
    }
}
