using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class Material : IDisposable
{
    private GL _gl { get; }

    private Shader shader;
    internal List<(string name, TextureUnit slot, Texture tex)> textures;

    public Shader Shader => shader;

    public Material(GL gl, Shader shader, List<(string name, TextureUnit slot, Texture tex)> textures)
    {
        _gl = gl;
        this.shader = shader;
        this.textures = textures;
    }

    public void Bind()
    {
        shader.Use();
        foreach (var (name, slot, tex) in textures) {
            tex.Bind(slot);
            shader.SetUniform(name, tex);
        }
    }

    public void Dispose()
    {
        shader.Dispose();
        foreach (var (name, slot, tex) in textures) tex.Dispose();
        textures.Clear();
    }

    public override string ToString() => $"{shader} [tex: {textures.Count}]";
}