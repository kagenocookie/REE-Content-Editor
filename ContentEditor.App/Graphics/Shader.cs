using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class Shader : IDisposable
{
    private uint _handle;
    public uint Handle => _handle;
    private GL _gl;
    public string Name { get; private set; }

    internal Shader(GL gl)
    {
        _gl = gl;
        Name = string.Empty;
    }

    public Shader(GL gl, string shaderPath, string[]? defines = null, int version = 330)
    {
        _gl = gl;

        LoadFromCombinedShaderFile(shaderPath, defines, version);
        Name = Path.GetFileNameWithoutExtension(shaderPath);
    }

    public bool LoadFromCombinedShaderFile(string shaderPath, string[]? defines = null, int version = 330)
    {
        var (vertex, fragment) = LoadCombinedShader(shaderPath, defines, version);
        CreateProgram(vertex, fragment);
        return true;
    }

    private void CreateProgram(uint vertex, uint fragment)
    {
        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        _gl.LinkProgram(_handle);
        _gl.GetProgram(_handle, GLEnum.LinkStatus, out var status);
        if (status == 0) {
            throw new Exception($"Program failed to link with error: {_gl.GetProgramInfoLog(_handle)}");
        }
        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
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
        //A new overload has been created for setting a uniform so we can use the transform in our shader.
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            Logger.Error($"{name} uniform not found on shader.");
            return;
        }
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
        _gl.DeleteProgram(_handle);
    }

    private (uint vertex, uint fragment) LoadCombinedShader(string shaderFilepath, string[]? defines, int version)
    {
        var ctx = new ShaderContext(Path.GetDirectoryName(shaderFilepath)!);
        defines ??= [];
        var vertexText = ctx.ParseShader(ShaderType.VertexShader, shaderFilepath, Path.GetFileName(shaderFilepath), version, defines, true);
        var fragmentText = ctx.ParseShader(ShaderType.FragmentShader, shaderFilepath, Path.GetFileName(shaderFilepath), version, defines, true);

        var vertex = LoadShader(ShaderType.VertexShader, vertexText);
        var frag = LoadShader(ShaderType.FragmentShader, fragmentText);
        return (vertex, frag);
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

        public string ParseShader(ShaderType type, string shaderFilepath, string name, int version, string[] defines, bool isMain)
        {
            var dict = type == ShaderType.VertexShader ? VertShaders : FragShaders;
            if (dict.TryGetValue(name, out var text)) return text;

            var shaderLines = File.ReadAllLines(shaderFilepath).ToList();
            if (isMain) {
                if (!shaderLines[0].StartsWith("#version")) {
                    shaderLines.Insert(0, $"#version {version} core");
                }
                var typeDefine = type == ShaderType.FragmentShader ? "FRAGMENT_PROGRAM" : "VERTEX_PROGRAM";
                if (!shaderLines[1].StartsWith("#define " + typeDefine)) {
                    shaderLines.Insert(1, "#define " + typeDefine);
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

    private static string ParseShader(ShaderType type, string shaderFile, int version)
    {
        var shader = File.ReadAllText(shaderFile);

        return shader;
    }

    public override string ToString() => $"[{_handle}] {Name}";
}

public static class ShaderDefines
{
    public const string EnableSkinning = "ENABLE_SKINNING";
}
