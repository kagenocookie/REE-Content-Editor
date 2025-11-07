using System.Numerics;
using System.Runtime.CompilerServices;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;
#pragma warning disable CS8618

public abstract class Mesh : IDisposable
{
    protected GL GL;

    public MeshLayout layout { get; protected set; }

    public float[] VertexData { get; protected set; }
    public int[] Indices { get; protected set; }
    public VertexArrayObject<float, int> VAO { get; protected set; }
    public BufferObject<float> VBO { get; protected set; }
    public BufferObject<int> EBO { get; protected set; }
    public AABB BoundingBox { get; protected set; }
    public int MeshGroup { get; set; }

    public uint ID => VAO.Handle;

    public bool HasTangents => layout.HasAttributes(MeshAttributeFlag.Tangent);
    public bool HasBones => layout.HasAttributes(MeshAttributeFlag.Weight);
    public bool HasColor => layout.HasAttributes(MeshAttributeFlag.Color);

    public PrimitiveType MeshType { get; set; } = PrimitiveType.Triangles;

    protected Mesh()
    {
    }

    protected Mesh(GL gl)
    {
        GL = gl;
        CreateBuffers();
    }

    internal void Initialize(GL gl)
    {
        GL = gl;
        CreateBuffers();
        UpdateBuffers();
    }

    public virtual void Bind()
    {
        VAO.Bind();
    }

    protected void ApplyVertexAttributes()
    {
        var size = 0;
        foreach (var va in layout.Attributes) {
            size += va.Size;
            if (va.Index == MeshLayout.Index_BoneIndex || va.Index == MeshLayout.Index_Index) {
                VAO.VertexAttributePointerInt(va.Index, va.Size, VertexAttribIType.Int, va.Offset);
            } else if (va.Index == MeshLayout.Index_Color) {
                VAO.VertexAttributePointerFloat(va.Index, 4, VertexAttribType.UnsignedByte, va.Offset, true);
            } else {
                VAO.VertexAttributePointerFloat(va.Index, va.Size, VertexAttribType.Float, va.Offset);
            }
        }
        VAO.BindVertexBuffer(VBO.Handle, (uint)size);

        // this is only needed for instanced meshes, not for normally drawn ones.
        // But it doesn't seem to break normal meshes either so it's "fine"
        VAO.EnableInstancedMatrix(MeshLayout.Index_InstancesMatrix);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateInstancedMatrixBuffer(uint vboHandle, uint byteOffset)
    {
        VAO.UpdateInstancedMatrixBuffer(vboHandle, byteOffset);
    }

    protected void CreateBuffers()
    {
        VBO = new BufferObject<float>(GL, BufferTargetARB.ArrayBuffer);
        EBO = new BufferObject<int>(GL, BufferTargetARB.ElementArrayBuffer);
        VAO = new VertexArrayObject<float, int>(GL);
    }

    protected void UpdateBuffers()
    {
        VBO.UpdateBuffer(VertexData);
        EBO.UpdateBuffer(Indices);
        VAO.Bind();
        EBO.Bind();
        ApplyVertexAttributes();
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

    public virtual Mesh Clone()
    {
        var newInstance = (Mesh)Activator.CreateInstance(GetType())!;
        CopyGeometryData(newInstance);
        return newInstance;
    }

    protected void CopyGeometryDataReuseArrays(Mesh target)
    {
        target.GL = GL;
        target.VertexData = VertexData;
        target.Indices = Indices;
        target.BoundingBox = BoundingBox;
        target.MeshGroup = MeshGroup;
        target.layout = layout;
        target.MeshType = MeshType;
    }

    protected void CopyGeometryData(Mesh target)
    {
        target.GL = GL;
        target.VertexData = new float[VertexData.Length];
        Array.Copy(VertexData, target.VertexData, VertexData.Length);
        target.Indices = new int[Indices.Length];
        Array.Copy(Indices, target.Indices, Indices.Length);
        target.BoundingBox = BoundingBox;
        target.MeshGroup = MeshGroup;
        target.layout = layout;
        target.MeshType = MeshType;
    }
}