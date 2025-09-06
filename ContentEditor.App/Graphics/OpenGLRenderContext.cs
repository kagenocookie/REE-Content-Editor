using System.Runtime.CompilerServices;
using ContentPatcher;
using ReeLib;
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

    private bool _wasBlend, _wasDisableDepth;

    private readonly Dictionary<string, Shader> shaders = new();

    private Shader StandardShader => GetShader("Shaders/GLSL/standard3D.glsl");
    private Shader ViewShadedShader => GetShader("Shaders/GLSL/viewShaded.glsl");
    private Shader WireShader => GetShader("Shaders/GLSL/wireframe.glsl");
    private Shader MonoShader => GetShader("Shaders/GLSL/unshaded-color.glsl");
    private Shader FilledWireShader => GetShader("Shaders/GLSL/wireframe-uv.glsl");

    public override IEnumerable<Material> GetPresetMaterials(EditorPresetMaterials preset)
    {
        switch (preset) {
            case EditorPresetMaterials.Wireframe:
                yield return CreateFilledWireMaterial();
                yield return CreateWireMaterial();
                yield break;
            case EditorPresetMaterials.Default:
            default:
                yield return CreateViewShadedMaterial();
                yield break;
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
        _wireMaterial.SetParameter("_OuterColor", new Color(0, 0, 0, 5));
        _wireMaterial.SetParameter("_InnerColor", new Color(0, 255, 0, 200));
        _wireMaterial.name = "wire";
        return _wireMaterial.Clone();
    }
    private Material? _filledWireMaterial;
    private Material CreateFilledWireMaterial()
    {
        if (_filledWireMaterial != null) return _filledWireMaterial.Clone();
        _filledWireMaterial = new(GL, FilledWireShader);
        _filledWireMaterial.SetParameter("_OuterColor", new Color(0, 0, 0, 5));
        _filledWireMaterial.SetParameter("_InnerColor", new Color(0, 255, 0, 200));
        _filledWireMaterial.name = "wireFilled";
        return _filledWireMaterial.Clone();
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

        return shaders[path] = shader = new Shader(GL, Path.Combine(AppContext.BaseDirectory, path));
    }

    public override (MeshHandle, ShapeMesh) CreateShapeMesh()
    {
        var mesh = new ShapeMesh(GL);
        var innerHandle = new MeshResourceHandle(mesh);
        var handle = new MeshHandle(innerHandle);
        MeshRefs.AddUnnamed(innerHandle);
        return (handle, mesh);
    }

    protected override MeshResourceHandle? LoadMeshResource(FileHandle fileHandle)
    {
        var meshResource = fileHandle.Resource as AssimpMeshResource ?? fileHandle.GetCustomContent<AssimpMeshResource>();
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

    protected override MeshHandle CreateMeshInstanceHandle(MeshResourceHandle resource, FileHandle file)
    {
        if (file.Loader is McolFileLoader) {
            return new McolMeshHandle(GL, resource, file.GetFile<McolFile>());
        } else {
            return new MeshHandle(resource);
        }
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

    private MeshHandle? lastInstancedMesh;
    private Material? lastMaterial;
    public override unsafe void RenderSimple(MeshHandle handle, in Matrix4X4<float> transform)
    {
        for (int i = 0; i < handle.Handle.Meshes.Count; i++) {
            var mesh = handle.Handle.Meshes[i];
            var material = handle.GetMaterial(i);

            // TODO frustum culling
            // var bounds = sub.mesh.BoundingBox;

            mesh.Bind();
            if (lastMaterial != material) {
                lastMaterial = material;
                material.Bind();
            }
            material.Shader.SetUniform("uModel", transform);

            GL.DrawArrays(PrimitiveType.Triangles, 0, (uint)mesh.Indices.Length);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
        }
        lastInstancedMesh = null;
    }

    public override void RenderInstanced(MeshHandle mesh, int instanceIndex, int instanceCount, in Matrix4X4<float> transform)
    {
        var actMesh = mesh.GetMesh(0);
        if (lastInstancedMesh != mesh) {
            mesh.GetMaterial(0).Bind();
            lastInstancedMesh = mesh;
        }

        actMesh.Bind();
        mesh.GetMaterial(0).Shader.SetUniform("uModel", transform);
        GL.DrawArrays(PrimitiveType.Triangles, 0, (uint)actMesh.Indices.Length);

        // TODO actually do instanced drawing
        // GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, (uint)actMesh.Indices.Length, instanceCount);
    }

    private void BindMaterial(Material material)
    {
        material.Bind();
        BindMaterialFlags(material);
    }

    private void BindMaterialFlags(Material material)
    {
        var blend = material.BlendMode;
        if (blend.Blend) {
            if (!_wasBlend) {
                GL.Enable(EnableCap.Blend);
                _wasBlend = true;
            }

            GL.BlendFunc(blend.BlendModeSrc, blend.BlendModeDest);
        } else if (_wasBlend) {
            GL.Disable(EnableCap.Blend);
            _wasBlend = false;
        }

        if (material.DisableDepth) {
            if (!_wasDisableDepth) {
                GL.Disable(EnableCap.DepthTest);
                _wasDisableDepth = true;
            }
        } else if (_wasDisableDepth) {
            GL.Enable(EnableCap.DepthTest);
            _wasDisableDepth = false;
        }
    }

    internal override void BeforeRender()
    {
        lastMaterial = null;
        lastInstancedMesh = null;
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
        _missingTexture.Path = "__missing";
        return _missingTexture;
    }

    private Texture GetDefaultTexture()
    {
        if (_defaultTexture != null) return _defaultTexture;

        _defaultTexture = new Texture(GL);
        _defaultTexture.LoadFromRawData(DefaultWhite, 4, 4);
        _defaultTexture.Path = "__default";
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
