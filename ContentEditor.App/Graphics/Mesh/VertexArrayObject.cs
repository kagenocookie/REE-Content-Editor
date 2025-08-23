using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class VertexArrayObject<TVertexType, TIndexType> : IDisposable
    where TVertexType : unmanaged
    where TIndexType : unmanaged
{
    internal uint _handle;
    private GL _gl;

    public VertexArrayObject(GL gl, BufferObject<TVertexType> vbo, BufferObject<TIndexType> ebo)
    {
        _gl = gl;

        _handle = _gl.GenVertexArray();
        Bind();
        vbo.Bind();
        ebo.Bind();
    }

    public unsafe void VertexAttributePointer(uint index, int count, VertexAttribPointerType type, uint vertexSize, int offset)
    {
        _gl.VertexAttribPointer(index, count, type, false, vertexSize * (uint)sizeof(TVertexType), (void*)(offset * sizeof(TVertexType)));
        _gl.EnableVertexAttribArray(index);
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
