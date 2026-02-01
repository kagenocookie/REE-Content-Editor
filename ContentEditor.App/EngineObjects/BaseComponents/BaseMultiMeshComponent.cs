using ContentEditor.App.Graphics;
using ReeLib;
using ReeLib.via;

namespace ContentEditor.App;

public abstract class BaseMultiMeshComponent(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data)
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
}
