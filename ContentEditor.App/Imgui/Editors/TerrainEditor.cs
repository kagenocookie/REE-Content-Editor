using System.Numerics;
using Assimp;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Bvh;
using ReeLib.Terr;

namespace ContentEditor.App.ImguiHandling;

public class TerrainEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "Terrain";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public TerrFile File => Handle.GetFile<TerrFile>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public TerrainEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    private string? lastFilepath;

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild<TerrFile, List<(string name1, string name2)>>("BVH Layers", File, getter: (f) => f!.bvh!.stringTable!).AddDefaultHandler();
            context.AddChild<TerrFile, List<TerrainType>>("Terrain Types", File, getter: (f) => f!.Types!).AddDefaultHandler();
            context.AddChild<TerrFile, List<Guid>>("GUIDs", File, getter: (f) => f!.Guids!).AddDefaultHandler();

            context.AddChild<TerrFile, List<BvhTriangle>>("Raw triangles", File, getter: (f) => f!.bvh!.triangles).AddDefaultHandler();
            context.AddChild<TerrFile, List<Vector3>>("Raw vertices", File, getter: (f) => f!.bvh!.vertices).AddDefaultHandler();
        }

        var window = EditorWindow.CurrentWindow!;
        if (ImGui.Button("Export to mesh ...")) {
            PlatformUtils.ShowSaveFileDialog((fn) => {
                lastFilepath = fn;
                window.InvokeFromUIThread(() => {
                    McolEditor.ExportMcolToGlb(File.bvh, fn);
                });
            }, lastFilepath ?? Handle.Filename.ToString(), MeshViewer.MeshFilesFilter);
        }
        ImGui.SameLine();
        if (ImGui.Button("Import mesh ...")) {
            PlatformUtils.ShowFileDialog((files) => {
                var fn = files[0];
                lastFilepath = fn;
                window.InvokeFromUIThread(() => {
                    ImportGlbIntoTerrain(File, fn);
                    context.ClearChildren();
                });
            }, lastFilepath ?? Handle.Filename.ToString(), MeshViewer.MeshFilesFilter);
        }

        ImGui.Spacing();
        context.children[0].ShowUI();
        ImGui.Spacing();
        ImGui.Spacing();
        for (int i = 1; i < context.children.Count; ++i) {
            context.children[i].ShowUI();
        }
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }

    public static bool ImportGlbIntoTerrain(TerrFile file, string meshFilepath)
    {
        using var ctx = new AssimpContext();
        var scene = ctx.ImportFile(meshFilepath);
        file.bvh ??= new(file.FileHandler);
        return McolEditor.LoadTrianglesFromScene(file.bvh, scene, PathUtils.ParseFileFormat(file.FileHandler.FilePath));
    }
}
