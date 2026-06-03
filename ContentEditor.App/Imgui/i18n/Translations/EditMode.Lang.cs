using ContentEditor.Core;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class EditMode
    {
        public static readonly FixedString Button_ResetGeometry = "Reset Preview Geometry";
        public static readonly FixedString Button_BakeNavmesh = "Bake navmesh ...";
        public static readonly FixedString StoredFiles = "Stored Files";

        public static readonly FixedString Navmesh_NodeEditWarning = "Don't manually edit links or pair nodes unless you're sure you know what you're doing";
        public static readonly FixedString Navmesh_SceneModeToolbar = "Nav Mesh Controls";
        public static readonly FixedString Navmesh_SceneMode = "Scene Click Action";
        public static readonly FixedString Navmesh_AttrFill = "Fill Mode";
        public static readonly FixedString Navmesh_Attribute = "Attribute Filter";
    }
}
