using ReeLib.Common;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public sealed class Shader : IDisposable
{
    private uint _handle;
    public uint Handle => _handle;
    private GL _gl;
    public string Name { get; private set; }
    public ShaderFlags Flags { get; }

    /// <summary>
    /// 16-bit shader ID (8bit mmh3 hash of filepath + 8bit ShaderFlags)
    /// </summary>
    public readonly uint ID;

    private static readonly Dictionary<ShaderFlags, string[]> FlagDefines = new() {
        { ShaderFlags.None, [] },
        { ShaderFlags.EnableSkinning, ["ENABLE_SKINNING"] },
        { ShaderFlags.EnableStreamingTex, ["ENABLE_STREAMING_TEX"] },
        { ShaderFlags.EnableInstancing, ["ENABLE_INSTANCING"] },
    };

    static Shader()
    {
        // build defines lookup tables for all flag combinations for faster runtime lookups
        var keys = FlagDefines.Keys.Where(f => f != ShaderFlags.None).ToArray();
        foreach (var key in keys) {
            foreach (var key2 in keys) {
                if (key == key2 || FlagDefines.ContainsKey(key | key2)) continue;

                FlagDefines[key | key2] = FlagDefines[key].Concat(FlagDefines[key2]).ToArray();
            }
        }
    }

    public const int MaxBoneCount = 250;

    public Shader(GL gl, string shaderPath, ShaderFlags flags = ShaderFlags.None, int version = 330)
    {
        _gl = gl;
        Flags = flags;
        // using just part of the hash here: 8 bits for path hash + 8 bits for the flags
        // should be good enough since we probably won't have more than maybe 20 unique shaders
        var shaderId = MurMur3HashUtils.GetHash(shaderPath);
        ID = (uint)(shaderId & 0x0000ff00) + (uint)flags;

        LoadFromCombinedShaderFile(shaderPath, flags, version);
        Name = Path.GetFileNameWithoutExtension(shaderPath);
    }

    public bool LoadFromCombinedShaderFile(string shaderPath, ShaderFlags flags = ShaderFlags.None, int version = 330)
    {
        var defines = FlagDefines[flags];
        var (vertex, fragment, geometry) = LoadCombinedShader(shaderPath, defines, version);
        CreateProgram(vertex, fragment, geometry);
        return true;
    }

    private void CreateProgram(uint vertex, uint fragment, uint geometry)
    {
        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        if (geometry != 0) {
            _gl.AttachShader(_handle, geometry);
        }
        _gl.LinkProgram(_handle);
        _gl.GetProgram(_handle, GLEnum.LinkStatus, out var status);
        if (status == 0) {
            throw new Exception($"Program failed to link with error: {_gl.GetProgramInfoLog(_handle)}");
        }
        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
        if (geometry != 0) {
            _gl.DetachShader(_handle, geometry);
            _gl.DeleteShader(geometry);
        }
    }

    public void Use()
    {
        _gl.UseProgram(_handle);
    }

    public void SetUniform(string name, int value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform1(location, value);
    }

    public void SetUniform(string name, float value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            Logger.Error($"{name} uniform not found on shader.");
            return;
        }
        _gl.Uniform1(location, value);
    }

    public unsafe void SetUniform(string name, Matrix4X4<float> value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            Logger.Error($"{name} uniform not found on shader.");
            return;
        }
        _gl.UniformMatrix4(location, 1, false, (float*) &value);
    }

    public unsafe void SetUniform(int location, Matrix4X4<float> value)
    {
        _gl.UniformMatrix4(location, 1, false, (float*) &value);
    }

    public void SetUniform(string name, Texture texture)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            Logger.Error($"{name} uniform not found on shader.");
            return;
        }
        _gl.Uniform1(location, texture.Handle);
    }

    public void Dispose()
    {
        if (_handle != 0) {
            _gl.DeleteProgram(_handle);
            _handle = 0;
        }
    }

    private (uint vertex, uint fragment, uint geometry) LoadCombinedShader(string shaderFilepath, string[]? defines, int version)
    {
        var ctx = new ShaderContext(Path.GetDirectoryName(shaderFilepath)!);
        defines ??= [];
        var vertexText = ctx.ParseShader(ShaderType.VertexShader, shaderFilepath, Path.GetFileName(shaderFilepath), version, defines, true);
        var fragmentText = ctx.ParseShader(ShaderType.FragmentShader, shaderFilepath, Path.GetFileName(shaderFilepath), version, defines, true);
        var geometryText = ctx.ParseShader(ShaderType.GeometryShader, shaderFilepath, Path.GetFileName(shaderFilepath), version, defines, true);

        var vertex = LoadShader(ShaderType.VertexShader, vertexText);
        var frag = LoadShader(ShaderType.FragmentShader, fragmentText);
        var geometry = string.IsNullOrEmpty(geometryText)? 0 : LoadShader(ShaderType.GeometryShader, geometryText);
        return (vertex, frag, geometry);
    }

    private uint LoadShader(ShaderType type, string src)
    {
        uint handle = _gl.CreateShader(type);
        _gl.ShaderSource(handle, src);
        _gl.CompileShader(handle);
        string infoLog = _gl.GetShaderInfoLog(handle);
        if (!string.IsNullOrWhiteSpace(infoLog))
        {
            throw new Exception($"Error compiling shader of type {type}, failed with error {infoLog}");
        }

        return handle;
    }

    private class ShaderContext(string basepath)
    {
        public Dictionary<string, string> VertShaders = new();
        public Dictionary<string, string> FragShaders = new();
        public Dictionary<string, string> GeoShaders = new();

        public string ParseShader(ShaderType type, string shaderFilepath, string name, int version, string[] defines, bool isMain)
        {
            var dict = type switch {
                ShaderType.VertexShader => VertShaders,
                ShaderType.FragmentShader => FragShaders,
                ShaderType.GeometryShader => GeoShaders,
                _ => null,
            };
            if (dict == null) return string.Empty;
            if (dict.TryGetValue(name, out var text)) return text;

            var shaderLines = File.ReadAllLines(shaderFilepath).ToList();
            if (isMain) {
                var typeDefine = type switch {
                    ShaderType.FragmentShader => "FRAGMENT_PROGRAM",
                    ShaderType.VertexShader => "VERTEX_PROGRAM",
                    ShaderType.GeometryShader => "GEOMETRY_PROGRAM",
                    _ => null,
                };
                if (typeDefine == null) return "";
                if (!shaderLines.Contains("#ifdef " + typeDefine)) {
                    return string.Empty;
                }

                if (!shaderLines[0].StartsWith("#version")) {
                    shaderLines.Insert(0, $"#version {version} core");
                }

                if (!shaderLines[1].StartsWith("#define " + typeDefine)) {
                    shaderLines.Insert(1, "#define " + typeDefine);
                }

                foreach (var define in defines) {
                    shaderLines.Insert(1, "#define " + define);
                }
            }
            for (int i = 0; i < shaderLines.Count; i++) {
                var line = shaderLines[i];
                if (line.StartsWith("#include ")) {
                    var includeName = line.Substring("#include ".Length).Trim().TrimEnd(';').Trim('"');
                    var includePath = Path.Combine(basepath, includeName);
                    var include = ParseShader(type, includePath, includeName, version, defines, false);
                    shaderLines[i] = include;
                }
            }

            return dict[name] = string.Join('\n', shaderLines);
        }
    }

    public override string ToString() => $"[{_handle}] {Name}";
}

public static class ShaderDefines
{
    public const string EnableSkinning = "ENABLE_SKINNING";
}
