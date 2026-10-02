using System.Text;
using ContentEditor.Core;

namespace ContentEditor.App;

public static partial class Lang
{
    public static class MeshViewer
    {
        public static readonly FixedString Menu_Editor = "Editor";
        public static readonly IconString Menu_Camera = new("{0} Camera", AppIcons.SI_GenericCamera);
        public static readonly IconString Menu_Rendering = new("{0} Rendering: ", AppIcons.SI_SceneRender);
        public static readonly IconString Menu_Material = new("{0} Material", AppIcons.SI_FileType_MDF);
        public static readonly IconString Menu_RCOL = new("{0} RCOL", AppIcons.SI_FileType_RCOL);
        public static readonly IconString Menu_Chain = new("{0} Chain", AppIcons.SI_MeshViewerChain);
        public static readonly IconString Menu_IO = new("{0} Import / Export", AppIcons.SI_GenericIO);
        public static readonly IconString Tab_OutlinerModels = new("{0} Models", AppIcons.SI_FileType_MESH);
        public static readonly IconString Tab_OutlinerAnimations = new("{0} Animations", AppIcons.SI_Animation);
        public static readonly FixedString Tooltip_OutlinerExpand = "Expand Outliner";
        public static readonly FixedString Tooltip_OutlinerCollapse = "Collapse Outliner";
        public static readonly FixedString Tooltip_OutlinerGroupExpand = "Expand Groups";
        public static readonly FixedString Tooltip_OutlinerGroupCollapse = "Collapse Groups";
        public static readonly FixedString Tooltip_OutlinerHoverHighlight = "Toggle Hover Highlighting";
        public static readonly FixedString Tooltip_OutlinerMeshInfo = "Mesh Info";
        public static readonly FixedString Tooltip_CollectionSave = "Save Collection";
        public static readonly FixedString Tooltip_CollectionLoad = "Load Collection";
        public static readonly FixedString Tooltip_CollectionClear = "Remove all additional meshes";
        public static readonly IconString Button_OutlinerMeshCollection = new("{0} Mesh Collection", AppIcons.SI_SceneGameObject4);
        public static readonly FixedString Separator_Objects = "Objects";
        public static readonly FixedString Separator_Render = "Render Modes";
        public static readonly FixedString Separator_TextureDisplay = "Texture Display";
        public static readonly FixedString Separator_AddMesh = "Add Mesh";
        public static readonly IconString MenuItem_MeshCollectionRemove = new("{0} Remove", AppIcons.SI_GenericDelete);
        public static readonly IconString MenuItem_MeshCollectionInfo = new("{0} Info", AppIcons.SI_GenericInfo);
        public static readonly IconString MenuItem_MeshCollectionTransform = new("{0} Transform", AppIcons.SI_Generic3Axis);
        public static readonly IconString MenuItem_MeshCollectionSkeleton = new("{0} Skeleton", AppIcons.SI_FileType_FBXSKEL);
        public static readonly IconString MenuItem_MeshCollectionOpenNewWin = new("{0} Open In Standalone Window", AppIcons.SI_WindowOpenNew);
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
        public static readonly FixedString DisplayTex_HighRes = "Hi-Res";
        public static readonly FixedString DisplayTex_LowRes = "Low-Res";
        public static readonly FixedString Warning_CantOpenMeshFromCollections = "External mesh files can't be opened directly under mesh collections.\nOpen it separately and convert to .mesh first.";
        public static readonly FixedString Warning_CantResolveMeshPath = """
            Can't resolve .mesh file from the given path.
            Re-check if you have the right file path and if the mesh is actually valid.
            """;
        public static readonly FixedString Warning_CantResolveStreamingMeshPath = """
            Can't resolve .mesh file from the given path.
            Re-check if you have the right file path and if the mesh is actually valid.
            If it's a mesh with a streaming file, ensure both are extracted to a matching file path.
            """;
        public static readonly FixedString Error_MaterialCountMismatch = """
            Mesh material count does not match MDF2 material count. Textures won't display correctly ingame.
            Ensure that both counts match.
            """;

        public static readonly FixedString Error_MaterialNotFound = """
            Mesh references material names that are not present in the selected MDF2.
            Such submeshes will be invisible ingame.
            """;
        public static readonly FixedString NoMeshLoaded = "No mesh loaded";
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
