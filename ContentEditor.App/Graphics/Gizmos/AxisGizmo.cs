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
        var builder = context.GetMaterialBuilder(BuiltInMaterials.MonoColor).Blend();

        matGroup.Add(builder.Color("_MainColor", new Color(255, 0, 0, 150)).Create("x"));
        matGroup.Add(builder.Color("_MainColor", new Color(0, 255, 0, 150)).Create("y"));
        matGroup.Add(builder.Color("_MainColor", new Color(0, 0, 255, 150)).Create("z"));

        matGroup.Add(builder.Color("_MainColor", new Color(255, 0, 0, 48)).Create("xx"));
        matGroup.Add(builder.Color("_MainColor", new Color(0, 255, 0, 48)).Create("yy"));
        matGroup.Add(builder.Color("_MainColor", new Color(0, 0, 255, 48)).Create("zz"));

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