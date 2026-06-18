using System.Text;
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
        public static readonly InterpolatedString<string> UnsavedChangesText_SingleFile = "The file {0} has unsaved changes.\nAre you sure you wish to close it?";

        public static readonly FixedString LinkCopied = "Link was copied!";
        public static readonly FixedString URLOpened = "URL was opened!";
        public static readonly FixedString FilterInput = "Filter";

        public static readonly IconString WikiLink = new IconString("{0} Documentation Wiki", AppIcons.SI_GenericWiki);
        public static readonly FixedString WikiLink_NoIcon = $"{AppIcons.SI_GenericWiki}";
        public static readonly FixedString WikiLink_Tooltip = "Open wiki for usage documentation";
    }
}
