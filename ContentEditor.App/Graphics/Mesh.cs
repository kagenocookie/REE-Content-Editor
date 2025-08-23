using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

#pragma warning disable CS8618

public class Mesh : IDisposable
{
    public Mesh(GL gl, float[] vertexData, uint[] indices, List<Texture> textures)
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
            attrTotal = 11;
            attributes = [
                new VertAttribute(3, 0),
                new VertAttribute(2, 3),
                new VertAttribute(3, 5),
                new VertAttribute(3, 8),
            ];
        } else {
            attrTotal = 8;
            attributes = [
                new VertAttribute(3, 0),
                new VertAttribute(2, 3),
                new VertAttribute(3, 5),
            ];
        }
        VertexData = new float[sourceMesh.VertexCount * attrTotal];
        for (int i = 0; i < sourceMesh.Vertices.Count; ++i) {
            VertexData[i * attrTotal + 0] = sourceMesh.Vertices[i].X;
            VertexData[i * attrTotal + 1] = sourceMesh.Vertices[i].Y;
            VertexData[i * attrTotal + 2] = sourceMesh.Vertices[i].Z;
            VertexData[i * attrTotal + 3] = uv0[i].X;
            VertexData[i * attrTotal + 4] = uv0[i].Y;
            VertexData[i * attrTotal + 5] = sourceMesh.Normals[i].X;
            VertexData[i * attrTotal + 6] = sourceMesh.Normals[i].Y;
            VertexData[i * attrTotal + 7] = sourceMesh.Normals[i].Z;
            if (hasTangents) {
                VertexData[i * attrTotal + 8] = sourceMesh.Tangents[i].X;
                VertexData[i * attrTotal + 9] = sourceMesh.Tangents[i].Y;
                VertexData[i * attrTotal + 10] = sourceMesh.Tangents[i].Z;
            }
        }

        Indices = sourceMesh.GetUnsignedIndices().ToArray();
        BoundingBox = new AABB(sourceMesh.BoundingBox.Min, sourceMesh.BoundingBox.Max);
        SetupMesh();
    }

    public float[] VertexData { get; private set; }
    public uint[] Indices { get; private set; }
    public VertexArrayObject<float, uint> VAO { get; set; }
    public BufferObject<float> VBO { get; set; }
    public BufferObject<uint> EBO { get; set; }
    public AABB BoundingBox { get; set; }
    public GL GL { get; }

    public unsafe void SetupMesh()
    {
        VBO = new BufferObject<float>(GL, VertexData, BufferTargetARB.ArrayBuffer);
        EBO = new BufferObject<uint>(GL, Indices, BufferTargetARB.ElementArrayBuffer);
        VAO = new VertexArrayObject<float, uint>(GL, VBO, EBO);
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