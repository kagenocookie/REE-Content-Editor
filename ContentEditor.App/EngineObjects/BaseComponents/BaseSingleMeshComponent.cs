using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentEditor.App;

public abstract class BaseSingleMeshComponent(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data), IScenePickableComponent
{
    protected MeshHandle? mesh;
    protected MaterialGroup? material;

    public override AABB LocalBounds => mesh?.BoundingBox ?? default;

    public bool HasMesh => mesh?.Meshes.Any() == true;

    internal override void OnActivate()
    {
        base.OnActivate();

        if (!AppConfig.Instance.RenderMeshes.Get()) return;
        UnloadMesh();
        RefreshMesh();
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        UnloadMesh();
    }

    protected abstract void RefreshMesh();

    public void SetMesh(string meshFilepath, string? materialFilepath)
    {
        UnloadMesh();
        // note - when loading material groups from the mesh file, we receive a placeholder material with just a default shader and white texture
        material = string.IsNullOrEmpty(materialFilepath)
            ? Scene!.RenderContext.LoadMaterialGroup(meshFilepath)
            : Scene!.RenderContext.LoadMaterialGroup(materialFilepath);
        mesh = Scene.RenderContext.LoadMesh(meshFilepath);
        if (mesh != null && material != null) {
            Scene.RenderContext.SetMeshMaterial(mesh, material);
        }
    }

    protected void UnloadMesh()
    {
        if (mesh == null || Scene == null) return;

        if (mesh != null) {
            // TODO: would we rather just store the render context inside mesh refs / material groups, and have them be IDispoable?
            Scene.RenderContext.UnloadMesh(mesh);
            mesh = null;
        }
        if (material != null) {
            Scene.RenderContext.UnloadMaterialGroup(material);
            material = null;
        }
    }

    protected virtual bool IsMeshUpToDate() => true;

    internal override unsafe void Render(RenderContext context)
    {
        // TODO - this may be better handled on the level of scene + component grouping instead of inside individual components
        var render = AppConfig.Instance.RenderMeshes.Get();
        if (!render) {
            return;
        }
        if (mesh == null || !IsMeshUpToDate()) {
            RefreshMesh();
        }
        if (mesh != null) {
            ref readonly var transform = ref GameObject.Transform.WorldTransform;
            context.RenderSimple(mesh, transform);
        }
    }

    public void CollectPickables(PickableData data)
    {
        if (mesh == null || !AppConfig.Instance.RenderMeshes.Get()) return;

        data.TryAdd(this, 0, mesh, Transform.WorldTransform, WorldSpaceBounds);
    }

    protected bool LoadMeshFromPrefab(string prefab)
    {
        if (string.IsNullOrEmpty(prefab)) return false;

        if (Scene!.Workspace.ResourceManager.TryResolveGameFile(prefab, out var handle)) {
            var pfb = handle.GetFile<PfbFile>();
            var meshComp = pfb.IterAllGameObjects(true)
                .SelectMany(go => go.Components.Where(comp => comp.RszClass.name == MeshComponent.Classname))
                .FirstOrDefault();
            if (meshComp == null) return false;

            var mesh = RszFieldCache.Mesh.Resource.Get(meshComp);
            if (string.IsNullOrEmpty(mesh)) return false;

            var mat = RszFieldCache.Mesh.Material.Get(meshComp);
            SetMesh(mesh, mat);
            return true;
        }

        return false;
    }
}
