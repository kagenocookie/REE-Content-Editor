using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class Errors
    {
        public static readonly FixedString FileLoad_NotReady_Title = "You're too fast";
        public static readonly FixedString FileLoad_NotReady_Message = "Please wait for the workspace to load up before opening files.";
        public static readonly FixedString FileLoad_GameUnset_Title = "Game unset";
        public static readonly FixedString FileLoad_GameUnset_Message = "Select a game first.";
        public static readonly FixedString FileLoad_FileNotFound_Title = "File not found";
        public static readonly InterpolatedString<string> FileLoad_FileNotFound = new("File could not be found:\n{0}");
        public static readonly FixedString FileLoad_Unsupported_Title = "Unsupported file";
        public static readonly FixedString FileLoad_ImportError = "Failed to import";
        public static readonly FixedString FileLoad_InvalidArchive = "The given archive files couldn't be processed into bundles";
        public static readonly InterpolatedString<int, string> FileLoad_UnsupportedFormat = new("This file format ({0}) is not yet supported:\n{1}") { Converter1 = (f) => ((KnownFileFormats)f).ToString() };
        public static readonly InterpolatedString<string> FileLoad_UnsupportedFormatRSZ = new("""
            File could not be opened or is not supported:
            {0}

            This is an RSZ-based file format, make sure you have the correct game selected.
            """);
        public static readonly InterpolatedString<string> FileLoad_NotEditable = "File is not supported for editing:\n{0}";
        public static readonly InterpolatedString<string> FileLoad_UnknownError = "File could not be opened or is not supported:\n{0}";
        public static readonly InterpolatedString<string> ExeNotFound = "Game executable not found at: {0}";

        public static readonly FixedString PatchFailed = "Failed to execute patcher";
        public static readonly FixedString PatchRevertFailed = "Failed to revert patches";

        public static readonly FixedString OpenedInExternalEditor_Title = "Opened in external editor";
        public static readonly FixedString OpenedInExternalEditor_Text = "The file has been opened in the configured eternal editor.";
    }
}
