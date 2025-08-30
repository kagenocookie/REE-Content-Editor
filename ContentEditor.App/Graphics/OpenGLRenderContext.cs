using System.Runtime.CompilerServices;
using ContentPatcher;
using ReeLib.via;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public sealed class OpenGLRenderContext(GL gl) : RenderContext
{
    public GL GL { get; } = gl;

    private uint _outputBuffer;
    private uint _outputTexDepthBuffer;

    private uint _globalUniformBuffer;

    private Texture? _missingTexture;
    private Texture? _defaultTexture;

    private readonly Dictionary<string, Shader> shaders = new();

    private Shader StandardShader => GetShader("Shaders/GLSL/standard3D.glsl");
    private Shader ViewShadedShader => GetShader("Shaders/GLSL/viewShaded.glsl");
    private Shader WireShader => GetShader("Shaders/GLSL/wireframe.glsl");

    public override Material GetPresetMaterial(EditorPresetMaterials preset)
    {
        switch (preset) {
            case EditorPresetMaterials.Wireframe:
                return CreateWireMaterial();
            case EditorPresetMaterials.Default:
            default:
                return CreateViewShadedMaterial();
        }
    }

    private Material? _standardMaterial;
    private Material CreateStandardMaterial()
    {
        if (_standardMaterial != null) return _standardMaterial.Clone();
        _standardMaterial = new(GL, StandardShader);
        _standardMaterial.AddTextureParameter("_MainTexture", TextureUnit.Texture0);
        _standardMaterial.name = "default";
        return _standardMaterial.Clone();
    }

    private Material? _viewShadedMaterial;
    private Material CreateViewShadedMaterial()
    {
        if (_viewShadedMaterial != null) return _viewShadedMaterial.Clone();
        _viewShadedMaterial = new(GL, ViewShadedShader);
        _viewShadedMaterial.AddTextureParameter("_MainTexture", TextureUnit.Texture0);
        _viewShadedMaterial.name = "default";
        return _viewShadedMaterial.Clone();
    }

    private Material? _wireMaterial;
    private Material CreateWireMaterial()
    {
        if (_wireMaterial != null) return _wireMaterial.Clone();
        _wireMaterial = new(GL, WireShader);
        _wireMaterial.BlendMode = new MaterialBlendMode(true, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _wireMaterial.DisableDepth = true;
        _wireMaterial.SetParameter("_MainColor", new Color(0, 0, 0, 5));
        _wireMaterial.SetParameter("_WireColor", new Color(0, 255, 0, 200));
        _wireMaterial.name = "default";
        return _wireMaterial.Clone();
    }

    private unsafe void ApplyGlobalUniforms()
    {
        if (_globalUniformBuffer == 0) {
            _globalUniformBuffer = GL.GenBuffer();
            GL.BindBuffer(BufferTargetARB.UniformBuffer, _globalUniformBuffer);
            GL.BufferData(BufferTargetARB.UniformBuffer, (uint)sizeof(Matrix4X4<float>) * 3, null, BufferUsageARB.StaticDraw);
            GL.BindBuffer(BufferTargetARB.UniformBuffer, 0);
        }

        GL.BindBuffer(BufferTargetARB.UniformBuffer, _globalUniformBuffer);
        SetUniformBufferMatrix(0, sizeof(Matrix4X4<float>), ViewMatrix);
        SetUniformBufferMatrix(sizeof(Matrix4X4<float>), sizeof(Matrix4X4<float>), ProjectionMatrix);
        SetUniformBufferMatrix(sizeof(Matrix4X4<float>) * 2, sizeof(Matrix4X4<float>), ViewProjectionMatrix);
        GL.BindBufferBase(BufferTargetARB.UniformBuffer, 0, _globalUniformBuffer);
        GL.BindBuffer(BufferTargetARB.UniformBuffer, 0);
    }

    private unsafe void SetUniformBufferMatrix(int offset, int size, Matrix4X4<float> value)
    {
        GL.BufferSubData(BufferTargetARB.UniformBuffer, (nint)offset, (uint)size, (float*)&value);
    }

    private Shader GetShader(string path)
    {
        if (shaders.TryGetValue(path, out var shader)) {
            return shader;
        }

        return shaders[path] = shader = new Shader(GL, path);
    }

    public override (MeshHandle, ShapeMesh) CreateShapeMesh()
    {
        var mesh = new ShapeMesh(GL);
        var innerHandle = new MeshResourceHandle(mesh);
        var handle = new MeshHandle(innerHandle);
        return (handle, mesh);
    }

    protected override MeshResourceHandle? LoadMeshResource(FileHandle fileHandle)
    {
        var meshResource = fileHandle.GetResource<AssimpMeshResource>();
        if (meshResource == null) return null;

        var handle = new MeshResourceHandle(MeshRefs.NextInstanceID);
        var meshScene = meshResource.Scene;

        var matDict = new Dictionary<string, int>(meshScene.Meshes.Count);
        foreach (var srcMesh in meshScene.Meshes) {
            var newMesh = new TriangleMesh(GL, srcMesh);
            if (meshScene.HasMaterials) {
                var matname = meshScene.Materials[srcMesh.MaterialIndex].Name;
                handle.SetMaterialName(handle.Meshes.Count, matname);
            } else {
                handle.SetMaterialName(handle.Meshes.Count, "");
            }
            handle.Meshes.Add(newMesh);
        }

        return handle;
    }

    private Texture LoadTexture(Assimp.Scene scene, Assimp.TextureSlot texture)
    {
        try {
            if (texture.FilePath.StartsWith('*')) {
                var texData = scene.GetEmbeddedTexture(texture.FilePath);
                var tex = new Texture(GL);
                if (texData.HasCompressedData) {
                    var stream = new MemoryStream(texData.CompressedData);
                    tex.LoadFromStream(stream);
                } else {
                    // untested
                    var bytes = Unsafe.As<byte[]>(texData.NonCompressedData);
                    tex.LoadFromRawData(bytes, (uint)texData.Width, (uint)texData.Height);
                }
                TextureRefs.AddUnnamed(tex);
                return tex;
            } else if (TextureRefs.TryAddReference(texture.FilePath, out var handle)) {
                return handle.Resource;
            } else if (ResourceManager.TryResolveFile(texture.FilePath, out var texHandle)) {
                var tex = new Texture(GL).LoadFromFile(texHandle);
                return tex;
            } else {
                return GetMissingTexture();
            }
        } catch (Exception e) {
            Logger.Error("Failed to load texture " + texture.FilePath + ": " + e.Message);
            return GetMissingTexture();
        }
    }

    public override MaterialGroup LoadMaterialGroup(FileHandle file)
    {
        if (MaterialRefs.TryAddReference(file, out var groupRef)) {
            AddMaterialTextureReferences(groupRef.Resource);
            return groupRef.Resource;
        }

        var scene = (file.Resource as AssimpMaterialResource)?.Scene ?? (file.Resource as AssimpMeshResource)?.Scene;
        if (scene == null) {
            Logger.Error("Failed to load material group - invalid resource type " + file.Filepath);
            return new MaterialGroup();
        }

        var group = new MaterialGroup();
        foreach (var mat in scene.Materials) {
            if (!mat.HasName) {
                Logger.Debug("Material " + scene.Materials.IndexOf(mat) + " has no name");
                continue;
            }

            var material = CreateViewShadedMaterial();
            // var material = CreateWireMaterial();
            material.name = mat.Name;
            if (material.HasTextureParameter(TextureUnit.Texture0)) {
                if (mat.HasTextureDiffuse) {
                    var tex = LoadTexture(scene, mat.TextureDiffuse);
                    material.SetParameter(TextureUnit.Texture0, tex);
                } else {
                    material.SetParameter(TextureUnit.Texture0, GetDefaultTexture());
                }
            }
            group.Add(material);
        }

        MaterialRefs.Add(file, group);
        return group;
    }

    public override unsafe void RenderSimple(MeshHandle handle, in Matrix4X4<float> transform)
    {
        for (int i = 0; i < handle.Handle.Meshes.Count; i++) {
            var mesh = handle.Handle.Meshes[i];
            var material = handle.GetMaterial(i);

            // TODO frustum culling
            // var bounds = sub.mesh.BoundingBox;

            mesh.Bind();
            material.Bind();
            material.Shader.SetUniform("uModel", transform);

            var blend = material.BlendMode;
            if (blend.Blend) {
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(blend.BlendModeSrc, blend.BlendModeDest);
            }
            if (material.DisableDepth) {
                GL.Disable(EnableCap.DepthTest);
            }

            GL.DrawArrays(PrimitiveType.Triangles, 0, (uint)mesh.Indices.Length);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
        }
    }

    internal override void BeforeRender()
    {
        if (_renderTargetTextureSizeOutdated) {
            UpdateRenderTarget();
        }

        if (_outputBuffer != 0) {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _outputBuffer);
            GL.ClearColor(0, 0, 0, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Viewport(new System.Drawing.Size((int)_renderTargetTextureSize.X, (int)_renderTargetTextureSize.Y));
        }
        base.BeforeRender();
        ApplyGlobalUniforms();
    }

    internal override void AfterRender()
    {
        if (_outputBuffer != 0) {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(new System.Drawing.Size((int)ViewportSize.X, (int)ViewportSize.Y));
        }
    }

    private unsafe void UpdateRenderTarget()
    {
        _renderTargetTextureSizeOutdated = false;
        if (_outputBuffer == 0) {
            _outputBuffer = GL.GenFramebuffer();
        }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _outputBuffer);

        if (_outputTexDepthBuffer != 0) {
            GL.DeleteRenderbuffer(_outputTexDepthBuffer);
            _outputTexDepthBuffer = 0;
        }

        if (_outputTexture == 0) {
            _outputTexture = GL.GenTexture();
        }

        GL.BindTexture(TextureTarget.Texture2D, _outputTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb, (uint)_renderTargetTextureSize.X, (uint)_renderTargetTextureSize.Y, 0, PixelFormat.Rgb, PixelType.UnsignedByte, null);
        uint nearest = (uint)GLEnum.Nearest;
        GL.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in nearest);
        GL.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in nearest);

        _outputTexDepthBuffer = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _outputTexDepthBuffer);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent, (uint)_renderTargetTextureSize.X, (uint)_renderTargetTextureSize.Y);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _outputTexDepthBuffer);
        GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, _outputTexture, 0);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete) {
            throw new Exception("Failed to set up render target texture");
        }
    }

    private static readonly byte[] DefaultPink = [
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
        0xff, 0x50, 0xff, 0xff,
    ];

    private static readonly byte[] DefaultWhite = [
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
        0xff, 0xff, 0xff, 0xff,
    ];

    private Texture GetMissingTexture()
    {
        if (_missingTexture != null) return _missingTexture;

        _missingTexture = new Texture(GL);
        _missingTexture.LoadFromRawData(DefaultPink, 4, 4);
        return _missingTexture;
    }

    private Texture GetDefaultTexture()
    {
        if (_defaultTexture != null) return _defaultTexture;

        _defaultTexture = new Texture(GL);
        _defaultTexture.LoadFromRawData(DefaultWhite, 4, 4);
        return _defaultTexture;
    }

    protected override void Dispose(bool disposing)
    {
        if (_outputTexDepthBuffer != 0) {
            GL.DeleteRenderbuffer(_outputTexDepthBuffer);
        }
        if (_outputTexture != 0) {
            GL.DeleteTexture(_outputTexture);
        }
        if (_outputBuffer != 0) {
            GL.DeleteFramebuffer(_outputBuffer);
        }
        foreach (var shader in shaders.Values) {
            shader.Dispose();
        }
        shaders.Clear();
        _defaultTexture?.Dispose();
        _missingTexture?.Dispose();
        base.Dispose(disposing);
    }
}
