using System.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReeLib.Rcol;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

// TODO

public class ShapeMesh : Mesh
{
    public ShapeType ShapeType { get; private set; } = ShapeType.Invalid;

    public ShapeMesh(GL gl) : base(gl) { }

    private static readonly Vector3[] BoxTrianglePoints = [
        new(-1, -1, -1),    new(1, -1, -1),     new(1, -1, 1),
        new(-1, -1, -1),    new(1, -1, 1),      new(-1, -1, 1),
        new(-1, 1, -1),     new(-1, 1, 1),      new(1, 1, 1),
        new(-1, 1, -1),     new(1, 1, 1),       new(1, 1, -1),
        new(-1, 1, -1),     new(-1, -1, -1),    new(-1, -1, 1),
        new(-1, 1, -1),     new(-1, -1, 1),     new(-1, 1, 1),
        new(1, 1, 1),       new(1, -1, 1),      new(1, -1, -1),
        new(1, 1, 1),       new(1, -1, -1),     new(1, 1, -1),
        new(-1, 1, 1),      new(-1, -1, 1),     new(1, -1, 1),
        new(-1, 1, 1),      new(1, -1, 1),      new(1, 1, 1),
        new(-1, 1, -1),     new(1, 1, -1),      new(1, -1, -1),
        new(-1, 1, -1),     new(1, -1, -1),     new(-1, -1, -1)
    ];

    public void SetShape(AABB aabb)
    {
        if (ShapeType != ShapeType.Box && ShapeType != ShapeType.Aabb) {
            SetAttributesNoTangents();
            Indices = new int[36];
            VertexData = new float[36 * attributeNumberCount];
        }
        BoundingBox = aabb;
        Span<Vector3> verts = stackalloc Vector3[8];
        var min = aabb.minpos;
        var max = aabb.maxpos;
        verts[0] = new Vector3(min.X, min.Y, min.Z);
        verts[1] = new Vector3(max.X, min.Y, min.Z);
        verts[2] = new Vector3(min.X, max.Y, min.Z);
        verts[3] = new Vector3(max.X, max.Y, min.Z);
        verts[4] = new Vector3(min.X, min.Y, max.Z);
        verts[5] = new Vector3(max.X, min.Y, max.Z);
        verts[6] = new Vector3(min.X, max.Y, max.Z);
        verts[7] = new Vector3(max.X, max.Y, max.Z);
    }

    public unsafe void SetShape(OBB box)
    {
        if (ShapeType != ShapeType.Box && ShapeType != ShapeType.Aabb) {
            SetAttributesNoTangents();
            Indices = new int[36];
            VertexData = new float[36 * attributeNumberCount];
        }
        Span<Vector3> verts = stackalloc Vector3[8];

    }

    public void SetShape(Sphere aabb)
    {
        if (ShapeType != ShapeType.Sphere && ShapeType != ShapeType.ContinuousSphere) {
            // VertexData = new float[];
        }
    }

    public void SetShape(Capsule aabb)
    {

    }

    public void SetShape(Cylinder aabb)
    {

    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}