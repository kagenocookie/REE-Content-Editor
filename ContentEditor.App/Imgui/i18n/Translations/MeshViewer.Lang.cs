using System.Text;
using ContentEditor.Core;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class MeshViewer
    {
        public static readonly IconString Title_Animations = new("{0} Animations", AppIcons.SI_Animation);

        public static readonly FixedString Error_MaterialCountMismatch = """
            Mesh material count does not match MDF2 material count. Textures won't display correctly ingame.
            Ensure that both counts match.
            """;

        public static readonly FixedString Error_MaterialNotFound = """
            Mesh references material names that are not present in the selected MDF2.
            Such submeshes will be invisible ingame.
            """;
    }
}
