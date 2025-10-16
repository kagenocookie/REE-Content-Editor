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

    public VertexArrayObject(GL gl)
    {
        _gl = gl;

        _handle = _gl.GenVertexArray();
    }

    public unsafe void VertexAttributePointerFloat(uint index, int count, VertexAttribPointerType type, uint vertexSize, int offset)
    {
        _gl.VertexAttribPointer(index, count, type, false, vertexSize * (uint)sizeof(TVertexType), (void*)(offset * sizeof(TVertexType)));
        _gl.EnableVertexAttribArray(index);
    }

    public unsafe void VertexAttributePointerInt(uint index, int count, VertexAttribIType type, uint vertexSize, int offset)
    {
        _gl.VertexAttribIPointer(index, count, type, vertexSize * (uint)sizeof(TVertexType), (void*)(offset * sizeof(TVertexType)));
        _gl.EnableVertexAttribArray(index);
    }

    private const uint INSTANCE_BINDING_INDEX = 10;
    public unsafe void EnableInstancedMatrix(uint index)
    {
        for (uint i = 0; i < 4; ++i) {
            _gl.EnableVertexAttribArray(index + i);
            // _gl.VertexAttribPointer(index + i, 4, VertexAttribPointerType.Float, false, (uint)sizeof(Matrix4x4), (void*)(i * sizeof(Vector4)));
            _gl.VertexAttribBinding(index + i, INSTANCE_BINDING_INDEX);
            _gl.VertexAttribFormat(index + i, 4, VertexAttribType.Float, false, (uint)(i * sizeof(Vector4)));
            _gl.VertexAttribDivisor(index + i, 1);
        }
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
