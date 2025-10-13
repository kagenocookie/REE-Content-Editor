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

    public override void Init(RenderContext context)
    {
        (mesh, shape) = context.CreateShapeMesh();
        Meshes.Add(mesh);
        shape.MeshType = PrimitiveType.Lines;

        // note: we don't need to have a reference stored on the rendercontext for this material
        // because there's no textures in it, nor does it have a link to any MDF2 files
        var mat = context.GetBuiltInMaterial(BuiltInMaterials.MonoColor);
        mat.name = "white";
        mat.SetParameter("_MainColor", new Color(255, 255, 255, 255));
        mesh.SetMaterials(new MaterialGroup(mat), [0]);
    }

    public override void Render(RenderContext context)
    {
        if (!shouldRenderNext) return;

        context.RenderSimple(Meshes[0], Matrix4X4<float>.Identity);

        var mat = Meshes[0].GetMaterial(0);
        mat.SetParameter("_MainColor", new Color(136, 136, 136, 255));
        GL.DepthFunc(DepthFunction.Greater);
        mat.Bind(); // force a material re-bind
        context.RenderSimple(Meshes[0], Matrix4X4<float>.Identity);
        GL.DepthFunc(DepthFunction.Less);
        mat.SetParameter("_MainColor", new Color(255, 255, 255, 255));
    }

    public override void Update(RenderContext context, float deltaTime)
    {
        shouldRenderNext = false;
        var wnd = EditorWindow.CurrentWindow;
        if (wnd == null) return;

        var showBounds = false;
        var builder = new ShapeBuilder() { GeoType = ShapeBuilder.GeometryType.Line };
        foreach (var ww in wnd.ActiveImguiWindows) {
            if (ww.Handler is ObjectInspector insp && insp.ParentWindow is SceneEditor scnEdit) {
                var scene = scnEdit.GetScene();
                if (scene == null) continue;

                if (scnEdit.PrimaryTarget != insp.Target) continue;

                AABB targetBounds;
                if (scnEdit.PrimaryTarget is GameObject go) {
                    targetBounds = go.GetWorldSpaceBounds();
                    showBounds = showBounds || !targetBounds.IsEmpty;
                } else if (scnEdit.PrimaryTarget is Folder folder) {
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