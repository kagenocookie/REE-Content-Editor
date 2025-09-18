using System.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class LineMesh : Mesh
{
    public LineMesh(GL gl, params Vector3[] points) : base(gl)
    {
        var attrs = AttributeCount;
        VertexData = new float[points.Length * attrs];
        Indices = new int[points.Length];
        BoundingBox = AABB.MaxMin;
        for (int index = 0; index < points.Length; ++index) {
            var point = points[index];
            VertexData[index * attrs + 0] = point.X;
            VertexData[index * attrs + 1] = point.Y;
            VertexData[index * attrs + 2] = point.Z;
            VertexData[index * attrs + 8] = (float)index;
            Indices[index] = index;
            BoundingBox = BoundingBox.Extend(point);
        }
        UpdateBuffers();
    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}