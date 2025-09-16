using System.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

#pragma warning disable CS8618

public abstract class Mesh : IDisposable
{
    protected readonly GL GL;

    protected uint attributeNumberCount;
    protected VertAttribute[] attributes;

    protected record struct VertAttribute(int Count, int Offset);

    public float[] VertexData { get; protected set; }
    public int[] Indices { get; protected set; }
    public VertexArrayObject<float, int> VAO { get; protected set; }
    public BufferObject<float> VBO { get; protected set; }
    public BufferObject<int> EBO { get; protected set; }
    public AABB BoundingBox { get; protected set; }
    public int MeshGroup { get; set; }

    public PrimitiveType MeshType { get; init; } = PrimitiveType.Triangles;

    protected Mesh(GL gl)
    {
        GL = gl;
        CreateBuffers();
    }

    public virtual void Bind()
    {
        VAO.Bind();
        VBO.Bind();
        EBO.Bind();
        ApplyVertexAttributes();
    }

    protected void SetAttributesNoTangents()
    {
        if (attributeNumberCount == 9) return;
        attributeNumberCount = 9;
        attributes = [
            new VertAttribute(3, 0), // position
            new VertAttribute(2, 3), // uv
            new VertAttribute(3, 5), // normal
            new VertAttribute(1, 8), // index
        ];
    }

    protected void SetAttributesWithTangents()
    {
        if (attributeNumberCount == 12) return;
        attributeNumberCount = 12;
        attributes = [
            new VertAttribute(3, 0), // position
            new VertAttribute(2, 3), // uv
            new VertAttribute(3, 5), // normal
            new VertAttribute(1, 8), // index
            new VertAttribute(3, 9), // tangent
        ];
    }

    protected void ApplyVertexAttributes()
    {
        for (uint i = 0; i < attributes.Length; ++i) {
            var va = attributes[i];
            VAO.VertexAttributePointer(i, va.Count, VertexAttribPointerType.Float, attributeNumberCount, va.Offset);
        }
    }

    protected unsafe void CreateBuffers()
    {
        VBO = new BufferObject<float>(GL, BufferTargetARB.ArrayBuffer);
        EBO = new BufferObject<int>(GL, BufferTargetARB.ElementArrayBuffer);
        VAO = new VertexArrayObject<float, int>(GL);
    }

    protected unsafe void UpdateBuffers()
    {
        VBO.UpdateBuffer(VertexData);
        EBO.UpdateBuffer(Indices);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        VAO.Dispose();
        VBO.Dispose();
        EBO.Dispose();
    }

    protected void AssignBuffersFromVertexList(ReadOnlySpan<Vector3> vertices)
    {
        if (Indices.Length != vertices.Length) {
            Indices = new int[vertices.Length];
        }
        for (int i = 0; i < Indices.Length; ++i) {
            Indices[i] = i;
        }
    }
}