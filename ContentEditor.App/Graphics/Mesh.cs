using System.Numerics;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;
#pragma warning disable CS8618

public abstract class Mesh : IDisposable
{
    protected readonly GL GL;

    protected VertAttribute[] attributes = DefaultAttributes;

    protected record struct VertAttribute(int Offset, int Count, uint Index);

    public float[] VertexData { get; protected set; }
    public int[] Indices { get; protected set; }
    public VertexArrayObject<float, int> VAO { get; protected set; }
    public BufferObject<float> VBO { get; protected set; }
    public BufferObject<int> EBO { get; protected set; }
    public AABB BoundingBox { get; protected set; }
    public int MeshGroup { get; set; }

    private MeshFlags _flags;
    public MeshFlags Flags {
        get => _flags;
        protected set {
            if (_flags == value) return;
            _flags = value;
            UpdateAttributes();
        }
    }

    public bool HasTangents => (Flags & MeshFlags.HasTangents) != 0;
    public bool HasBones => (Flags & MeshFlags.HasBones) != 0;

    private const int MinAttributeCount = 9;
    protected int AttributeCount => MinAttributeCount
        + (HasTangents ? 3 : 0)
        + (HasBones ? 8 : 0);

    protected const int TangentAttributeOffset = 9;
    protected int BonesAttributeOffset => HasTangents ? 12 : 9;

    public PrimitiveType MeshType { get; init; } = PrimitiveType.Triangles;

    private const int Index_Position = 0;
    private const int Index_UV = 1;
    private const int Index_Normal = 2;
    private const int Index_Index = 3;
    private const int Index_Tangent = 4;
    private const int Index_BoneIndex = 5;
    private const int Index_BoneWeight = 6;

    private static readonly VertAttribute[] DefaultAttributes = [
        new VertAttribute(0, 3, Index_Position),
        new VertAttribute(3, 2, Index_UV),
        new VertAttribute(5, 3, Index_Normal),
        new VertAttribute(8, 1, Index_Index),
    ];

    private static readonly VertAttribute[] AttributesBones = DefaultAttributes.Concat([
        new VertAttribute(9, 4, Index_BoneIndex),
        new VertAttribute(13, 4, Index_BoneWeight),
    ]).ToArray();

    private static readonly VertAttribute[] AttributesTangents = DefaultAttributes.Concat([
        new VertAttribute(9, 3, Index_Tangent),
    ]).ToArray();

    private static readonly VertAttribute[] AttributesTangentsBones = DefaultAttributes.Concat([
        new VertAttribute(9, 3, Index_Tangent),
        new VertAttribute(12, 4, Index_BoneIndex),
        new VertAttribute(16, 4, Index_BoneWeight),
    ]).ToArray();

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

    private void UpdateAttributes()
    {
        if (Flags == (MeshFlags.HasBones|MeshFlags.HasTangents)) {
            attributes = AttributesTangentsBones;
        } else if (Flags == MeshFlags.HasBones) {
            attributes = AttributesBones;
        } else if (Flags == MeshFlags.HasTangents) {
            attributes = AttributesTangents;
        } else {
            attributes = DefaultAttributes;
        }
    }

    protected void ApplyVertexAttributes()
    {
        var count = (uint)AttributeCount;
        foreach (var va in attributes) {
            if (va.Index == Index_BoneIndex || va.Index == Index_Index) {
                VAO.VertexAttributePointerInt(va.Index, va.Count, VertexAttribIType.Int, count, va.Offset);
            } else {
                VAO.VertexAttributePointerFloat(va.Index, va.Count, VertexAttribPointerType.Float, count, va.Offset);
            }
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