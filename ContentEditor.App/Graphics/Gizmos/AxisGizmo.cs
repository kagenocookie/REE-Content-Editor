using System.Numerics;
using ReeLib.via;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class AxisGizmo : Gizmo
{
    public AxisGizmo(GL gl) : base(gl)
    {
    }

    public override void Init(OpenGLRenderContext context)
    {
        var mesh = context.CreateBlankMesh();
        mesh.Handle.Meshes.Add(new LineMesh(GL, new Vector3(-100000, 0, 0), new Vector3(100000, 0, 0)) { MeshType = PrimitiveType.Lines });
        mesh.Handle.Meshes.Add(new LineMesh(GL, new Vector3(0, -100000, 0), new Vector3(0, 100000, 0)) { MeshType = PrimitiveType.Lines });
        mesh.Handle.Meshes.Add(new LineMesh(GL, new Vector3(0, 0, -100000), new Vector3(0, 0, 100000)) { MeshType = PrimitiveType.Lines });

        var matGroup = new MaterialGroup();
        var mat = context.GetBuiltInMaterial(BuiltInMaterials.MonoColor);
        mat.name = "x";
        mat.SetParameter("_MainColor", new Color(255, 0, 0, 150));
        mat.BlendMode = new MaterialBlendMode(true, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        matGroup.Add(mat);

        mat = context.GetBuiltInMaterial(BuiltInMaterials.MonoColor);
        mat.name = "y";
        mat.SetParameter("_MainColor", new Color(0, 255, 0, 150));
        mat.BlendMode = new MaterialBlendMode(true, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        matGroup.Add(mat);

        mat = context.GetBuiltInMaterial(BuiltInMaterials.MonoColor);
        mat.name = "z";
        mat.SetParameter("_MainColor", new Color(0, 0, 255, 150));
        mat.BlendMode = new MaterialBlendMode(true, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        matGroup.Add(mat);

        mat = matGroup.Materials[0].Clone("xx");
        mat.SetParameter("_MainColor", new Color(255, 0, 0, 48));
        matGroup.Add(mat);

        mat = matGroup.Materials[1].Clone("yy");
        mat.SetParameter("_MainColor", new Color(0, 255, 0, 48));
        matGroup.Add(mat);

        mat = matGroup.Materials[2].Clone("zz");
        mat.SetParameter("_MainColor", new Color(0, 0, 255, 48));
        matGroup.Add(mat);

        mesh.SetMaterials(matGroup, [0, 1, 2]);
        Meshes.Add(mesh);
    }

    public override void Render(OpenGLRenderContext context)
    {
        for (int i = 0; i < 3; i++) {
            context.Batch.Gizmo.Add(new GizmoRenderBatchItem(Meshes[0].GetMaterial(i), Meshes[0].GetMesh(i), Matrix4X4<float>.Identity, Meshes[0].GetMaterial(i + 3)));
        }
    }
}