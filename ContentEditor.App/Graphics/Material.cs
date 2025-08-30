using System.Numerics;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class Material
{
    private GL _gl { get; }

    private Shader shader;

    public Shader Shader => shader;
    private readonly List<MaterialParameter<Vector4>> vec4Parameters = new();
    private readonly List<MaterialParameter<float>> floatParameters = new();
    private readonly List<(string name, TextureUnit slot, Texture? tex)> textureParameters = new();

    public string name = string.Empty;
    public MaterialBlendMode BlendMode = new();
    public bool DisableDepth;

    public IEnumerable<Texture> Textures => textureParameters.Where(t => t.tex != null).Select(t => t.tex!);

    public Material(GL gl, Shader shader, string name = "")
    {
        _gl = gl;
        this.shader = shader;
        this.name = name;
    }

    private void SetParameter<TValue>(List<MaterialParameter<TValue>> list, string name, TValue vec)
    {
        var param = list.FirstOrDefault(v => v.name == name);
        if (param == null) {
            var loc = _gl.GetUniformLocation(shader.Handle, name);
            if (loc == -1) {
                Logger.Error($"uniform {name} not found in shader.");
                return;
            }
            list.Add(new MaterialParameter<TValue>(vec, loc));
        } else {
            param.Value = vec;
        }
    }

    public void AddTextureParameter(string name, TextureUnit slot)
    {
        textureParameters.Add((name, slot, null));
    }
    public bool HasTextureParameter(TextureUnit slot) => textureParameters.FindIndex(a => a.slot == slot) != -1;

    public void SetParameter(string name, Vector4 vec) => SetParameter(vec4Parameters, name, vec);
    public void SetParameter(string name, float vec) => SetParameter(floatParameters, name, vec);
    public void SetParameter(string name, Color col) => SetParameter(vec4Parameters, name, col.ToVector4());
    public void SetParameter(string name, TextureUnit slot, Texture tex)
    {
        var param = textureParameters.FindIndex(v => v.name == name);
        if (param == -1) {
            var loc = _gl.GetUniformLocation(shader.Handle, name);
            if (loc == -1) {
                Logger.Error($"uniform {name} not found in shader.");
                return;
            }
            textureParameters.Add((name, slot, tex));
        } else {
            textureParameters[param] = (name, slot, tex);
        }
    }
    public void SetParameter(TextureUnit slot, Texture tex)
    {
        var param = textureParameters.FindIndex(v => v.slot == slot);
        if (param == -1) {
            Logger.Error($"Texture unit {slot} not known for shader.");
            return;
        } else {
            textureParameters[param] = (textureParameters[param].name, slot, tex);
        }
    }

    public void Bind()
    {
        shader.Use();
        foreach (var param in vec4Parameters) {
            _gl.Uniform4(param._location, param.Value);
        }
        foreach (var param in floatParameters) {
            _gl.Uniform1(param._location, param.Value);
        }
        foreach (var (name, slot, tex) in textureParameters) {
            if (tex == null) continue;

            tex.Bind(slot);
            shader.SetUniform(name, tex);
        }
    }

    public Material Clone()
    {
        var mat = new Material(_gl, shader);
        mat.vec4Parameters.AddRange(vec4Parameters.Select(x => new MaterialParameter<Vector4>(x.Value, x._location)));
        mat.floatParameters.AddRange(floatParameters.Select(x => new MaterialParameter<float>(x.Value, x._location)));
        mat.textureParameters.AddRange(textureParameters);
        mat.BlendMode = new MaterialBlendMode(BlendMode.Blend, BlendMode.BlendModeSrc, BlendMode.BlendModeDest);
        mat.DisableDepth = DisableDepth;
        mat.name = name;
        return mat;
    }

    public override string ToString() => $"{shader} [tex count: {textureParameters.Count}; first: {textureParameters.FirstOrDefault().tex}]";
}

public sealed class MaterialParameter<TValue>(TValue value, int location) // where TValue : unmanaged, IEquatable<TValue>
{
    public string name = string.Empty;
    public int _location { get; } = location;
    public TValue Value { get; set; } = value;
}

public record MaterialBlendMode(
    bool Blend = false,
    BlendingFactor BlendModeSrc = BlendingFactor.SrcAlpha,
    BlendingFactor BlendModeDest = BlendingFactor.DstAlpha
);

public enum EditorPresetMaterials
{
    Default,
    Wireframe,
    WireframeFilled,
}
