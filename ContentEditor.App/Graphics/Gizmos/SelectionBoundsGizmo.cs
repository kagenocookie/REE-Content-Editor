using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ReeLib.via;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class SelectionBoundsGizmo : Gizmo
{
    private ShapeMesh shape = null!;
    private MeshHandle mesh = null!;

    private bool shouldRenderNext;

    public SelectionBoundsGizmo(GL gl) : base(gl)
    {
    }

    public override void Init(OpenGLRenderContext context)
    {
        (mesh, shape) = context.CreateShapeMesh();
        Meshes.Add(mesh);
        shape.MeshType = PrimitiveType.Lines;

        // note: we don't need to have a reference stored on the rendercontext for this material
        // because there's no textures in it, nor does it have a link to any MDF2 files
        var (mat, mat2) = context.GetMaterialBuilder(BuiltInMaterials.MonoColor).Create2("white", "back");
        mat.SetParameter("_MainColor", new Color(255, 255, 255, 255));
        mat2.SetParameter("_MainColor", new Color(136, 136, 136, 255));
        mesh.SetMaterials(new MaterialGroup(mat, mat2), [0]);
    }

    public override void Render(OpenGLRenderContext context)
    {
        if (!shouldRenderNext) return;

        context.Batch.Gizmo.Add(new GizmoRenderBatchItem(Meshes[0].GetMaterial(0), Meshes[0].GetMesh(0), Matrix4X4<float>.Identity, Meshes[0].GetMaterial(1)));
    }

    public override void Update(OpenGLRenderContext context, float deltaTime)
    {
        shouldRenderNext = false;
        var wnd = EditorWindow.CurrentWindow;
        if (wnd == null) return;

        var showBounds = false;
        var builder = new ShapeBuilder() { GeoType = ShapeBuilder.GeometryType.Line };
        foreach (var ww in wnd.ActiveImguiWindows) {
            if (ww.Handler is ObjectInspector insp && insp.ParentWindow is ISceneEditor editor) {
                var scene = editor.GetScene();
                if (scene == null) continue;

                var primaryTarget = (editor as SceneEditor)?.PrimaryTarget ?? (editor as PrefabEditor)?.PrimaryTarget;

                if (primaryTarget != insp.Target) continue;

                AABB targetBounds;
                if (primaryTarget is GameObject go) {
                    targetBounds = go.GetWorldSpaceBounds();
                    showBounds = showBounds || !targetBounds.IsEmpty;
                } else if (primaryTarget is Folder folder) {
                    targetBounds = folder.GetWorldSpaceBounds();
                    showBounds = showBounds || !targetBounds.IsEmpty;
                } else {
                    continue;
                }

                if (showBounds && !targetBounds.IsEmpty) {
                    builder.Add(targetBounds);
                }
            }
        }

        if (showBounds) {
            shape.Build(builder);
            shouldRenderNext = true;
        }
    }
}