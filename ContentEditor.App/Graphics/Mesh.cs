using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

#pragma warning disable CS8618

public class Mesh : IDisposable
{
    public Mesh(GL gl, float[] vertexData, int[] indices, List<Texture> textures)
    {
        GL = gl;
        VertexData = vertexData;
        Indices = indices;
        SetupMesh();
    }

    private uint attrTotal;
    private VertAttribute[] attributes;

    private record struct VertAttribute(int Count, int Offset);

    public Mesh(GL gl, Assimp.Mesh sourceMesh)
    {
        GL = gl;
        var uv0 = sourceMesh.TextureCoordinateChannels[0];
        var hasTangents = sourceMesh.Tangents.Count > 0;
        if (hasTangents) {
            attrTotal = 12;
            attributes = [
                new VertAttribute(3, 0), // position
                new VertAttribute(2, 3), // uv
                new VertAttribute(3, 5), // normal
                new VertAttribute(1, 8), // index
                new VertAttribute(3, 9), // tangent
            ];
        } else {
            attrTotal = 9;
            attributes = [
                new VertAttribute(3, 0), // position
                new VertAttribute(2, 3), // uv
                new VertAttribute(3, 5), // normal
                new VertAttribute(1, 8), // index
            ];
        }

        Indices = sourceMesh.GetIndices().ToArray();

        VertexData = new float[Indices.Length * attrTotal];
        for (int index = 0; index < Indices.Length; ++index) {
            var vert = (int)Indices[index];
            VertexData[index * attrTotal + 0] = sourceMesh.Vertices[vert].X;
            VertexData[index * attrTotal + 1] = sourceMesh.Vertices[vert].Y;
            VertexData[index * attrTotal + 2] = sourceMesh.Vertices[vert].Z;
            VertexData[index * attrTotal + 3] = uv0[vert].X;
            VertexData[index * attrTotal + 4] = uv0[vert].Y;
            VertexData[index * attrTotal + 5] = sourceMesh.Normals[vert].X;
            VertexData[index * attrTotal + 6] = sourceMesh.Normals[vert].Y;
            VertexData[index * attrTotal + 7] = sourceMesh.Normals[vert].Z;
            VertexData[index * attrTotal + 8] = (float)index;
            if (hasTangents) {
                VertexData[index * attrTotal + 9] = sourceMesh.Tangents[vert].X;
                VertexData[index * attrTotal + 10] = sourceMesh.Tangents[vert].Y;
                VertexData[index * attrTotal + 11] = sourceMesh.Tangents[vert].Z;
            }
        }

        BoundingBox = new AABB(sourceMesh.BoundingBox.Min, sourceMesh.BoundingBox.Max);
        SetupMesh();
    }

    public float[] VertexData { get; private set; }
    public int[] Indices { get; private set; }
    public VertexArrayObject<float, int> VAO { get; set; }
    public BufferObject<float> VBO { get; set; }
    public BufferObject<int> EBO { get; set; }
    public AABB BoundingBox { get; set; }
    public GL GL { get; }

    public unsafe void SetupMesh()
    {
        VBO = new BufferObject<float>(GL, VertexData, BufferTargetARB.ArrayBuffer);
        EBO = new BufferObject<int>(GL, Indices, BufferTargetARB.ElementArrayBuffer);
        VAO = new VertexArrayObject<float, int>(GL, VBO, EBO);
        ApplyVertexAttributes();
    }

    public void Bind()
    {
        VAO.Bind();
        VBO.Bind();
        EBO.Bind();
        ApplyVertexAttributes();
    }

    private void ApplyVertexAttributes()
    {
        for (uint i = 0; i < attributes.Length; ++i) {
            var va = attributes[i];
            VAO.VertexAttributePointer(i, va.Count, VertexAttribPointerType.Float, attrTotal, va.Offset);
        }
    }

    public void Dispose()
    {
        VAO.Dispose();
        VBO.Dispose();
        EBO.Dispose();
    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}