using System.Numerics;
using ReeLib.Rcol;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class ShapeMesh : Mesh
{
    public ShapeType ShapeType { get; private set; } = ShapeType.Invalid;

    public ShapeMesh() { }

    public ShapeMesh(ShapeBuilder builder)
    {
        Build(builder);
    }

    public ShapeMesh(GL gl) : base(gl)
    {
    }

    public void Build(ShapeBuilder builder)
    {
        var (v, i, b) = (VertexData, Indices, BoundingBox);
        builder.UpdateMesh(ref v, ref i, ref b);
        (VertexData, Indices, BoundingBox) = (v, i, b);
        if (builder.GeoType == ShapeBuilder.GeometryType.Line) {
            MeshType = PrimitiveType.Lines;
        }
        layout = builder.layout;
        if (GL != null) UpdateBuffers();
    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}