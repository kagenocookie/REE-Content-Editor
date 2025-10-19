using System.Numerics;
using ReeLib.via;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class GridGizmo : Gizmo
{
    private const float GridCellSpacing = 5f;

    private Matrix4X4<float> position;

    public GridGizmo(GL gl) : base(gl)
    {
    }

    public override void Init(OpenGLRenderContext context)
    {
        var mesh = context.CreateBlankMesh();

        const int lineCount = 100;
        const float gridSpan = lineCount * GridCellSpacing;

        var matGroup = new MaterialGroup();
        var mat = context.GetMaterialBuilder(BuiltInMaterials.MonoColor, "gray")
            .Color("_MainColor", new Color(100, 100, 100, 100))
            .Float("_FadeMaxDistance", gridSpan)
            .Blend();
        matGroup.Add(mat);

        var lines = new List<Vector3>((lineCount + 1) * 2 * 2);
        for (int x = -lineCount; x < lineCount; x++) {
            lines.Add(new Vector3(x * GridCellSpacing, 0, -gridSpan));
            lines.Add(new Vector3(x * GridCellSpacing, 0, gridSpan));
        }
        for (int z = -lineCount; z < lineCount; z++) {
            lines.Add(new Vector3(-gridSpan, 0, z * GridCellSpacing));
            lines.Add(new Vector3(gridSpan, 0, z * GridCellSpacing));
        }
        mesh.Handle.Meshes.Add(new LineMesh(GL, lines.ToArray()) { MeshType = PrimitiveType.Lines });
        mesh.SetMaterials(matGroup, [0]);
        Meshes.Add(mesh);
    }

    public override void Update(OpenGLRenderContext context, float deltaTime)
    {
        Vector3D<float> campos;
        if (Matrix4X4.Invert(context.ViewMatrix, out var inverted)) {
            campos = inverted.Row4.ToSystem().ToSilkNetVec3() with { Y = 0 };
        } else {
            campos = new();
        }
        campos.X = MathF.Round(campos.X / GridCellSpacing) * GridCellSpacing;
        campos.Z = MathF.Round(campos.Z / GridCellSpacing) * GridCellSpacing;
        position = Matrix4X4.CreateTranslation<float>(campos);
    }

    public override void Render(OpenGLRenderContext context)
    {
        context.RenderSimple(Meshes[0], position);
    }
}