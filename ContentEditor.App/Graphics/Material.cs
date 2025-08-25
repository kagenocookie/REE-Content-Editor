using System.Numerics;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class Material
{
    private GL _gl { get; }

    private Shader shader;
    internal List<(string name, TextureUnit slot, Texture tex)> textures;

    public Shader Shader => shader;
    private readonly List<MaterialParameter<Vector4>> Vec4Parameters = new();
    private readonly List<MaterialParameter<float>> FloatParameters = new();

    public Material(GL gl, Shader shader, List<(string name, TextureUnit slot, Texture tex)> textures)
    {
        _gl = gl;
        this.shader = shader;
        this.textures = textures;
    }

    private void SetParameter<TValue>(List<MaterialParameter<TValue>> list, string name, TValue vec) where TValue : unmanaged, IEquatable<TValue>
    {
        var param = list.FirstOrDefault(v => v.name == name);
        if (param == null) {
            var loc =_gl.GetUniformLocation(shader.Handle, name);
            if (loc == -1)
            {
                Logger.Error($"uniform {name} not found in shader.");
                return;
            }
            list.Add(new MaterialParameter<TValue>(vec, loc));
        } else {
            param.Value = vec;
        }
    }

    public void SetParameter(string name, Vector4 vec) => SetParameter(Vec4Parameters, name, vec);
    public void SetParameter(string name, float vec) => SetParameter(FloatParameters, name, vec);
    public void SetParameter(string name, Color col) => SetParameter(Vec4Parameters, name, col.ToVector4());

    public void Bind()
    {
        shader.Use();
        foreach (var param in Vec4Parameters) {
            _gl.Uniform4(param._location, param.Value);
        }
        foreach (var param in FloatParameters) {
            _gl.Uniform1(param._location, param.Value);
        }
        foreach (var (name, slot, tex) in textures) {
            tex.Bind(slot);
            shader.SetUniform(name, tex);
        }
    }

    public override string ToString() => $"{shader} [tex count: {textures.Count}; first: {textures.FirstOrDefault().tex}]";
}

public sealed class MaterialParameter<TValue>(TValue value, int location) where TValue : unmanaged, IEquatable<TValue>
{
    public string name = string.Empty;
    public int _location { get; } = location;
    public TValue Value { get; set; } = value;
}
