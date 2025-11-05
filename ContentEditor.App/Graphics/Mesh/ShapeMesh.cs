using System.Numerics;
using ReeLib.Rcol;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class ShapeMesh : Mesh
{
    public ShapeType ShapeType { get; private set; } = ShapeType.Invalid;

    private ShapeMesh() { }

    public ShapeMesh(GL gl) : base(gl)
    {
    }

    public void SetShape(AABB aabb)
    {
        var (v, i, b) = (VertexData, Indices, BoundingBox);
        ShapeBuilder.CreateSingle(aabb, ref v, ref i, ref b);
        (VertexData, Indices, BoundingBox) = (v, i, b);
        ShapeType = ShapeType.Aabb;
        // TODO verify - do we need to negate the GameObject's transform (are AabbShapes truly AABB or just AABB relative to the GameObject they're in?)
        UpdateBuffers();
    }

    public void SetShape(OBB box)
    {
        var (v, i, b) = (VertexData, Indices, BoundingBox);
        ShapeBuilder.CreateSingle(box, ref v, ref i, ref b);
        (VertexData, Indices, BoundingBox) = (v, i, b);
        ShapeType = ShapeType.Box;
        UpdateBuffers();
    }

    public void SetWireShape(Sphere sphere, ShapeType type)
    {
        var (v, i, b) = (VertexData, Indices, BoundingBox);
        ShapeBuilder.CreateSingle(sphere, ref v, ref i, ref b);
        (VertexData, Indices, BoundingBox) = (v, i, b);
        ShapeType = type;
        UpdateBuffers();
    }

    public void SetWireShape(Capsule capsule, ShapeType type)
    {
        if (capsule.p1 == capsule.p0) {
            SetWireShape(new Sphere() { pos = capsule.p0, r = capsule.R }, ShapeType.Sphere);
            ShapeType = type;
            return;
        }

        var (v, i, b) = (VertexData, Indices, BoundingBox);
        ShapeBuilder.CreateSingle(capsule, ref v, ref i, ref b);
        (VertexData, Indices, BoundingBox) = (v, i, b);
        ShapeType = type;
        UpdateBuffers();
    }

    public void SetWireShape(Cylinder cylinder)
    {
        var (v, i, b) = (VertexData, Indices, BoundingBox);
        ShapeBuilder.CreateSingle(cylinder, ref v, ref i, ref b);
        (VertexData, Indices, BoundingBox) = (v, i, b);
        ShapeType = ShapeType.Cylinder;
        UpdateBuffers();
    }

    public void Build(ShapeBuilder builder)
    {
        var (v, i, b) = (VertexData, Indices, BoundingBox);
        builder.UpdateMesh(ref v, ref i, ref b);
        (VertexData, Indices, BoundingBox) = (v, i, b);
        if (builder.GeoType == ShapeBuilder.GeometryType.Line) {
            MeshType = PrimitiveType.Lines;
        }
        UpdateBuffers();
    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}