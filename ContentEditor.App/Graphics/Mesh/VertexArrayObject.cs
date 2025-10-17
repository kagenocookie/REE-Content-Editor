using System.Numerics;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public sealed class VertexArrayObject<TVertexType, TIndexType> : IDisposable
    where TVertexType : unmanaged
    where TIndexType : unmanaged
{
    internal uint _handle;
    private GL _gl;
    public uint Handle => _handle;

    private const uint VERTEX_BINDING_INDEX = 0;
    private const uint INSTANCE_BINDING_INDEX = 1;

    public VertexArrayObject(GL gl)
    {
        _gl = gl;

        _handle = _gl.GenVertexArray();
    }

    public unsafe void VertexAttributePointerFloat(uint index, int count, VertexAttribType type, int offset)
    {
        _gl.EnableVertexAttribArray(index);
        _gl.VertexAttribFormat(index, count, type, false, (uint)(offset * sizeof(TVertexType)));
        _gl.VertexAttribBinding(index, VERTEX_BINDING_INDEX);
    }

    public unsafe void VertexAttributePointerInt(uint index, int count, VertexAttribIType type, int offset)
    {
        _gl.EnableVertexAttribArray(index);
        _gl.VertexAttribIFormat(index, count, type, (uint)(offset * sizeof(TVertexType)));
        _gl.VertexAttribBinding(index, VERTEX_BINDING_INDEX);
    }

    public unsafe void BindVertexBuffer(uint buffer, uint vertexValueCount)
    {
        _gl.BindVertexBuffer(VERTEX_BINDING_INDEX, buffer, 0, vertexValueCount * (uint)sizeof(TVertexType));
    }

    public unsafe void EnableInstancedMatrix(uint index)
    {
        for (uint i = 0; i < 4; ++i) {
            _gl.EnableVertexAttribArray(index + i);
            _gl.VertexAttribFormat(index + i, 4, VertexAttribType.Float, false, (uint)(i * sizeof(Vector4)));
            _gl.VertexAttribBinding(index + i, INSTANCE_BINDING_INDEX);
        }
        _gl.VertexBindingDivisor(INSTANCE_BINDING_INDEX, 1);
    }

    public unsafe void UpdateInstancedMatrixBuffer(uint buffer, uint byteOffset)
    {
        _gl.BindVertexBuffer(INSTANCE_BINDING_INDEX, buffer, (IntPtr)byteOffset, (uint)sizeof(Matrix4x4));
    }

    public void Bind()
    {
        _gl.BindVertexArray(_handle);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_handle);
    }

    public override string ToString() => $"[{_handle}] VAO<{typeof(TVertexType).Name}, {typeof(TIndexType).Name}>";
}
