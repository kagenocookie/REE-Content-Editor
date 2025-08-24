using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using ReeLib.via;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public abstract class RenderContext : IDisposable
{
    public float DeltaTime { get; internal set; }
    internal Matrix4X4<float> ViewMatrix { get; set; } = Matrix4X4<float>.Identity;
    internal Matrix4X4<float> ProjectionMatrix { get; set; } = Matrix4X4<float>.Identity;
    internal Matrix4X4<float> ViewProjectionMatrix { get; set; } = Matrix4X4<float>.Identity;

    protected Material? defaultMaterial;

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

    public abstract int CreateObject(Assimp.Scene scene);
    public abstract void DestroyObject(int objectIndex);

    public abstract AABB GetBounds(int objectHandle);

    public void SetRenderToTexture(Vector2 textureSize = new())
    {
        if (textureSize == _renderTargetTextureSize) return;
        if (textureSize.X < 1 || textureSize.Y < 1) {
            throw new Exception("Invalid negative render texture size");
        }
        _renderTargetTextureSizeOutdated = true;
        _renderTargetTextureSize = textureSize;
    }

    protected sealed class SparseList<T> where T : class
    {
        private List<T> Objects = new();
        private SortedSet<int> GapIndices = new();

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

        public void Remove(int itemIndex)
        {
            var obj = Objects[itemIndex - 1];
            GapIndices.Add(itemIndex);
            (obj as IDisposable)?.Dispose();
            Objects[itemIndex - 1] = null!;
        }
    }
}

public sealed class OpenGLRenderContext(GL gl) : RenderContext
{
    public GL GL { get; } = gl;

    private uint _outputBuffer;
    private uint _outputTexDepthBuffer;

    private SparseList<MeshObjectGroup> Objects = new();
    private Dictionary<Assimp.Scene, MeshObjectGroup> ObjectGroups = new();

    private sealed record MeshObjectGroup(List<MeshObject> Objects) : IDisposable
    {
        public void Dispose()
        {
            foreach (var o in Objects) {
                o.Mesh.Dispose();
                o.Material.Dispose();
            }
        }
    }

    private sealed record MeshObject(Mesh Mesh, Material Material);

    private uint _globalUniformBuffer;

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

    public override int CreateObject(Assimp.Scene scene)
    {
        if (!ObjectGroups.TryGetValue(scene, out var group)) {
            ObjectGroups[scene] = group = new(new());
        }

        // var shader = new Shader(GL, "Shaders/GLSL/standard3D.glsl");
        var shader = new Shader(GL, "Shaders/GLSL/viewShaded.glsl");

        var mats = new List<Material>();
        var texDicts = new Dictionary<string, Texture>();
        var embeddedTex = scene.Textures;
        foreach (var srcMat in scene.Materials) {
            var textures = new List<(string name, TextureUnit slot, Texture tex)>();
            if (srcMat.HasTextureDiffuse) {
                var srcTex = srcMat.TextureDiffuse;
                var texUnit = TextureUnit.Texture0;
                if (!texDicts.TryGetValue(srcTex.FilePath, out var tex)) {
                    texDicts[srcTex.FilePath] = tex = new Texture(GL);
                    if (srcTex.FilePath.StartsWith('*')) {
                        // embedded texture
                        var texIndex = int.Parse(srcTex.FilePath.AsSpan().Slice(1), CultureInfo.InvariantCulture);
                        var texData = embeddedTex[texIndex];
                        // Stream stream;
                        if (texData.HasCompressedData) {
                            var stream = new MemoryStream(texData.CompressedData);
                            tex.LoadFromStream(stream);
                        } else {
                            // untested
                            var bytes = Unsafe.As<byte[]>(texData.NonCompressedData);
                            tex.LoadFromRawData(bytes, (uint)texData.Width, (uint)texData.Height);
                        }
                    } else if (File.Exists(srcTex.FilePath)) {
                        tex.LoadFromFile(srcTex.FilePath);
                    } else {
                        // TODO reuse textures
                        tex = CreateDefaultTexture();
                    }
                }
                textures.Add(("_MainTexture", texUnit, tex));
            } else {
                var texUnit = TextureUnit.Texture0;
                var tex = CreateDefaultTexture();
                textures.Add(("_MainTexture", texUnit, tex));
            }
            var newMat = new Material(GL, shader, textures);
            mats.Add(newMat);
        }

        foreach (var srcMesh in scene.Meshes) {
            var newMesh = new Mesh(GL, srcMesh);
            group.Objects.Add(new MeshObject(newMesh, mats[srcMesh.MaterialIndex]));
        }

        var id = Objects.Add(group);
        // Logger.Debug("Created object handle " + id);
        return id;
    }

    public override void DestroyObject(int objectIndex)
    {
        if (objectIndex <= 0) return;

        Objects.Remove(objectIndex);
        // Logger.Debug("Destroyed object handle " + objectIndex);
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

    private Material CreateDefaultMaterial()
    {
        var shader = new Shader(GL, "Shaders/GLSL/standard3D.glsl");
        var mat = new Material(GL, shader, new());
        return mat;
    }

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

    private Texture CreateDefaultTexture()
    {
        var tex = new Texture(GL);
        tex.LoadFromRawData(DefaultWhite, 4, 4);
        return tex;
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
    }
}
