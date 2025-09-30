using System.Numerics;
using System.Runtime.CompilerServices;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Mesh;
using ReeLib.via;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using VYaml.Serialization;

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

    private MeshHandle? axisMesh;
    private MeshHandle? gridMesh;

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

    private Shader GetShader(string path, ShaderFlags flags)
    {
        // ignore streaming tex flag here as it does not actually affect the shaders, only the materials
        flags = flags & (~ShaderFlags.EnableStreamingTex);
        if (shaders.TryGetValue((path, flags), out var shader)) {
            return shader;
        }

        return shaders[(path, flags)] = shader = new Shader(GL, Path.Combine(AppContext.BaseDirectory, path), flags);
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
        var meshFile = meshResource.NativeMesh;
        MeshBoneHierarchy? boneList = null;
        if (meshFile.BoneData?.Bones.Count > 0) {
            boneList = meshFile.BoneData;
        }
        foreach (var mesh in meshFile.Meshes) {
            foreach (var group in mesh.LODs[0].MeshGroups) {
                foreach (var sub in group.Submeshes) {
                    var newMesh = new TriangleMesh(GL, meshFile, sub);
                    newMesh.MeshGroup = group.groupId;
                    handle.SetMaterialName(handle.Meshes.Count, meshFile.MaterialNames[sub.materialIndex]);
                    handle.Bones = boneList;
                    handle.Meshes.Add(newMesh);
                }
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
        } else if (resource.Animatable) {
            return new AnimatedMeshHandle(GL, resource);
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

            return LoadTextureInternal(texture.FilePath) ?? GetMissingTexture();;
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
        var materials = scene?.Materials ?? (file.Resource as AssimpMeshResource)?.MaterialList;
        if (materials == null) {
            Logger.Error("Failed to load material group - invalid resource type " + file.Filepath);
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


    private MeshHandle? lastInstancedMesh;
    private Material? lastMaterial;
    public override unsafe void RenderSimple(MeshHandle handle, in Matrix4X4<float> transform)
    {
        for (int i = 0; i < handle.Handle.Meshes.Count; i++) {
            var mesh = handle.Handle.Meshes[i];
            if (!handle.GetMeshPartEnabled(mesh.MeshGroup)) continue;

            var material = handle.GetMaterial(i);

            // TODO frustum culling
            // var bounds = sub.mesh.BoundingBox;

            mesh.Bind();
            if (lastMaterial != material) {
                lastMaterial = material;
                material.Bind();
                BindMaterialFlags(material);
            }
            material.Shader.SetUniform("uModel", transform);
            handle.BindForRender(material, i);

            GL.DrawArrays(mesh.MeshType, 0, (uint)mesh.Indices.Length);
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
        lastMaterial = null;
        lastInstancedMesh = null;

        ResetBlendingSettings();

        axisMesh ??= CreateAxis();
        gridMesh ??= CreateGrid();

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

        if (AppConfig.Instance.RenderAxis.Get()) {
            RenderSimple(axisMesh, Matrix4X4<float>.Identity);

            Vector3D<float> campos;
            if (Matrix4X4.Invert(ViewMatrix, out var inverted)) {
                campos = inverted.Column4.ToSystem().ToVec3().ToGeneric() with { Y = 0 };
            } else {
                campos = new();
            }
            campos.X = MathF.Round(campos.X / GridCellSpacing) * GridCellSpacing;
            campos.Z = MathF.Round(campos.Z / GridCellSpacing) * GridCellSpacing;
            var gridMatrix = Matrix4X4.CreateTranslation<float>(campos);
            RenderSimple(gridMesh, gridMatrix);
        }
        ResetBlendingSettings();
    }

    private void ResetBlendingSettings()
    {
        GL.Enable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);
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

    private Shader MonoShader => GetShader("Shaders/GLSL/unshaded-color.glsl", ShaderFlags.None);

    private MeshHandle CreateAxis()
    {
        var matGroup = new MaterialGroup();
        var mat = new Material(GL, MonoShader, "x");
        mat.SetParameter("_MainColor", new Color(255, 0, 0, 128));
        mat.BlendMode = new MaterialBlendMode(true, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        matGroup.Add(mat);

        mat = new Material(GL, MonoShader, "y");
        mat.SetParameter("_MainColor", new Color(0, 255, 0, 128));
        mat.BlendMode = new MaterialBlendMode(true, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        matGroup.Add(mat);

        mat = new Material(GL, MonoShader, "z");
        mat.SetParameter("_MainColor", new Color(0, 0, 255, 128));
        mat.BlendMode = new MaterialBlendMode(true, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        matGroup.Add(mat);

        var handle = new MeshResourceHandle(MeshRefs.NextInstanceID);
        axisMesh = new MeshHandle(handle);
        handle.Meshes.Add(new LineMesh(GL, new System.Numerics.Vector3(-10000, 0, 0), new Vector3(10000, 0, 0)) { MeshType = PrimitiveType.Lines });
        handle.Meshes.Add(new LineMesh(GL, new System.Numerics.Vector3(0, -10000, 0), new Vector3(0, 10000, 0)) { MeshType = PrimitiveType.Lines });
        handle.Meshes.Add(new LineMesh(GL, new System.Numerics.Vector3(0, 0, -10000), new Vector3(0, 0, 10000)) { MeshType = PrimitiveType.Lines });
        axisMesh.SetMaterials(matGroup, [0, 1, 2]);

        MaterialRefs.AddUnnamed(matGroup);
        MeshRefs.AddUnnamed(handle);
        return axisMesh;
    }

    private MeshHandle CreateGrid()
    {
        var matGroup = new MaterialGroup();
        var mat = new Material(GL, MonoShader, "gray");
        mat.SetParameter("_MainColor", new Color(100, 100, 100, 64));
        mat.BlendMode = new MaterialBlendMode(true, BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        matGroup.Add(mat);

        var handle = new MeshResourceHandle(MeshRefs.NextInstanceID);
        gridMesh = new MeshHandle(handle);
        var lineCount = 100;
        var gridSpan = lineCount * GridCellSpacing;
        var lines = new List<Vector3>((lineCount + 1) * 2 * 2);
        for (int x = -lineCount; x <= lineCount; x++) {
            lines.Add(new Vector3(x * GridCellSpacing, 0, -gridSpan));
            lines.Add(new Vector3(x * GridCellSpacing, 0, gridSpan));
        }
        for (int z = -lineCount; z <= lineCount; z++) {
            lines.Add(new Vector3(-gridSpan, 0, z * GridCellSpacing));
            lines.Add(new Vector3(gridSpan, 0, z * GridCellSpacing));
        }
        handle.Meshes.Add(new LineMesh(GL, lines.ToArray()) { MeshType = PrimitiveType.Lines });
        gridMesh.SetMaterials(matGroup, [0]);

        MaterialRefs.AddUnnamed(matGroup);
        MeshRefs.AddUnnamed(handle);
        return gridMesh;
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
