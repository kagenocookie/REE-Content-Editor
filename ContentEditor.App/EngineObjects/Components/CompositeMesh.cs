using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("via.render.CompositeMesh")]
public class CompositeMesh(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data), IFixedClassnameComponent, IGizmoComponent
{
    static string IFixedClassnameComponent.Classname => "via.render.CompositeMesh";

    private readonly List<MeshHandle> meshes = new();
    private readonly List<MaterialGroup> materials = new();

    private readonly List<List<Matrix4x4>> _transformsCache = new();

    public IEnumerable<RszInstance> Groups => Data.Get(RszFieldCache.CompositeMesh.InstanceGroups).Cast<RszInstance>();

    public RszInstance? focusedGroup;
    public int focusedGroupElementIndex = -1;

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

                        var pos = RszFieldCache.CompositeMesh.TransformController.LocalPosition.Get(inst);
                        var rot = RszFieldCache.CompositeMesh.TransformController.LocalRotation.Get(inst);
                        // var scl = RszFieldCache.CompositeMesh.TransformController.LocalScale.Get(inst);
                        var instanceMat = ContentEditor.App.Transform.GetMatrixFromTransforms(pos, rot, Vector3.One);
                        bounds = bounds
                            .Extend(Vector3.Transform(meshBounds.minpos, instanceMat))
                            .Extend(Vector3.Transform(meshBounds.maxpos, instanceMat));
                    }
                }
            }
            return bounds;
        }
    }

    public bool HasMesh => meshes.Count != 0;

    bool IGizmoComponent.IsEnabled => AppConfig.Instance.RenderMeshes.Get() && (EditorWindow.CurrentWindow?.HasOpenInspectorForTarget(GameObject) ?? false);

    internal override void OnActivate()
    {
        base.OnActivate();
        Scene!.Root.Gizmos.Add(this);

        if (!AppConfig.Instance.RenderMeshes.Get()) return;
        ReloadMeshes();
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        Scene!.Root.Gizmos.Remove(this);
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

            // var mat = ctx.LoadMaterialGroup(matPath);
            var mat = ctx.LoadMaterialGroup(matPath, ShaderFlags.EnableInstancing);
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
        RecomputeWorldAABB();
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

    protected override void OnUpdateTransform()
    {
        UpdateInstanceTransforms();
    }

    internal void UpdateInstanceTransforms()
    {
        ref readonly var transform = ref GameObject.Transform.WorldTransform;
        var instances = RszFieldCache.CompositeMesh.InstanceGroups.Get(Data);
        for (int i = 0; i < instances.Count; i++) {
            var group = instances[i] as RszInstance;
            if (group == null) {
                if (_transformsCache.Count > i) {
                    _transformsCache[i].Clear();
                }
                continue;
            }

            var transforms = RszFieldCache.CompositeMesh.InstanceGroup.Transforms.Get(group);
            List<Matrix4x4> matrices;
            if (_transformsCache.Count > i) {
                matrices = _transformsCache[i];
                matrices.Clear();
            } else {
                _transformsCache.Add(matrices = new List<Matrix4x4>(transforms.Count));
            }
            foreach (var inst in transforms.Cast<RszInstance>()) {
                if (!RszFieldCache.CompositeMesh.TransformController.Enabled.Get(inst)) continue;

                var pos = RszFieldCache.CompositeMesh.TransformController.LocalPosition.Get(inst);
                var rot = RszFieldCache.CompositeMesh.TransformController.LocalRotation.Get(inst);
                var scl = RszFieldCache.CompositeMesh.TransformController.LocalScale.Get(inst);
                var instanceMat = transform * ContentEditor.App.Transform.GetMatrixFromTransforms(pos, rot, scl);
                matrices.Add(instanceMat);
            }
        }
        _transformsCache.RemoveAtAfter(instances.Count);
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
        for (int i = 0; i < meshes.Count && i < _transformsCache.Count; i++) {
            var cache = _transformsCache[i];
            var mesh = meshes[i];
            if (cache.Count == 0 || mesh.IsEmpty) continue;
            context.RenderInstanced(mesh, cache);
            // foreach (var trans in cache) context.RenderSimple(mesh, trans);
        }
    }

    GizmoContainer? IGizmoComponent.Update(GizmoContainer? gizmo)
    {
        gizmo ??= new GizmoContainer(RootScene!, this);

        ref readonly var transform = ref GameObject.Transform.WorldTransform;
        var instances = RszFieldCache.CompositeMesh.InstanceGroups.Get(Data);
        for (int i = 0; i < instances.Count; i++) {
            var group = instances[i] as RszInstance;
            if (group == null) {
                continue;
            }
            if (focusedGroup != null && focusedGroup != group) continue;

            var transforms = RszFieldCache.CompositeMesh.InstanceGroup.Transforms.Get(group);
            int index = 0;
            foreach (var inst in transforms.Cast<RszInstance>()) {
                if (focusedGroupElementIndex != -1 && index++ != focusedGroupElementIndex) continue;
                if (!RszFieldCache.CompositeMesh.TransformController.Enabled.Get(inst)) continue;

                var pos = RszFieldCache.CompositeMesh.TransformController.LocalPosition.Get(inst);
                var rot = RszFieldCache.CompositeMesh.TransformController.LocalRotation.Get(inst);
                var scl = RszFieldCache.CompositeMesh.TransformController.LocalScale.Get(inst);
                var item = new ReeLib.via.Transform(pos, rot, scl);
                if (gizmo.Cur.Push().TransformHandle(transform, item, out var newTransform, out int handleId)) {
                    UndoRedo.RecordCallbackSetter(null, inst, item, newTransform, (obj, newTr) => {
                        RszFieldCache.CompositeMesh.TransformController.LocalPosition.Set(inst, newTr.pos);
                        RszFieldCache.CompositeMesh.TransformController.LocalRotation.Set(inst, newTr.rot);
                        RszFieldCache.CompositeMesh.TransformController.LocalScale.Set(inst, newTr.scale);
                        RecomputeWorldAABB();
                    }, $"{inst.GetHashCode()}{handleId}t");
                }
            }
        }

        return gizmo;
    }
}
