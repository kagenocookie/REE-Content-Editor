using System.Buffers;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.UVar;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("via.physics.Colliders")]
public class Colliders(GameObject gameObject, RszInstance data) : RenderableComponent(gameObject, data), IFixedClassnameComponent
{
    static string IFixedClassnameComponent.Classname => "via.physics.Colliders";

    private List<object> CollidersList => RszFieldCache.Colliders.ColliderList.Get(Data);
    private IEnumerable<RszInstance> EnumerableColliders => CollidersList.OfType<RszInstance>();

    private MaterialGroup? material;
    private readonly List<MeshHandle?> meshes = new();

    public override AABB LocalBounds => !meshes.Any(m => m != null) ? default : AABB.Combine(meshes.Where(m => m != null).Select(m => m!.BoundingBox));

    public bool HasMesh => meshes.Count != 0;

    internal override void OnActivate()
    {
        base.OnActivate();

        if (!AppConfig.Instance.RenderColliders.Get()) return;
        UpdateColliderMeshes();
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        UnloadMeshes();
        if (material != null) {
            Scene?.RenderContext.UnloadMaterialGroup(material);
        }
    }

    public void UpdateColliderMeshes()
    {
        UnloadMeshes();
        for (int i = 0; i < CollidersList.Count; i++) {
            UpdateColliderMesh(i);
        }
    }

    public void UpdateColliderMesh(RszInstance colliderInstance)
    {
        UpdateColliderMesh(CollidersList.IndexOf(colliderInstance));
    }

    public void UpdateColliderMesh(int colliderIndex)
    {
        if (colliderIndex < 0) return;

        var colliders = CollidersList;
        var ctx = Scene?.RenderContext;
        if (ctx == null) return;

        var collider = (RszInstance)colliders[colliderIndex];
        var shape = RszFieldCache.Collider.Shape.Get(collider);

        while (meshes.Count <= colliderIndex) meshes.Add(null);
        var meshHandle = meshes[colliderIndex];
        var curMesh = meshHandle?.GetMesh(0);
        material ??= ctx.GetPresetMaterialGroup(EditorPresetMaterials.Wireframe);

        switch (shape.RszClass.name) {
            case "via.physics.SphereShape":
            case "via.physics.ContinuousSphereShape": {
                    if (curMesh == null || curMesh is not ShapeMesh shapeMesh) {
                        UnloadMesh(colliderIndex);
                        (meshHandle, shapeMesh) = ctx.CreateShapeMesh();
                    }

                    shapeMesh.SetWireShape(
                        RszFieldCache.SphereShape.Sphere.Get(shape),
                        shape.RszClass.name == "via.physics.SphereShape" ? ReeLib.Rcol.ShapeType.Sphere : ReeLib.Rcol.ShapeType.ContinuousSphere
                    );
                    break;
                }
            case "via.physics.AabbShape": {
                    if (curMesh == null || curMesh is not ShapeMesh shapeMesh) {
                        UnloadMesh(colliderIndex);
                        (meshHandle, shapeMesh) = ctx.CreateShapeMesh();
                    }
                    shapeMesh.SetShape(RszFieldCache.AabbShape.Aabb.Get(shape));
                    break;
                }
            case "via.physics.BoxShape": {
                    if (curMesh == null || curMesh is not ShapeMesh shapeMesh) {
                        UnloadMesh(colliderIndex);
                        (meshHandle, shapeMesh) = ctx.CreateShapeMesh();
                    }
                    shapeMesh.SetShape(RszFieldCache.BoxShape.Box.Get(shape));
                    break;
                }
            case "via.physics.CapsuleShape":
            case "via.physics.ContinuousCapsuleShape": {
                    if (curMesh == null || curMesh is not ShapeMesh shapeMesh) {
                        UnloadMesh(colliderIndex);
                        (meshHandle, shapeMesh) = ctx.CreateShapeMesh();
                    }
                    shapeMesh.SetWireShape(
                        RszFieldCache.CapsuleShape.Capsule.Get(shape),
                        shape.RszClass.name == "via.physics.CapsuleShape" ? ReeLib.Rcol.ShapeType.Capsule : ReeLib.Rcol.ShapeType.ContinuousCapsule
                    );
                    break;
                }
            case "via.physics.CylinderShape": {
                    if (curMesh == null || curMesh is not ShapeMesh shapeMesh) {
                        UnloadMesh(colliderIndex);
                        (meshHandle, shapeMesh) = ctx.CreateShapeMesh();
                    }
                    shapeMesh.SetWireShape(RszFieldCache.CylinderShape.Cylinder.Get(shape));
                    break;
                }
            case "via.physics.MeshShape": {
                    UnloadMesh(colliderIndex);
                    var mcolFilepath = RszFieldCache.MeshShape.Mesh.Get(shape);
                    if (!string.IsNullOrEmpty(mcolFilepath)) {
                        // TODO mcol shapes
                        meshHandle = ctx.LoadMesh(mcolFilepath);
                    }
                    break;
                }
            case "via.physics.StaticCompoundShape": {
                    var shapelist = RszFieldCache.StaticCompoundShape.Shapes.Get(shape);
                    // TODO
                    break;
                }
            case "via.physics.HeightFieldShape": {
                    var hfFilepath = RszFieldCache.HeightFieldShape.HeightField.Get(shape);
                    break;
                }
            default:
                // unsupported
                break;
        }

        meshes[colliderIndex] = meshHandle;
        if (meshHandle != null) {
            ctx.SetMeshMaterial(meshHandle, material);
            for (int i = 0; i < meshHandle.Handle.Meshes.Count; i++) {
                var submesh = meshHandle.Handle.Meshes[i];
                meshHandle.SetMaterial(i, submesh is TriangleMesh ? "wire" : "wireFilled");
            }
            meshHandle.Update();
        }
    }

    private void UnloadMeshes()
    {
        for (int i = 0; i < CollidersList.Count; ++i) {
            UnloadMesh(i);
        }
        meshes.Clear();
    }

    private void UnloadMesh(int index)
    {
        if (meshes.Count <= index || Scene == null) return;

        var handle = meshes[index];
        if (handle == null) return;

        Scene.RenderContext.UnloadMesh(handle);
        meshes[index] = null;
    }

    internal override unsafe void Render(RenderContext context)
    {
        var render = AppConfig.Instance.RenderColliders.Get();
        if (!render) {
            UnloadMeshes();
            return;
        }
        if (meshes.Count == 0) {
            UpdateColliderMeshes();
        }
        ref readonly var transform = ref GameObject.Transform.WorldTransform;
        foreach (var coll in meshes) {
            if (coll != null) {
                // NOTE: ideally the update should only happen when things actually change
                // but we don't have a file.Changed event at the moment
                // coll.Update();
                context.RenderSimple(coll, transform);
            }
        }
    }
}
