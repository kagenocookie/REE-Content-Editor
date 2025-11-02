using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("via.landscape.Foliage")]
public class Foliage(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data), IFixedClassnameComponent, IGizmoComponent
{
    static string IFixedClassnameComponent.Classname => "via.landscape.Foliage";

    private readonly List<MeshHandle> meshes = new();
    private readonly List<MaterialGroup> materials = new();

    private readonly List<List<Matrix4X4<float>>> _transformsCache = new();

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

    bool IGizmoComponent.IsEnabled => AppConfig.Instance.RenderMeshes.Get() && (EditorWindow.CurrentWindow?.HasOpenInspectorForTarget(GameObject) ?? false);

    internal override void OnActivate()
    {
        base.OnActivate();
        Scene!.RootScene.Gizmos.Add(this);

        if (!AppConfig.Instance.RenderMeshes.Get()) return;
        ReloadMeshes();
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        Scene!.RootScene.Gizmos.Remove(this);
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

                var mat = ctx.LoadMaterialGroup(group.materialPath, ShaderFlags.EnableInstancing);
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

    private void UpdateInstanceTransforms()
    {
        if (file == null) {
            _transformsCache.Clear();
            return;
        }
        ref readonly var transform = ref GameObject.Transform.WorldTransform;
        for (int i = 0; i < meshes.Count; i++) {
            var group = file.InstanceGroups[i];

            List<Matrix4X4<float>> matrices;
            if (_transformsCache.Count > i) {
                matrices = _transformsCache[i];
                matrices.Clear();
            } else {
                _transformsCache.Add(matrices = new List<Matrix4X4<float>>(group.transforms!.Length));
            }

            foreach (var inst in group.transforms!) {
                var instanceMat = transform * ContentEditor.App.Transform.GetMatrixFromTransforms(inst.pos.ToGeneric(), inst.rot.ToGeneric(), inst.scale.ToGeneric());
                matrices.Add(instanceMat);
            }
        }
        _transformsCache.RemoveAtAfter(meshes.Count);
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
        }
    }

    GizmoContainer? IGizmoComponent.Update(GizmoContainer? gizmo)
    {
        if (file == null) return null;
        gizmo ??= new GizmoContainer(RootScene!, this);

        ref readonly var transformOg = ref GameObject.Transform.WorldTransform;
        var transform = transformOg.ToSystem();

        for (int i = 0; i < meshes.Count; i++) {
            var group = file.InstanceGroups[i];
            for (int m = 0; m < group.transforms!.Length; m++) {
                var item = group.transforms![m];
                if (gizmo.Cur.Push().TransformHandle(transform, item, out var newTransform, out int handleId)) {
                    UndoRedo.RecordCallbackSetter(null, (group.transforms, m), item, newTransform, (obj, newTr) => {
                        obj.transforms[obj.m] = newTr;
                        RecomputeWorldAABB();
                    }, $"{group.GetHashCode()}{m}{handleId}t");
                }
            }
        }

        return gizmo;
    }
}
