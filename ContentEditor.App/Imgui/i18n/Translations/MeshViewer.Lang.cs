using System.Text;
using ContentEditor.Core;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class MeshViewer
    {
        public static readonly FixedString Title_Editor = "Editor";
        public static readonly FixedString Editor_Objects = "Objects";
        public static readonly FixedString Editor_Submeshes = "Submeshes";
        public static readonly FixedString Editor_NoSubmeshes = "No submeshes are available.";
        public static readonly FixedString Editor_ModeObject = "Object";
        public static readonly FixedString Editor_ModeEdit = "Edit";
        public static readonly FixedString Editor_Options = "Options";
        public static readonly FixedString Editor_OptionsTitle = "Mesh Editor Options";
        public static readonly FixedString Editor_Vertices = "Vertices";
        public static readonly FixedString Editor_Selected = "Selected";
        public static readonly FixedString Editor_VertexInstructions = "Click a visible vertex to select it, or click and drag to box-select. Hold Shift to add to the selection, or Ctrl to toggle. Press G to move the selection. While moving, X, Y, or Z constrains movement to that global axis; Shift plus an axis excludes it. Left-click or Enter confirms, and right-click or Escape cancels.";
        public static readonly FixedString Editor_Moving = "Moving vertices";
        public static readonly FixedString Editor_VertexSize = "Vertex size";
        public static readonly FixedString Editor_SelectionRadius = "Selection radius";
        public static readonly FixedString Editor_MirrorAxes = "Global axis mirroring";
        public static readonly FixedString Editor_MirrorRadius = "Mirror radius";
        public static readonly FixedString Editor_ExportReminder = "*Use 'Export Mesh' to write mesh with Edits*";
        public static readonly FixedString Editor_StayOnTop = "Stay on top";
        public static readonly FixedString Display_Default = "Default";
        public static readonly FixedString Display_Solid = "Solid";
        public static readonly FixedString Display_Wireframe = "Wireframe";
        public static readonly IconString Title_Animations = new("{0} Animations", AppIcons.SI_Animation);

        public static readonly FixedString Error_MaterialCountMismatch = """
            Mesh material count does not match MDF2 material count. Textures won't display correctly ingame.
            Ensure that both counts match.
            """;

        public static readonly FixedString Error_MaterialNotFound = """
            Mesh references material names that are not present in the selected MDF2.
            Such submeshes will be invisible ingame.
            """;

        public static readonly FixedString BlendImportSettings = "Blend File Import Settings";
        public static readonly FixedString BlendImportSettingsMissing = "No .blend import settings found for the file.";
        public static readonly FixedString BlendSceneUnavailable = "Scene info unavailable.";
        public static readonly FixedString ImportConfig = "Import Config";
        public static readonly FixedString ImportConfigRename = "New name";
        public static readonly FixedString Armature = "Armature";
        public static readonly FixedString CreateNewBlendImportConfig = "Add new import config";
        public static readonly FixedString ImportAllMeshes = "Import all meshes";
        public static readonly FixedString IncludeTangents = "Include Tangents";
        public static readonly FixedString IncludeTangentsToolTip = """
            Whether to export the mesh with Blender's tangets or have them auto generate.
            Doesn't matter for most meshes, but can cause visual issues for some shader types if not included.
            Tangents are important for some meshes, but Blender might not always successfully export them.
            """;
        public static readonly FixedString ApplyRotations = "Apply Armature Rotation";
        public static readonly FixedString ApplyRotationsToolTip = """
            Whether to automatically apply any rotations on the armature.
            Rotation can cause issues with skeleton orientation on import, this will attempt to automatically resolve that.
            """;

        public static readonly FixedString RemoveStreamingMesh = "Remove Streaming Mesh";
    }
}
