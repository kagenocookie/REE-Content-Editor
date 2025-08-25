using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using ContentPatcher;
using ReeLib;
using ReeLib.via;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using SmartFormat.Utilities;

namespace ContentEditor.App.Graphics;

public abstract class RenderContext : IDisposable
{
    public float DeltaTime { get; internal set; }
    internal Matrix4X4<float> ViewMatrix { get; set; } = Matrix4X4<float>.Identity;
    internal Matrix4X4<float> ProjectionMatrix { get; set; } = Matrix4X4<float>.Identity;
    internal Matrix4X4<float> ViewProjectionMatrix { get; set; } = Matrix4X4<float>.Identity;

    protected Material? defaultMaterial;

    protected ResourceManager _resourceManager = null!;
    internal ResourceManager ResourceManager
    {
        get => _resourceManager;
        set => UpdateResourceManager(value);
    }

    public Vector2 ViewportSize { get; set; }

    protected Vector2 _renderTargetTextureSize;
    protected bool _renderTargetTextureSizeOutdated;

    protected uint _outputTexture;
    public uint RenderTargetTextureHandle => _outputTexture;
    public Vector2 RenderOutputSize => _outputTexture != 0 ? _renderTargetTextureSize : ViewportSize;

    internal virtual void BeforeRender()
    {
        var size = RenderOutputSize;
        ProjectionMatrix = Matrix4X4.CreatePerspectiveFieldOfView(80f * MathF.PI / 180, size.X / size.Y, 0.1f, 1000.0f);
        ViewProjectionMatrix = ViewMatrix * ProjectionMatrix;
    }

    internal virtual void AfterRender()
    {
    }

    /// <summary>
    /// Render a simple mesh (static, single mesh with no animation)
    /// </summary>
    public abstract void RenderSimple(int objectHandle, in Matrix4X4<float> transform);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing) { }

    public abstract int CreateObject(Assimp.Scene scene, int materialGroupId);
    public abstract void DestroyObject(int objectIndex);

    public abstract int LoadMaterialGroup(Assimp.Scene scene);
    public abstract void DestroyMaterialGroup(int objectIndex);

    public abstract AABB GetBounds(int objectHandle);

    protected virtual void UpdateResourceManager(ResourceManager manager)
    {
        _resourceManager = manager;
    }

    public void SetRenderToTexture(Vector2 textureSize = new())
    {
        if (textureSize == _renderTargetTextureSize) return;
        if (textureSize.X < 1 || textureSize.Y < 1) {
            throw new Exception("Invalid negative render texture size");
        }
        _renderTargetTextureSizeOutdated = true;
        _renderTargetTextureSize = textureSize;
    }

    protected sealed class ResourceContainer<TSource, T> where T : class where TSource : class
    {
        private List<T> Objects = new();
        private SortedSet<int> GapIndices = new();
        private Dictionary<TSource, int> ResourceCache = new();

        public IEnumerable<T> Instances => Objects.Where(o => o != null);

        public T this[int index] => Objects[index - 1];

        public int Add(T item)
        {
            int index;
            if (GapIndices.Count != 0) {
                index = GapIndices.First();
                GapIndices.Remove(index);
                Objects[index - 1] = item;
            } else {
                index = Objects.Count + 1;
                Objects.Add(item);
            }
            return index;
        }

        public bool TryGet(TSource item, [MaybeNullWhen(false)] out T data, out int index) => ResourceCache.TryGetValue(item, out index) ? (data = Objects[index]) != null : (data = null) != null;

        public T Remove(int itemIndex)
        {
            var obj = Objects[itemIndex - 1];
            GapIndices.Add(itemIndex);
            (obj as IDisposable)?.Dispose();
            Objects[itemIndex - 1] = null!;
            return obj;
        }

        public void Clear()
        {
            ResourceCache.Clear();
            GapIndices.Clear();
            Objects.Clear();
        }

        public int GetIndex(T obj) => Objects.IndexOf(obj) + 1;
    }
}

public sealed class OpenGLRenderContext(GL gl) : RenderContext
{
    public GL GL { get; } = gl;

    private uint _outputBuffer;
    private uint _outputTexDepthBuffer;

    private ResourceContainer<Assimp.Scene, MeshObjectGroup> Objects = new();
    private ResourceContainer<Assimp.Scene, MaterialGroup> Materials = new();
    private ResourceRefCounter<Texture> Textures = new();

    private readonly Dictionary<string, Shader> shaders = new();

    private Shader StandardShader => GetShader("Shaders/GLSL/standard3D.glsl");
    private Shader ViewShadedShader => GetShader("Shaders/GLSL/viewShaded.glsl");

    private uint _globalUniformBuffer;

    private Texture? _missingTexture;
    private Texture? _defaultTexture;

    private sealed record MeshObjectGroup(List<MeshObject> Objects) : IDisposable
    {
        public int MaterialGroupId { get; set; }

        public void Dispose()
        {
            foreach (var o in Objects) {
                o.Mesh.Dispose();
                // o.Material.Dispose();
            }
        }
    }

    private sealed record MeshObject(Mesh Mesh, Material Material);

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

    private void AddTexture(Material material, Assimp.TextureSlot texture)
    {

    }

    private Shader GetShader(string path)
    {
        if (shaders.TryGetValue(path, out var shader)) {
            return shader;
        }

        return shaders[path] = shader = new Shader(GL, path);
    }

