using System.Numerics;
using System.Runtime.CompilerServices;
using ReeLib.Common;
using ReeLib.via;
using Silk.NET.Maths;
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
    private MaterialParameter<Matrix4X4<float>>? boneMatricesParameter;
    private readonly MaterialParameter<Matrix4X4<float>> modelMatrixParam;

    /// <summary>
    /// 24-bit integer hash of the material parameter names and values.
    /// </summary>
    public uint Hash { get; private set; }

    public string name = string.Empty;
    public MaterialBlendMode BlendMode = new();
    public bool DisableDepth;

    public IEnumerable<Texture> Textures => textureParameters.Where(t => t.tex != null).Select(t => t.tex!);

    public Material(GL gl, Shader shader, string name = "")
    {
        _gl = gl;
        this.shader = shader;
        this.name = name;
        modelMatrixParam = new MaterialParameter<Matrix4X4<float>>(Matrix4X4<float>.Identity, _gl.GetUniformLocation(shader.Handle, "uModel"));
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
            list.Add(new MaterialParameter<TValue>(vec, loc) { name = name });
        } else {
            param.Value = vec;
        }
        RecomputeHash();
    }

    private static TValue GetParameter<TValue>(List<MaterialParameter<TValue>> list, string name)
    {
        var param = list.FirstOrDefault(v => v.name == name);
        return param == null ? default! : (TValue)param.Value;
    }

    public void RecomputeHash()
    {
        uint hash = 17;
        HashParameters(vec4Parameters, ref hash);
        HashParameters(floatParameters, ref hash);
        foreach (var tex in textureParameters) {
            hash = unchecked(hash * 31 + MurMur3HashUtils.GetHash(tex.name));
            hash = unchecked(hash * 31 + MurMur3HashUtils.GetHash(tex.tex?.Path ?? ""));
        }

        // keep only 24 bits because that's how much we use for the render sorting
        Hash = hash & 0xffffff;
    }

    private static void HashParameters<TValue>(List<MaterialParameter<TValue>> list, ref uint hash)
    {
        foreach (var p in list) {
            hash = unchecked(hash * 31 + MurMur3HashUtils.GetHash(p.name));
            hash = unchecked(hash * 31 + (uint)p.Value!.GetHashCode());
        }
    }

    public void AddTextureParameter(string name, TextureUnit slot)
    {
        textureParameters.Add((name, slot, null));
    }
    public bool HasTextureParameter(TextureUnit slot) => textureParameters.FindIndex(a => a.slot == slot) != -1;

    public Color GetColor(string name) => Color.FromVector4(GetParameter<Vector4>(vec4Parameters, name));
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
        RecomputeHash();
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
        RecomputeHash();
    }

    public void Bind()
    {
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
        if (BlendMode.Blend) {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendMode.BlendModeSrc, BlendMode.BlendModeDest);
        } else {
            _gl.Disable(EnableCap.Blend);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void BindModel(in Matrix4X4<float> mat)
    {
        Shader.SetUniform(modelMatrixParam!._location, mat);
    }

    public unsafe void BindBoneMatrices(Span<Matrix4X4<float>> matrices)
    {
        if (boneMatricesParameter == null) {
            var loc = _gl.GetUniformLocation(shader.Handle, "boneMatrices[0]");
            if (loc == -1) {
                Logger.Error($"Uniform boneMatrices not found in shader.");
                return;
            }
            boneMatricesParameter = new MaterialParameter<Matrix4X4<float>>(Matrix4X4<float>.Identity, loc);
        }

        for (int i = 0; i < matrices.Length; ++i) {
            var value = matrices[i];
            _gl.UniformMatrix4(boneMatricesParameter._location + i, 1, false, (float*) &value);
        }
    }

    public Material Clone(string? name = null)
    {
        var mat = new Material(_gl, shader);
        mat.vec4Parameters.AddRange(vec4Parameters.Select(x => new MaterialParameter<Vector4>(x.Value, x._location) { name = x.name }));
        mat.floatParameters.AddRange(floatParameters.Select(x => new MaterialParameter<float>(x.Value, x._location) { name = x.name }));
        mat.textureParameters.AddRange(textureParameters);
        mat.BlendMode = new MaterialBlendMode(BlendMode.Blend, BlendMode.BlendModeSrc, BlendMode.BlendModeDest);
        mat.DisableDepth = DisableDepth;
        mat.name = name ?? this.name;
        mat.Hash = Hash;
        return mat;
    }

    public override string ToString() => $"\"{name}\" ({shader}) [tex count: {textureParameters.Count}; first: {textureParameters.FirstOrDefault().tex}]";
}

public sealed class MaterialParameter<TValue>(TValue value, int location) // where TValue : unmanaged, IEquatable<TValue>
{
    public string name = string.Empty;
    public int _location { get; } = location;
    public TValue Value { get; set; } = value;

    public override string ToString() => $"{name} = {Value}";
}

public record struct MaterialBlendMode(
    bool Blend = false,
    BlendingFactor BlendModeSrc = BlendingFactor.SrcAlpha,
    BlendingFactor BlendModeDest = BlendingFactor.DstAlpha
);

public enum EditorPresetMaterials
{
    Default,
    Wireframe,
}

public enum BuiltInMaterials
{
    ViewShaded,
    MonoColor,
    Wireframe,
    FilledWireframe,
    Standard,
}
