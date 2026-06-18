using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class Animations
    {
        public static readonly FixedString UndefinedBoneInMot = """
            This bone is not defined in this motion's bone list.
            The name will get lost on save and the bone likely won't animate ingame.
            If you want it to animate, add the bone to the Bones list.
            """;
    }
}
