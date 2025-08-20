using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class Material(GL gl) : IDisposable
{
    private GL _gl { get; } = gl;

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}