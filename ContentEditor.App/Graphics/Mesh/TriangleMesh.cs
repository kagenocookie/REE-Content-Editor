using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class TriangleMesh : Mesh
{
    public TriangleMesh(GL gl, float[] vertexData, int[] indices) : base(gl)
    {
        VertexData = vertexData;
        Indices = indices;
        UpdateBuffers();
    }

    public TriangleMesh(GL gl, Assimp.Mesh sourceMesh) : base(gl)
    {
        var uv0 = sourceMesh.TextureCoordinateChannels[0];
        var hasTangents = sourceMesh.Tangents.Count > 0;
        if (hasTangents) {
            SetAttributesWithTangents();
        } else {
            SetAttributesNoTangents();
        }

        Indices = sourceMesh.GetIndices().ToArray();

        VertexData = new float[Indices.Length * attributeNumberCount];
        for (int index = 0; index < Indices.Length; ++index) {
            var vert = (int)Indices[index];
            VertexData[index * attributeNumberCount + 0] = sourceMesh.Vertices[vert].X;
            VertexData[index * attributeNumberCount + 1] = sourceMesh.Vertices[vert].Y;
            VertexData[index * attributeNumberCount + 2] = sourceMesh.Vertices[vert].Z;
            VertexData[index * attributeNumberCount + 3] = uv0[vert].X;
            VertexData[index * attributeNumberCount + 4] = uv0[vert].Y;
            VertexData[index * attributeNumberCount + 5] = sourceMesh.Normals[vert].X;
            VertexData[index * attributeNumberCount + 6] = sourceMesh.Normals[vert].Y;
            VertexData[index * attributeNumberCount + 7] = sourceMesh.Normals[vert].Z;
            VertexData[index * attributeNumberCount + 8] = (float)index;
            if (hasTangents) {
                VertexData[index * attributeNumberCount + 9] = sourceMesh.Tangents[vert].X;
                VertexData[index * attributeNumberCount + 10] = sourceMesh.Tangents[vert].Y;
                VertexData[index * attributeNumberCount + 11] = sourceMesh.Tangents[vert].Z;
            }
        }

        BoundingBox = new AABB(sourceMesh.BoundingBox.Min, sourceMesh.BoundingBox.Max);
        UpdateBuffers();
    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}