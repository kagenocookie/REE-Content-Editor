using System.Buffers;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

public abstract class BaseSingleMeshComponent(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data)
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
}
