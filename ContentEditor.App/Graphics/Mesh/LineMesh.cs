using System.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class LineMesh : Mesh
{
    public LineMesh(GL gl, params Vector3[] points) : base(gl)
    {
        SetAttributesNoTangents();
        VertexData = new float[points.Length * attributeNumberCount];
        Indices = new int[points.Length];
        BoundingBox = AABB.MaxMin;
        for (int index = 0; index < points.Length; ++index) {
            var point = points[index];
            VertexData[index * attributeNumberCount + 0] = point.X;
            VertexData[index * attributeNumberCount + 1] = point.Y;
            VertexData[index * attributeNumberCount + 2] = point.Z;
            VertexData[index * attributeNumberCount + 8] = (float)index;
            Indices[index] = index;
            BoundingBox = BoundingBox.Extend(point);
        }
        UpdateBuffers();
    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}