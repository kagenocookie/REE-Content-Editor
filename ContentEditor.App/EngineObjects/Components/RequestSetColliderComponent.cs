using System.Buffers;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.UVar;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("via.physics.RequestSetCollider")]
public class RequestSetColliderComponent(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data), IFixedClassnameComponent
{
    static string IFixedClassnameComponent.Classname => "via.physics.RequestSetCollider";

    private MaterialGroup? material;

    public List<RcolMeshHandle> Meshes { get; } = new();

    public override AABB LocalBounds => Meshes.Count == 0 ? default : AABB.Combine(Meshes.Select(m => m.BoundingBox));

    public bool HasMesh => Meshes.Count != 0;

    internal override void OnActivate()
    {
        base.OnActivate();

        if (!AppConfig.Instance.RenderRequestSetColliders.Get()) return;
        ReloadMeshes();
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        UnloadMeshes();
    }

    public void ReloadMeshes()
    {
        UnloadMeshes();

        var rcols = RszFieldCache.RequestSetCollider.RequestSetGroups.Get(Data);
        if (rcols == null || rcols.Count == 0) {
            return;
        }

        var ctx = Scene!.RenderContext;

        material ??= ctx.GetPresetMaterialGroup(EditorPresetMaterials.Wireframe);
        foreach (var mat in material.Materials) {
            mat.SetParameter("_InnerColor", Color.FromVector4(Colors.RequestSetColliders));
        }

        foreach (var group in rcols.OfType<RszInstance>()) {
            var rcolPath = RszFieldCache.RequestSetGroup.Resource.Get(group);
            if (string.IsNullOrEmpty(rcolPath)) continue;

            if (!Scene!.Workspace.ResourceManager.TryResolveFile(rcolPath, out var handle)) {
                Logger.Info("Failed to resolve rcol file " + rcolPath);
                continue;
            }

            var rcol = (RcolMeshHandle?)ctx.LoadMesh(handle);
            if (rcol == null) continue;
            rcol.Update();
            rcol.SetMaterials(material, [0]);
            Meshes.Add(rcol);
        }
    }

    private void UnloadMeshes()
    {
        foreach (var mesh in Meshes) {
            Scene!.RenderContext.UnloadMesh(mesh);
        }
        Meshes.Clear();
        if (material != null) {
            Scene!.RenderContext.UnloadMaterialGroup(material);
            material = null;
        }
    }

    internal override unsafe void Render(RenderContext context)
    {
        var render = AppConfig.Instance.RenderRequestSetColliders.Get();
        if (!render) {
            UnloadMeshes();
            return;
        }
        if (Meshes.Count == 0) {
            ReloadMeshes();
            if (Meshes.Count == 0) return;
        }

        ref readonly var transform = ref GameObject.Transform.WorldTransform;
        foreach (var mesh in Meshes) {
            context.RenderSimple(mesh, transform);
        }
    }
}
