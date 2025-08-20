using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class Quad
{
    public float[] data =
    {
        // aPosition--------   aTexCoords
         0.5f,  0.5f,  0.0f,  1.0f, 1.0f,
         0.5f, -0.5f,  0.0f,  1.0f, 0.0f,
        -0.5f, -0.5f,  0.0f,  0.0f, 0.0f,
        -0.5f,  0.5f,  0.0f,  0.0f, 1.0f
    };

    public unsafe void Bind(GL _gl)
    {
        var _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);

        // Upload the vertices data to the VBO.
        fixed (float* buf = data)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint) (data.Length * sizeof(float)), buf, BufferUsageARB.StaticDraw);

        // The quad indices data.
        uint[] indices =
        {
            0u, 1u, 3u,
            1u, 2u, 3u
        };

        // Create the EBO.
        var _ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);

        // Upload the indices data to the EBO.
        fixed (uint* buf = indices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint) (indices.Length * sizeof(uint)), buf, BufferUsageARB.StaticDraw);


        _gl.EnableVertexAttribArray(0);
        // _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, )

    }
}
