using System.Text;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class SceneView
    {
        public static readonly FixedString Label_NoActiveScene = "No active scene. Activate one from the Scenes menu.";
        public static readonly InterpolatedString<string> Label_LoadingScene = new InterpolatedString<string>("Loading scene {0} ...");
        public static readonly InterpolatedString<string> Label_SceneNo3D = new InterpolatedString<string>("Scene {0} has no 3D content");
        public static readonly FixedString Label_EditTypeA = "Editing: --";
        public static readonly FixedString Label_EditTypeB = "Editing: ";

        public static readonly FixedString Tooltip_ReOpenScnEditor = "Re-Open Scene Editor";
        public static readonly FixedString Tooltip_OpenMacroShelf = "Open Macro Shelf";
        public static readonly FixedString Tooltip_FocusObj = "Focus on target object";
        public static readonly IconString MenuItem_CamControls = new IconString("{0} Controls", AppIcons.SI_GenericCamera);
    }
}
