using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class PakBrowser
    {
        public static readonly InterpolatedString<string> ConfirmDeleteBookmarks = new InterpolatedString<string>("Are you sure you want to delete all custom bookmarks for {0}?");

    }
}
