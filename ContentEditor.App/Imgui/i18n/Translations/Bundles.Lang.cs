using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class Bundles
    {
        public static readonly InterpolatedString<string, string> ConfirmDeleteBundleFile = new InterpolatedString<string, string>("Are you sure you want to delete {0} from {1}?");
    }
}
