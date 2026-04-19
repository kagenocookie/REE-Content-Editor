using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentEditor.App;

public abstract class BaseMultiMeshComponent(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data), IScenePickableComponent
{
    protected readonly List<MeshHandle> meshes = new();
    protected readonly List<MaterialGroup> materials = new();

    public override AABB LocalBounds => AABB.Combine(meshes.Select(m => m.BoundingBox));

    public bool HasMesh => meshes.Count > 0;

    internal override void OnActivate()
    {
        base.OnActivate();

        if (!AppConfig.Instance.RenderMeshes.Get()) return;
        UnloadMeshes();
        RefreshMesh();
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        UnloadMeshes();
    }

    protected abstract void RefreshMesh();

    protected MeshHandle? AddMesh(string meshFilepath, string? materialFilepath)
    {
        var material = string.IsNullOrEmpty(materialFilepath)
            ? Scene!.RenderContext.LoadMaterialGroup(meshFilepath)
            : Scene!.RenderContext.LoadMaterialGroup(materialFilepath);
        var mesh = Scene.RenderContext.LoadMesh(meshFilepath);

        if (mesh != null) meshes.Add(mesh);
        if (material != null) materials.Add(material);

        if (mesh != null && material != null) {
            Scene.RenderContext.SetMeshMaterial(mesh, material);
        }
        return mesh;
    }

    protected bool AddMeshesFromPrefab(string prefabFilepath)
    {
        var foundAny = false;
        if (Scene?.Workspace.ResourceManager.TryResolveGameFile(prefabFilepath, out var pfbHandle) == true) {
            var pfb = pfbHandle.GetFile<PfbFile>();
            if (pfb.Root == null) return false;

            foreach (var meshComp in pfb.IterAllGameObjects(true).SelectMany(c => c.Components.Where(cc => cc.RszClass.name == "via.render.Mesh"))) {
                if (meshComp?.Get(RszFieldCache.Mesh.Resource) is string meshPath && !string.IsNullOrEmpty(meshPath)) {
                    var enabledParts = meshComp?.Get(RszFieldCache.Mesh.PartsEnable).Cast<bool>();
                    var mdf = meshComp?.Get(RszFieldCache.Mesh.Material);
                    var mesh = AddMesh(meshPath, mdf);
                    if (enabledParts != null && mesh != null) {
                        mesh.SetPartsEnabled(enabledParts);
                    }
                    foundAny |= mesh != null;
                }
            }
        }
        return foundAny;
    }

    protected void UnloadMeshes()
    {
        if (Scene == null) return;
        foreach (var mesh in meshes) {
            Scene.RenderContext.UnloadMesh(mesh);
        }
        meshes.Clear();
        foreach (var mat in materials) {
            Scene.RenderContext.UnloadMaterialGroup(mat);
        }
        materials.Clear();
    }

    protected virtual bool IsMeshUpToDate() => true;

    internal override unsafe void Render(RenderContext context)
    {
        // TODO - this may be better handled on the level of scene + component grouping instead of inside individual components
        var render = AppConfig.Instance.RenderMeshes.Get();
        if (!render) {
            return;
        }
        if (meshes.Count == 0 || !IsMeshUpToDate()) {
            RefreshMesh();
        }
        if (meshes.Count != 0) {
            ref readonly var transform = ref GameObject.Transform.WorldTransform;
            foreach (var mesh in meshes) {
                context.RenderSimple(mesh, transform);
            }
        }
    }

    public void CollectPickables(PickableData data)
    {
        if (!AppConfig.Instance.RenderMeshes.Get()) return;

        foreach (var mesh in meshes) {
            data.TryAdd(this, 0, mesh, Transform.WorldTransform, WorldSpaceBounds);
        }
    }
}