    public override int CreateObject(Assimp.Scene meshScene, int materialGroupId)
    {
        if (Objects.TryGet(meshScene, out var group, out var index)) {
            // ?
        } else {
            group = new(new());
        }

        var inputMaterials = materialGroupId == 0 ? null : Materials[materialGroupId];

        var mats = new List<Material>();
        var texDicts = new Dictionary<string, Texture>();
        var embeddedTex = meshScene.Textures;
        foreach (var srcMat in meshScene.Materials) {
            var textures = new List<(string name, TextureUnit slot, Texture tex)>();
            Shader shader;
            if (srcMat.HasName && true == inputMaterials?.Materials.TryGetValue(srcMat.Name, out var importMat)) {
                shader = ViewShadedShader;
                // textures.Add(("_MainTexture", TextureUnit.Texture0, importMat.textures));
                textures.AddRange(importMat.textures);
            } else if (srcMat.HasTextureDiffuse) {
                var srcTex = srcMat.TextureDiffuse;
                var texUnit = TextureUnit.Texture0;
                if (!texDicts.TryGetValue(srcTex.FilePath, out var tex)) {
                    texDicts[srcTex.FilePath] = tex = LoadTexture(meshScene, srcMat.TextureDiffuse);
                }
                textures.Add(("_MainTexture", texUnit, tex));
                shader = ViewShadedShader;
            } else {
                var texUnit = TextureUnit.Texture0;
                var tex = CreateDefaultTexture();
                textures.Add(("_MainTexture", texUnit, tex));
                shader = ViewShadedShader;
            }
            var newMat = new Material(GL, shader, textures);
            mats.Add(newMat);
        }

        foreach (var srcMesh in meshScene.Meshes) {
            var newMesh = new Mesh(GL, srcMesh);
            group.Objects.Add(new MeshObject(newMesh, mats[srcMesh.MaterialIndex]));
        }
        group.MaterialGroupId = materialGroupId;

        var id = Objects.Add(group);
        // Logger.Debug("Created object handle " + id);
        return id;
    }

    public override void DestroyObject(int objectIndex)
    {
        if (objectIndex <= 0) return;

        var obj = Objects.Remove(objectIndex);
        if (obj.MaterialGroupId != 0) {
            DestroyMaterialGroup(obj.MaterialGroupId);
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
                Textures.AddUnnamed(tex);
                return tex;
            } else if (Textures.TryAddReference(texture.FilePath, out var handle)) {
                return handle.Resource;
            } else if (ResourceManager.TryResolveFile(texture.FilePath, out var texHandle)) {
                var tex = new Texture(GL).LoadFromFile(texHandle);
                return tex;
            } else {
                return CreateMissingTexture();
            }
        } catch (Exception e) {
            Logger.Error("Failed to load texture " + texture.FilePath + ": " + e.Message);
            return CreateMissingTexture();
        }
    }

    public override int LoadMaterialGroup(Assimp.Scene scene)
    {
        if (Materials.TryGet(scene, out var group, out var index)) {
            return index;
        }

        group = new MaterialGroup();
        foreach (var mat in scene.Materials) {
            if (!mat.HasName) {
                Logger.Debug("Material " + scene.Materials.IndexOf(mat) + " has no name");
                continue;
            }
            Material material;
            if (mat.HasTextureDiffuse) {
                var tex = LoadTexture(scene, mat.TextureDiffuse);
                material = new Material(GL, StandardShader, [("_MainTexture", TextureUnit.Texture0, tex)]);
            } else {
                material = new Material(GL, ViewShadedShader, [("_MainTexture", TextureUnit.Texture0, CreateDefaultTexture())]);
            }
            group.Add(mat.Name, material);
        }

        return Materials.Add(group);
    }

    public override void DestroyMaterialGroup(int objectIndex)
    {
        var mats = Materials.Remove(objectIndex);
        if (mats != null) {
            foreach (var m in mats.Materials) {
                foreach (var tex in m.Value.textures) {
                    this.Textures.Dereference(tex.tex);
                }
            }
        }
    }

    protected override void UpdateResourceManager(ResourceManager manager)
    {
        base.UpdateResourceManager(manager);
        foreach (var obj in Objects.Instances) {
            obj.Dispose();
        }
    }

    public override AABB GetBounds(int objectHandle)
    {
        var obj = Objects[objectHandle];
        return obj?.Objects.Aggregate(AABB.MaxMin, (sum, o) => o.Mesh.BoundingBox.Extend(sum)) ?? default;
    }

    public override unsafe void RenderSimple(int objectHandle, in Matrix4X4<float> transform)
    {
        var obj = Objects[objectHandle];
        foreach (var sub in obj.Objects) {
            sub.Mesh.Bind();
            sub.Material.Bind();
            sub.Material.Shader.SetUniform("uModel", transform);
            // var idx = GL.GetUniformBlockIndex(sub.Material.Shader.Handle, "GlobalData");
            // GL.UniformBlockBinding(sub.Material.Shader.Handle, idx, 0);

            GL.DrawElements(PrimitiveType.Triangles, (uint)sub.Mesh.Indices.Length, DrawElementsType.UnsignedInt, null);
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

    private Texture CreateMissingTexture()
    {
        if (_missingTexture != null) return _missingTexture;

        _missingTexture = new Texture(GL);
        _missingTexture.LoadFromRawData(DefaultPink, 4, 4);
        return _missingTexture;
    }

    private Texture CreateDefaultTexture()
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
        _defaultTexture?.Dispose();
        _missingTexture?.Dispose();
        Textures.Dispose();
        foreach (var obj in Objects.Instances) {
            obj.Dispose();
        }
        shaders.Clear();
    }
}
