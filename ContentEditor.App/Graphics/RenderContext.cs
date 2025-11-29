using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using ContentEditor.Editor;
using ContentPatcher;
using ReeLib;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App.Graphics;

public abstract class RenderContext : IDisposable, IFileHandleReferenceHolder
{
    public float DeltaTime { get; internal set; }
    internal Matrix4X4<float> ViewMatrix { get; set; } = Matrix4X4<float>.Identity;
    internal Matrix4X4<float> ProjectionMatrix { get; set; } = Matrix4X4<float>.Identity;
    internal Matrix4X4<float> ViewProjectionMatrix { get; set; } = Matrix4X4<float>.Identity;

    protected Material? defaultMaterial;

    protected readonly ResourceRefCounter<ulong, Texture> TextureRefs = new();
    protected readonly ResourceRefCounter<FileHandle, MeshResourceHandle> MeshRefs = new();
    protected readonly ResourceRefCounter<(FileHandle, ShaderFlags), MaterialGroup> MaterialRefs = new();

    protected readonly List<Gizmo> Gizmos = new();

    protected ResourceManager _resourceManager = null!;
    internal ResourceManager ResourceManager {
        get => _resourceManager;
        set => UpdateResourceManager(value);
    }

    /// <summary>The full renderable screen area size.</summary>
    public Vector2 ScreenSize { get; set; }

    /// <summary>The offset of the viewport's top left edge relative to the full screen area.</summary>
    public Vector2 ViewportOffset { get; set; }

    /// <summary>The additional offset from the viewport to the UI-enabled area.</summary>
    public Vector2 UIOffset { get; set; }

    /// <summary>The render output size of this render context's viewport.</summary>
    public Vector2 ViewportSize { get; set; }

    protected bool _renderTargetTextureSizeOutdated;

    protected uint _outputTexture;
    public uint RenderTargetTextureHandle => _outputTexture;

    protected Dictionary<IResourceFile, (FileHandle handle, int refcount)> _loadedResources = new();

    bool IFileHandleReferenceHolder.CanClose => true;
    IRectWindow? IFileHandleReferenceHolder.Parent => null;

    public Color ClearColor { get; internal set; }

    internal virtual void BeforeRender()
    {
    }

    internal virtual void AfterRender()
    {
    }

    /// <summary>
    /// Render a simple mesh (static, single mesh with no animation)
    /// </summary>
    public abstract void RenderSimple(MeshHandle handle, in Matrix4X4<float> transform);
    public abstract void RenderInstanced(MeshHandle handle, List<Matrix4X4<float>> transforms);

    public abstract MaterialGroup LoadMaterialGroup(FileHandle file, ShaderFlags flags = ShaderFlags.None);
    public abstract Material GetBuiltInMaterial(BuiltInMaterials material, ShaderFlags flags = ShaderFlags.None);
    public abstract IEnumerable<Material> GetPresetMaterials(EditorPresetMaterials preset);

    public abstract MeshHandle CreateBlankMesh();
    public abstract (MeshHandle, ShapeMesh) CreateShapeMesh();
    protected abstract MeshResourceHandle? LoadMeshResource(FileHandle fileHandle);

