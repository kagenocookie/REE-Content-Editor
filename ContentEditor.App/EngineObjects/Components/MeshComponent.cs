using System.Buffers;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("via.render.Mesh")]
public class MeshComponent(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data), IFixedClassnameComponent, IConstructorComponent
{
    public static new string Classname => "via.render.Mesh";

    private MeshHandle? mesh;
    private MaterialGroup? material;

    public override AABB LocalBounds => mesh?.BoundingBox ?? default;

    public bool HasMesh => mesh?.Meshes.Any() == true;

    public void ComponentInit()
    {
        RszFieldCache.Mesh.PartsEnable.Set(Data, Enumerable.Range(0, 256).Select(_ => (object)true).ToList());
    }

    internal override void OnActivate()
    {
        base.OnActivate();

        RefreshIfActive();
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        UnloadMesh();
    }

    public void RefreshIfActive()
    {
        if (Scene?.IsActive != true || !AppConfig.Instance.RenderMeshes.Get()) return;

        RefreshMesh();
    }

    private void RefreshMesh()
    {
        UnloadMesh();
        var meshPath = RszFieldCache.Mesh.Resource.Get(Data);
        if (!string.IsNullOrEmpty(meshPath)) {
            SetMesh(meshPath, RszFieldCache.Mesh.Material.Get(Data));
        }
    }

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
        RszFieldCache.Mesh.Resource.Set(Data, meshFilepath);
        RszFieldCache.Mesh.Material.Set(Data, materialFilepath ?? string.Empty);

        if (mesh != null) {
            var parts = RszFieldCache.Mesh.PartsEnable.Get(Data);
            for (int i = 0; i < parts.Count; ++i) {
                var enabled = (bool)parts[i];
                mesh.SetMeshPartEnabled(i, enabled);
            }
        }
    }

    public void SetMesh(FileHandle meshFile, FileHandle? materialFile)
    {
        UnloadMesh();
        material = Scene!.RenderContext.LoadMaterialGroup(materialFile ?? meshFile);
        mesh = Scene.RenderContext.LoadMesh(meshFile);
        if (mesh != null) {
            Scene.RenderContext.SetMeshMaterial(mesh, material);
        }
        RszFieldCache.Mesh.Resource.Set(Data, meshFile.InternalPath ?? meshFile.Filepath ?? string.Empty);
        RszFieldCache.Mesh.Material.Set(Data, materialFile?.InternalPath ?? materialFile?.Filepath ?? string.Empty);
    }

    private void UnloadMesh()
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

    internal override unsafe void Render(RenderContext context)
    {
        // TODO - this may be better handled on the level of scene + component grouping instead of inside individual components
        var render = AppConfig.Instance.RenderMeshes.Get();
        if (!render) {
            return;
        }
        if (mesh == null) {
            RefreshMesh();
        }
        if (mesh != null) {
            ref readonly var transform = ref GameObject.Transform.WorldTransform;
            context.RenderSimple(mesh, transform);
        }
    }
}
