using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class PakBrowser
    {
        public static readonly InterpolatedString<string> ConfirmDeleteBookmarks = new InterpolatedString<string>("Are you sure you want to delete all custom bookmarks for {0}?");

        public static readonly FixedString ExtractFolder = "Extract Folder to...";
        public static readonly FixedString ExtractFileKeepPaths = "Extract File (Maintain Paths)...";
        public static readonly FixedString ExtractFile = "Extract File to...";
    }
}
