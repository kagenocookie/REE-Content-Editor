using System.Buffers;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.UVar;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("via.render.CompositeMesh")]
public class CompositeMesh(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data), IFixedClassnameComponent
{
    static string IFixedClassnameComponent.Classname => "via.render.CompositeMesh";

    private readonly List<MeshHandle> meshes = new();
    private readonly List<MaterialGroup> materials = new();

    public override AABB LocalBounds {
        get {
            if (!meshes.Any(m => m != null)) return default;

            var bounds = AABB.MaxMin;
            var instances = RszFieldCache.CompositeMesh.InstanceGroups.Get(Data);
            for (int i = 0; i < meshes.Count; i++) {
                var coll = meshes[i];
                var group = instances[i] as RszInstance;
                if (group != null) {
                    var meshBounds = coll.BoundingBox;
                    var transforms = RszFieldCache.CompositeMesh.InstanceGroup.Transforms.Get(group);
                    foreach (var inst in transforms.Cast<RszInstance>()) {
                        if (!RszFieldCache.CompositeMesh.TransformController.Enabled.Get(inst)) continue;

                        var pos = RszFieldCache.CompositeMesh.TransformController.LocalPosition.Get(inst).ToGeneric();
                        var rot = RszFieldCache.CompositeMesh.TransformController.LocalRotation.Get(inst).ToGeneric();
                        // var scl = RszFieldCache.CompositeMesh.TransformController.LocalScale.Get(inst).ToGeneric();
                        var instanceMat = ContentEditor.App.Transform.GetMatrixFromTransforms(pos, rot, Vector3D<float>.One);
                        bounds = bounds
                            .Extend(Vector3D.Transform(meshBounds.minpos.ToGeneric(), instanceMat).ToSystem())
                            .Extend(Vector3D.Transform(meshBounds.maxpos.ToGeneric(), instanceMat).ToSystem());
                    }
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

    public void ReloadMeshes()
    {
        UnloadMeshes();

        var ctx = Scene!.RenderContext;
        var instances = RszFieldCache.CompositeMesh.InstanceGroups.Get(Data);
        for (int i = 0; i < instances?.Count; i++) {
            var group = (RszInstance)instances[i];
            var transforms = RszFieldCache.CompositeMesh.InstanceGroup.Transforms.Get(group);
            if (transforms.Count == 0) {
                continue;
            }

            var meshPath = RszFieldCache.CompositeMesh.InstanceGroup.Mesh.Get(group);
            var matPath = RszFieldCache.CompositeMesh.InstanceGroup.Material.Get(group);
            var mat = ctx.LoadMaterialGroup(matPath);
            var mesh = ctx.LoadMesh(meshPath);
            if (mesh != null) {
                if (mat != null) {
                    ctx.SetMeshMaterial(mesh, mat);
                }

                meshes.Add(mesh);
                materials.Add(mesh.Material);
            } else if (mat != null) {
                materials.Add(mat);
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
        var instances = RszFieldCache.CompositeMesh.InstanceGroups.Get(Data);
        for (int i = 0; i < meshes.Count; i++) {
            var group = instances[i] as RszInstance;
            if (group != null) {
                var transforms = RszFieldCache.CompositeMesh.InstanceGroup.Transforms.Get(group);
                foreach (var inst in transforms.Cast<RszInstance>()) {
                    if (!RszFieldCache.CompositeMesh.TransformController.Enabled.Get(inst)) continue;
                    var mesh = meshes[i];

                    var pos = RszFieldCache.CompositeMesh.TransformController.LocalPosition.Get(inst).ToGeneric();
                    var rot = RszFieldCache.CompositeMesh.TransformController.LocalRotation.Get(inst).ToGeneric();
                    var scl = RszFieldCache.CompositeMesh.TransformController.LocalScale.Get(inst).ToGeneric();
                    var instanceMat = ContentEditor.App.Transform.GetMatrixFromTransforms(pos, rot, scl);
                    context.RenderInstanced(mesh, i, transforms.Count, instanceMat);
                }
            }
        }
    }
}
