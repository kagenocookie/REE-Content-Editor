using System.Numerics;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class Shader : IDisposable
{
    private uint _handle;
    private GL _gl;

    public Shader(GL gl, string vertexPath, string fragmentPath)
    {
        _gl = gl;

        uint vertex = LoadShader(ShaderType.VertexShader, vertexPath);
        uint fragment = LoadShader(ShaderType.FragmentShader, fragmentPath);
        CreateProgram(vertex, fragment);
    }

    public Shader(GL gl, string shaderPath, int version = 330)
    {
        _gl = gl;

        var (vertex, fragment) = LoadCombinedShader(shaderPath, version);
        CreateProgram(vertex, fragment);
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

    public unsafe void SetUniform(string name, Matrix4x4 value)
    {
        //A new overload has been created for setting a uniform so we can use the transform in our shader.
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.UniformMatrix4(location, 1, false, (float*) &value);
    }

    public void SetUniform(string name, float value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform1(location, value);
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_handle);
    }

    private (uint vertex, uint fragment) LoadCombinedShader(string path, int version)
    {
        var content = File.ReadAllText(path);
        var vertex = LoadShader(ShaderType.VertexShader, $"#version {version} core\n#define VERTEX_PROGRAM\n" + content);
        var frag = LoadShader(ShaderType.FragmentShader, $"#version {version} core\n#define FRAGMENT_PROGRAM\r\n" + content);
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
}