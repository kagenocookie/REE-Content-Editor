using System.Buffers;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.UVar;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("via.landscape.Foliage")]
public class Foliage(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data), IFixedClassnameComponent
{
    static string IFixedClassnameComponent.Classname => "via.landscape.Foliage";

    private readonly List<MeshHandle> meshes = new();
    private readonly List<MaterialGroup> materials = new();

    public override AABB LocalBounds {
        get {
            if (!meshes.Any(m => m != null)) return default;

            var bounds = AABB.MaxMin;
            ref readonly var transform = ref GameObject.Transform.WorldTransform;
            for (int i = 0; i < meshes.Count; i++) {
                var mesh = meshes[i];
                if (!mesh.Meshes.Any()) continue;
                var meshBounds = mesh.BoundingBox;
                var group = file?.InstanceGroups?[i];
                if (group == null) continue;

                foreach (var inst in group.transforms!) {
                    var instanceMat = ContentEditor.App.Transform.GetMatrixFromTransforms(inst.pos.ToGeneric(), inst.rot.ToGeneric(), inst.scale.ToGeneric());
                    bounds = bounds
                        .Extend(Vector3D.Transform(meshBounds.minpos.ToGeneric(), instanceMat).ToSystem())
                        .Extend(Vector3D.Transform(meshBounds.maxpos.ToGeneric(), instanceMat).ToSystem());
                }
            }
            return bounds;
        }
    }

    public bool HasMesh => meshes.Count != 0;

    internal override void OnActivate()
    {
        base.OnActivate();

        if (!AppConfig.Instance.RenderMeshes.Get()) return;
        ReloadMeshes();
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        UnloadMeshes();
    }

    private FolFile? file;

    public void ReloadMeshes()
    {
        UnloadMeshes();
        var resourcePath = RszFieldCache.Foliage.FoliageResource.Get(Data);
        if (string.IsNullOrEmpty(resourcePath)) {
            return;
        }

        var ctx = Scene!.RenderContext;
        if (Scene!.Workspace.ResourceManager.TryResolveFile(resourcePath, out var folFile)) {
            file = folFile.GetFile<FolFile>();
            for (int i = 0; i < file.InstanceGroups?.Count; i++) {
                var group = file.InstanceGroups[i];
                if (string.IsNullOrEmpty(group.meshPath) || string.IsNullOrEmpty(group.materialPath)) {
                    continue;
                }

                var mat = ctx.LoadMaterialGroup(group.materialPath);
                var mesh = ctx.LoadMesh(group.meshPath);
                if (mesh != null) {
                    if (mat != null) {
                        ctx.SetMeshMaterial(mesh, mat);
                    }

                    meshes.Add(mesh);
                    materials.Add(mesh.Material);
                }
            }
        }
    }

    private void UnloadMeshes()
    {
        for (int i = 0; i < meshes.Count; ++i) {
            var handle = meshes[i];
            if (handle == null) return;

            var mat = materials[i];
            Scene!.RenderContext.UnloadMesh(handle);
            Scene.RenderContext.UnloadMaterialGroup(mat);
        }
        meshes.Clear();
        materials.Clear();
    }

    internal override unsafe void Render(RenderContext context)
    {
        var render = AppConfig.Instance.RenderMeshes.Get();
        if (!render) {
            UnloadMeshes();
            return;
        }
        if (meshes.Count == 0) {
            ReloadMeshes();
        }
        ref readonly var transform = ref GameObject.Transform.WorldTransform;
        for (int i = 0; i < meshes.Count; i++) {
            var coll = meshes[i];
            if (!coll.Meshes.Any()) continue;
            var group = file?.InstanceGroups?[i];
            if (group != null) {
                foreach (var inst in group.transforms!) {
                    var instanceMat = ContentEditor.App.Transform.GetMatrixFromTransforms(inst.pos.ToGeneric(), inst.rot.ToGeneric(), inst.scale.ToGeneric());
                    context.RenderInstanced(coll, i, group.transforms.Length, Matrix4X4.Multiply(transform, instanceMat));
                }
            }
        }
    }
}