    public void AddSceneGizmo(Gizmo gizmo) => Gizmos.Add(gizmo);
    public virtual void AddDefaultSceneGizmos() { }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Dispose(true);
    }

    protected virtual void Dispose(bool disposing)
    {
        TextureRefs.Dispose();
        MeshRefs.Dispose();
    }

    protected virtual void UpdateResourceManager(ResourceManager manager)
    {
        _resourceManager = manager;
        MeshRefs.Dispose();
        TextureRefs.Dispose();
    }

    private IResourceFile? LoadResource(string path)
    {
        if (!ResourceManager.TryResolveGameFile(path, out var fileHandle)) {
            Logger.Error("Failed to load resource " + path);
            return null;
        }

        var resource = fileHandle.Resource;
        if (_loadedResources.TryGetValue(resource, out var handleRefs)) {
            _loadedResources[resource] = (handleRefs.handle, handleRefs.refcount + 1);
        } else {
            _loadedResources[resource] = (fileHandle, 1);
            fileHandle.References.Add(this);
        }

        return resource;
    }

    internal void UnloadResource(IResourceFile resource)
    {
        if (_loadedResources.Remove(resource, out var handleRefs)) {
            if (handleRefs.refcount == 1) {
                handleRefs.handle.References.Remove(this);
            } else {
                _loadedResources[resource] = (handleRefs.handle, handleRefs.refcount - 1);
            }
        }
    }

    public void SetRenderToTexture(Vector2 textureSize = new())
    {
        if (textureSize == ViewportSize) return;
        if (textureSize.X < 1 || textureSize.Y < 1) {
            throw new Exception("Invalid negative render texture size");
        }
        _renderTargetTextureSizeOutdated = true;
        ViewportSize = textureSize;
    }

    public MeshHandle? LoadMesh(string mesh)
    {
        if (ResourceManager.TryResolveGameFile(mesh, out var handle)) {
            return LoadMesh(handle);
        }
        return null;
    }

    public MeshHandle? LoadMesh(FileHandle file)
    {
        MeshResourceHandle resource;
        if (MeshRefs.TryAddReference(file, out var handleRef)) {
            resource = handleRef.Resource;
        } else {
            resource = LoadMeshResource(file)!;
            if (resource == null) {
                // assume the caller knows what they're doing - might be a custom mesh-like file (rcol)
                resource = new MeshResourceHandle(MeshRefs.NextInstanceID);
            }
            MeshRefs.Add(file, resource);
        }

        var mesh = CreateMeshInstanceHandle(resource, file);
        SetMeshMaterial(mesh, mesh.Material);
        return mesh;
    }

    public abstract void StoreMesh(MeshResourceHandle mesh);

    protected virtual MeshHandle CreateMeshInstanceHandle(MeshResourceHandle resource, FileHandle file) => new MeshHandle(resource);

    public void UnloadMesh(MeshHandle handle)
    {
        MeshRefs.Dereference(handle.Handle);
    }

    public MaterialGroup? LoadMaterialGroup(string materialPath, ShaderFlags flags = ShaderFlags.None)
    {
        if (ResourceManager.TryResolveGameFile(materialPath, out var handle)) {
            return LoadMaterialGroup(handle, flags);
        }
        return null;
    }

    public void UnloadMaterialGroup(MaterialGroup material)
    {
        MaterialRefs.Dereference(material);
        foreach (var m in material.Materials) {
            foreach (var tex in m.Textures) {
                TextureRefs.Dereference(tex);
            }
        }
    }

    public void SetMeshMaterial(MeshHandle mesh, MaterialGroup material)
    {
        var handle = mesh.Handle;
        var matIndices = new List<int>();
        foreach (var srcMesh in handle.Meshes) {
            var matName = handle.GetMaterialName(matIndices.Count);
            var target = material.GetByName(matName);
            if (target != null) {
                matIndices.Add(material.GetMaterialIndex(target));
            } else {
                if (material.Materials.Count == 0) {
                    material.Materials.Add(GetPresetMaterials(EditorPresetMaterials.Default).First());
                    material.Materials[0].name = matName;
                }
                matIndices.Add(0);
            }
        }

        mesh.SetMaterials(material, matIndices);
    }

    protected void AddMaterialTextureReferences(MaterialGroup matGroup)
    {
        foreach (var mat in matGroup.Materials) {
            AddMaterialTextureReferences(mat);
        }
    }

    protected void AddMaterialTextureReferences(Material mat)
    {
        foreach (var tex in mat.Textures) {
            if (tex == null) continue;
            if (string.IsNullOrEmpty(tex.Path)) {
                TextureRefs.AddUnnamed(tex);
            } else {
                TextureRefs.Add(PakUtils.GetFilepathHash(tex.Path), tex);
            }
        }
    }

    protected sealed class ResourceContainer<TSource, T> where T : class where TSource : class
    {
        private List<T> Objects = new();
        private SortedSet<int> GapIndices = new();
        private Dictionary<TSource, int> ResourceCache = new();

        public IEnumerable<T> Instances => Objects.Where(o => o != null);

        public T this[int index] => Objects[index - 1];

        public int PeekNextId() => GapIndices.Count != 0 ? GapIndices.First() : Objects.Count + 1;

        public int Add(TSource source, T item)
        {
            if (ResourceCache.TryGetValue(source, out var prev)) {
                return prev;
            }
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

        public bool TryGet(TSource item, [MaybeNullWhen(false)] out T data, out int index)
            => ResourceCache.TryGetValue(item, out index) ? (data = Objects[index]) != null : (data = null) != null;

        public T Remove(int itemIndex)
        {
            var obj = Objects[itemIndex - 1];
            GapIndices.Add(itemIndex);
            (obj as IDisposable)?.Dispose();
            Objects[itemIndex - 1] = null!;
            return obj;
        }

        public void Remove(T instance)
        {
            // TODO optimize resource removal by instance
            var index = Objects.IndexOf(instance);
            GapIndices.Add(index + 1);
            Objects[index] = null!;
            (instance as IDisposable)?.Dispose();
        }

        public void Clear()
        {
            ResourceCache.Clear();
            GapIndices.Clear();
            Objects.Clear();
        }

        public int GetIndex(T obj) => Objects.IndexOf(obj) + 1;
    }

    void IFileHandleReferenceHolder.Close() { }

    public abstract void ExecuteRender();
}
