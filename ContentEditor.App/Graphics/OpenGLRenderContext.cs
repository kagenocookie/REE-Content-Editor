using System.Numerics;
using System.Runtime.CompilerServices;
using ContentEditor.App.FileLoaders;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Mesh;
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

    private readonly Dictionary<(string, ShaderFlags), Shader> shaders = new();
    private readonly Dictionary<(BuiltInMaterials, ShaderFlags), Material> builtInMaterials = new();

    public readonly RenderBatch Batch = new RenderBatch(gl);

    private bool _hasInitGizmos;

    private const float GridCellSpacing = 5f;

    public override IEnumerable<Material> GetPresetMaterials(EditorPresetMaterials preset)
    {
        switch (preset) {
            case EditorPresetMaterials.Wireframe:
                yield return CreateFilledWireMaterial(ShaderFlags.None);
                yield return CreateWireMaterial(ShaderFlags.None);
                yield break;
            case EditorPresetMaterials.Default:
            default:
                yield return CreateViewShadedMaterial(ShaderFlags.None);
                yield break;
        }
    }

    private Material CreateStandardMaterial(ShaderFlags flags)
    {
        if (!builtInMaterials.TryGetValue((BuiltInMaterials.Standard, flags), out var material)) {
            material = new(GL, GetShader("Shaders/GLSL/viewShaded.glsl", flags));
            material.AddTextureParameter("_MainTexture", TextureUnit.Texture0);
            material.name = "default";
        }
        return material.Clone();
    }

    private Material CreateViewShadedMaterial(ShaderFlags flags)
    {
        if (!builtInMaterials.TryGetValue((BuiltInMaterials.ViewShaded, flags), out var material)) {
            material = new(GL, GetShader("Shaders/GLSL/viewShaded.glsl", flags));
            material.AddTextureParameter("_MainTexture", TextureUnit.Texture0);
            material.name = "default";
        }
        return material.Clone();
    }

    private Material CreateWireMaterial(ShaderFlags flags)
    {
        if (!builtInMaterials.TryGetValue((BuiltInMaterials.Wireframe, flags), out var material)) {
            material = new(GL, GetShader("Shaders/GLSL/wireframe.glsl", flags));
            material.SetParameter("_OuterColor", new Color(0, 0, 0, 5));
            material.SetParameter("_InnerColor", new Color(0, 255, 0, 200));
            material.name = "wire";
        }
        return material.Clone();
    }

    private Material CreateFilledWireMaterial(ShaderFlags flags)
    {
        if (!builtInMaterials.TryGetValue((BuiltInMaterials.FilledWireframe, flags), out var material)) {
            material = new(GL, GetShader("Shaders/GLSL/wireframe-uv.glsl", flags));
            material.SetParameter("_OuterColor", new Color(0, 0, 0, 5));
            material.SetParameter("_InnerColor", new Color(0, 255, 0, 200));
            material.name = "wireFilled";
        }
        return material.Clone();
    }

    private Material CreateMonoMaterial(ShaderFlags flags)
    {
        if (!builtInMaterials.TryGetValue((BuiltInMaterials.MonoColor, flags), out var material)) {
            material = new(GL, GetShader("Shaders/GLSL/unshaded-color.glsl", flags));
            material.SetParameter("_MainColor", new Color(255, 255, 255, 255));
            material.SetParameter("_FadeMaxDistance", float.MaxValue);
            material.name = "color";
        }
        return material.Clone();
    }

    public override Material GetBuiltInMaterial(BuiltInMaterials material, ShaderFlags flags = ShaderFlags.None)
    {
        // TODO figure out better, non-hardcoded parametrization for shaders
        return material switch
        {
            BuiltInMaterials.FilledWireframe => CreateFilledWireMaterial(flags),
            BuiltInMaterials.Wireframe => CreateWireMaterial(flags),
            BuiltInMaterials.MonoColor => CreateMonoMaterial(flags),
            BuiltInMaterials.ViewShaded => CreateViewShadedMaterial(flags),
            BuiltInMaterials.Standard => CreateStandardMaterial(flags),
            _ => throw new NotImplementedException("Unsupported material " + material),
        };
    }

    private unsafe void ApplyGlobalUniforms()
    {
        if (_globalUniformBuffer == 0) {
            _globalUniformBuffer = GL.GenBuffer();
            GL.BindBuffer(BufferTargetARB.UniformBuffer, _globalUniformBuffer);
            GL.BufferData(BufferTargetARB.UniformBuffer, (uint)(sizeof(Matrix4X4<float>) * 3 + sizeof(Vector3)), null, BufferUsageARB.StaticDraw);
            GL.BindBuffer(BufferTargetARB.UniformBuffer, 0);
        }

        GL.BindBuffer(BufferTargetARB.UniformBuffer, _globalUniformBuffer);
        int offset = 0;
        SetUniformBufferMatrix(ref offset, sizeof(Matrix4X4<float>), ViewMatrix);
        SetUniformBufferMatrix(ref offset, sizeof(Matrix4X4<float>), ProjectionMatrix);
        SetUniformBufferMatrix(ref offset, sizeof(Matrix4X4<float>), ViewProjectionMatrix);
        Matrix4X4.Invert(ViewMatrix, out var viewInverted);
        SetUniformBufferVec3(ref offset, sizeof(Vector3), viewInverted.Row4.ToSystem().ToVec3());
        GL.BindBufferBase(BufferTargetARB.UniformBuffer, 0, _globalUniformBuffer);
        GL.BindBuffer(BufferTargetARB.UniformBuffer, 0);
    }

    private unsafe void SetUniformBufferMatrix(ref int offset, int size, Matrix4X4<float> value)
    {
        GL.BufferSubData(BufferTargetARB.UniformBuffer, (nint)offset, (uint)size, (float*)&value);
        offset += size;
    }

    private unsafe void SetUniformBufferVec3(ref int offset, int size, Vector3 vec)
    {
        GL.BufferSubData(BufferTargetARB.UniformBuffer, (nint)offset, (uint)size, (float*)&vec);
        offset += size;
    }

    private Shader GetShader(string path, ShaderFlags flags)
    {
        // ignore streaming tex flag here as it does not actually affect the shaders, only the materials
        flags = flags & (~ShaderFlags.EnableStreamingTex);
        if (shaders.TryGetValue((path, flags), out var shader)) {
            return shader;
        }

        return shaders[(path, flags)] = shader = new Shader(GL, Path.Combine(AppContext.BaseDirectory, path), flags);
    }

    public override MeshHandle CreateBlankMesh()
    {
        var innerHandle = new MeshResourceHandle(MeshRefs.NextInstanceID);
        var handle = new MeshHandle(innerHandle);
        MeshRefs.AddUnnamed(innerHandle);
        return handle;
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
        var meshResource = fileHandle.Resource as CommonMeshResource ?? fileHandle.GetCustomContent<CommonMeshResource>();
        if (meshResource == null) return null;

        var handle = new MeshResourceHandle(MeshRefs.NextInstanceID);
        var meshFile = meshResource.NativeMesh;
        if (meshFile.MeshData == null || meshResource.PreloadedMeshes == null && !(meshFile.MeshBuffer?.Positions.Length > 0) || meshFile.MeshData.LODs.Count == 0) {
            // TODO occluder meshes
            return handle;
        }

        var mainLod = meshFile.MeshData.LODs[0];
        MeshBoneHierarchy? boneList = null;
        if (meshFile.BoneData?.Bones.Count > 0) {
            boneList = meshFile.BoneData;
        }

        var meshlist = meshResource.PreloadedMeshes;
        if (meshlist == null) {
            meshlist = new();
            foreach (var group in mainLod.MeshGroups) {
                foreach (var sub in group.Submeshes) {
                    var newMesh = new TriangleMesh(GL, meshFile, sub);
                    newMesh.MeshGroup = group.groupId;
                    meshlist.Add(newMesh);
                }
            }
        }

        int meshIdx = 0;
        foreach (var group in mainLod.MeshGroups) {
            foreach (var sub in group.Submeshes) {
                var newMesh = meshlist[meshIdx++];
                newMesh.Initialize(GL);
                handle.SetMaterialName(handle.Meshes.Count, meshFile.MaterialNames[sub.materialIndex]);
                handle.Bones = boneList;
                handle.Meshes.Add(newMesh);
            }
        }

        return handle;
    }

    protected override MeshHandle CreateMeshInstanceHandle(MeshResourceHandle resource, FileHandle file)
    {
        if (file.Loader is McolFileLoader) {
            return new McolMeshHandle(GL, resource, file.GetFile<McolFile>());
        } else if (file.Loader is RcolFileLoader) {
            return new RcolMeshHandle(GL, resource, file.GetFile<RcolFile>());
        } else if (file.Loader is TerrFileLoader) {
            return new McolMeshHandle(GL, resource, file.GetFile<TerrFile>());
        } else if (resource.Animatable) {
            return new AnimatedMeshHandle(resource);
        } else {
            return new MeshHandle(resource);
        }
    }

    private Texture LoadTexture(Assimp.Scene? scene, Assimp.TextureSlot texture, ShaderFlags flags)
    {
        try {
            if (texture.FilePath.StartsWith('*')) {
                var texData = scene?.GetEmbeddedTexture(texture.FilePath);
                if (texData == null) return GetDefaultTexture();

                var tex = new Texture(GL);
                if (texData.HasCompressedData) {
                    var stream = new MemoryStream(texData.CompressedData);
                    tex.LoadFromStream(stream);
                } else {
                    var bytes = Unsafe.As<byte[]>(texData.NonCompressedData);
                    tex.LoadFromRawData(bytes, (uint)texData.Width, (uint)texData.Height);
                }
                TextureRefs.AddUnnamed(tex);
                return tex;
            }

            if (flags.HasFlag(ShaderFlags.EnableStreamingTex)) {
                string streamingPath = Path.Combine("streaming/", texture.FilePath);
                var streamingTex = LoadTextureInternal(streamingPath);
                if (streamingTex != null) return streamingTex;
            }

            return LoadTextureInternal(texture.FilePath) ?? GetMissingTexture();
        } catch (Exception e) {
            Logger.Error("Failed to load texture " + texture.FilePath + ": " + e.Message);
            return GetMissingTexture();
        }
    }

    private Texture? LoadTextureInternal(string filepath)
    {
        var filehash = PakUtils.GetFilepathHash(filepath);
        if (TextureRefs.TryAddReference(filehash, out var handle)) {
            return handle.Resource;
        } else if (ResourceManager.TryResolveFile(filepath, out var texHandle)) {
            var tex = new Texture(GL).LoadFromFile(texHandle);
            if (texHandle.Filepath != filepath) {
                var srcHash = filehash;
                filehash = PakUtils.GetFilepathHash(texHandle.Filepath);
                TextureRefs.AddKeyRemap(srcHash, filehash);
            }
            TextureRefs.Add(filehash, tex);
            return tex;
        } else {
            return null;
        }
    }

    public override MaterialGroup LoadMaterialGroup(FileHandle file, ShaderFlags flags = ShaderFlags.None)
    {
        if (MaterialRefs.TryAddReference((file, flags), out var groupRef)) {
            AddMaterialTextureReferences(groupRef.Resource);
            return groupRef.Resource;
        }

        var group = new MaterialGroup() { Flags = flags };
        var scene = (file.Resource as AssimpMaterialResource)?.Scene;
        var materials = scene?.Materials ?? (file.GetCustomContent<CommonMeshResource>())?.MaterialList;
        if (materials == null) {
            Logger.Error("Failed to load material group " + file.Filepath);
            var material = CreateViewShadedMaterial(flags);
            group.Add(material);
            material.SetParameter(TextureUnit.Texture0, GetDefaultTexture());
            MaterialRefs.Add((file, flags), group);
            AddMaterialTextureReferences(material);
            return group;
        }

        foreach (var mat in materials) {
            if (!mat.HasName) {
                Logger.Debug("Material " + materials.IndexOf(mat) + " has no name");
                continue;
            }

            var material = CreateViewShadedMaterial(flags);
            //var material = CreateWireMaterial(flags);
            material.name = mat.Name;
            if (material.HasTextureParameter(TextureUnit.Texture0)) {
                if (mat.HasTextureDiffuse) {
                    var tex = LoadTexture(scene, mat.TextureDiffuse, flags);
                    material.SetParameter(TextureUnit.Texture0, tex);
                } else {
                    material.SetParameter(TextureUnit.Texture0, GetDefaultTexture());
                }
            }
            group.Add(material);
        }

        MaterialRefs.Add((file, flags), group);
        return group;
    }

    public override unsafe void RenderSimple(MeshHandle handle, in Matrix4X4<float> transform)
    {
        for (int i = 0; i < handle.Handle.Meshes.Count; i++) {
            var mesh = handle.Handle.Meshes[i];
            if (!handle.GetMeshPartEnabled(mesh.MeshGroup)) continue;

            var material = handle.GetMaterial(i);

            Batch.Simple.Add(new NormalRenderBatchItem(material, mesh, transform, handle));
        }
    }

    public override void RenderInstanced(MeshHandle handle, List<Matrix4X4<float>> transforms)
    {
        for (int i = 0; i < handle.Handle.Meshes.Count; i++) {
            var mesh = handle.Handle.Meshes[i];
            if (!handle.GetMeshPartEnabled(mesh.MeshGroup)) continue;

            var material = handle.GetMaterial(i);

            Batch.Instanced.Add(new InstancedRenderBatchItem(material, mesh, transforms));
        }
    }

    public override void ExecuteRender()
    {
        Batch.Render(this);
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
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(blend.BlendModeSrc, blend.BlendModeDest);
        } else  {
            GL.Disable(EnableCap.Blend);
        }

        if (material.DisableDepth) {
            GL.Disable(EnableCap.DepthTest);
        } else {
            GL.Enable(EnableCap.DepthTest);
        }
    }

    internal override void BeforeRender()
    {
        ResetBlendingSettings();

        if (_renderTargetTextureSizeOutdated) {
            UpdateRenderTarget();
        }

        if (_outputBuffer != 0) {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _outputBuffer);
            GL.ClearColor(0, 0, 0, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Viewport(new System.Drawing.Size((int)ViewportSize.X, (int)ViewportSize.Y));
        }
        base.BeforeRender();
        ApplyGlobalUniforms();

        ResetBlendingSettings();
    }

    private void ResetBlendingSettings()
    {
        GL.Enable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);
    }

    internal override void AfterRender()
    {
        RenderGizmos();
        if (_outputBuffer != 0) {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(new System.Drawing.Size((int)ScreenSize.X, (int)ScreenSize.Y));
        }
    }

    public override void AddDefaultSceneGizmos()
    {
        Gizmos.Add(new GridGizmo(GL));
        Gizmos.Add(new AxisGizmo(GL));
        Gizmos.Add(new SelectionBoundsGizmo(GL));
    }

    private void RenderGizmos()
    {
        if (AppConfig.Instance.RenderAxis.Get()) {
            if (!_hasInitGizmos && Gizmos.Count > 0) {
                foreach (var gizmo in Gizmos) gizmo.Init(this);
                _hasInitGizmos = true;
            }
            foreach (var gizmo in Gizmos) {
                gizmo.Update(this, 0);
            }
            foreach (var gizmo in Gizmos) gizmo.Render(this);
            Batch.Gizmo.Render(this);
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
        GL.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb, (uint)ViewportSize.X, (uint)ViewportSize.Y, 0, PixelFormat.Rgb, PixelType.UnsignedByte, null);
        uint nearest = (uint)GLEnum.Nearest;
        GL.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, in nearest);
        GL.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, in nearest);

        _outputTexDepthBuffer = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _outputTexDepthBuffer);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent, (uint)ViewportSize.X, (uint)ViewportSize.Y);
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

    private static readonly ulong MissingTextureHash = PakUtils.GetFilepathHash("__missing");
    private Texture GetMissingTexture()
    {
        if (_missingTexture == null) {
            _missingTexture = new Texture(GL);
            _missingTexture.LoadFromRawData(DefaultPink, 4, 4);
            _missingTexture.Path = "__missing";
            TextureRefs.Add(MissingTextureHash, _missingTexture);
        }

        TextureRefs.TryAddReference(MissingTextureHash, out _);
        return _missingTexture;
    }

    private static readonly ulong DefaultTextureHash = PakUtils.GetFilepathHash("__default");
    private Texture GetDefaultTexture()
    {
        if (_defaultTexture == null) {
            _defaultTexture = new Texture(GL);
            _defaultTexture.LoadFromRawData(DefaultWhite, 4, 4);
            _defaultTexture.Path = "__default";
            TextureRefs.Add(DefaultTextureHash, _defaultTexture);
        }

        TextureRefs.TryAddReference(DefaultTextureHash, out _);
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
        Batch.Dispose();
        base.Dispose(disposing);
    }
}
