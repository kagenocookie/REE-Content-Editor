using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

[RszComponentClass("via.physics.Colliders")]
public class Colliders(GameObject gameObject, RszInstance data) : Component(gameObject, data), IFixedClassnameComponent, IGizmoComponent
{
    static string IFixedClassnameComponent.Classname => "via.physics.Colliders";

    private List<object> CollidersList => RszFieldCache.Colliders.ColliderList.Get(Data);
    public IEnumerable<RszInstance> EnumerableColliders => CollidersList.OfType<RszInstance>();

    private Material wireMaterial = null!;
    private Material lineMaterial = null!;
    private Material lineHighlightMaterial = null!;
    private readonly List<MeshHandle?> meshes = new();

    public bool HasMesh => meshes.Count != 0;

    public bool IsEnabled => AppConfig.Instance.RenderColliders.Get();

    internal override void OnActivate()
    {
        base.OnActivate();
        Scene!.Root.Gizmos.Add(this);
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        UnloadMeshes();
        Scene!.Root.Gizmos.Remove(this);
    }

    private void UnloadMeshes()
    {
        foreach (var m in meshes) {
            if (m != null) Scene!.RenderContext.UnloadMesh(m);
        }
        meshes.Clear();
    }

    public GizmoContainer? Update(GizmoContainer? gizmo)
    {
        if (!GameObject.ShouldDraw) return null;
        if (wireMaterial == null) {
            wireMaterial = Scene!.RenderContext.GetMaterialBuilder(BuiltInMaterials.Wireframe, "wire");
            (lineMaterial, lineHighlightMaterial) = Scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "lines")
                .Color("_MainColor", Colors.Colliders with { W = 0.5f })
                .Float("_FadeMaxDistance", 400)
                .Blend()
                .Create2("lines", "lines_highlight");
            lineHighlightMaterial.SetParameter("_MainColor", Colors.Colliders with { W = 0.8f });
        }
        var isActive = EditorWindow.CurrentWindow?.HasOpenInspectorForTarget(GameObject) ?? false;
        ref readonly var transform = ref Transform.WorldTransform;
        gizmo ??= new GizmoContainer(RootScene!, this);
        if (isActive) {
            gizmo.PushMaterial(lineHighlightMaterial, lineHighlightMaterial, priority: 1);
        } else {
            gizmo.PushMaterial(lineMaterial);
        }

        foreach (var collider in CollidersList.OfType<RszInstance>()) {
            var shape = RszFieldCache.Collider.Shape.Get(collider);
            if (isActive) gizmo.BeginControl();
            switch (shape.RszClass.name) {
                case "via.physics.SphereShape":
                case "via.physics.ContinuousSphereShape":
                    if (isActive) {
                        var sphere = RszFieldCache.SphereShape.Sphere.Get(shape);
                        if (gizmo.Cur.EditableSphere(transform, ref sphere, out var hid)) {
                            UndoRedo.RecordCallbackSetter(null, shape, RszFieldCache.SphereShape.Sphere.Get(shape), sphere, static (ss, vv) => RszFieldCache.SphereShape.Sphere.Set(ss, vv), $"{shape.GetHashCode()}{hid}");
                        }
                    } else {
                        gizmo.Cur.Add(transform, RszFieldCache.SphereShape.Sphere.Get(shape));
                    }
                    break;
                case "via.physics.AabbShape":
                    if (isActive) {
                        var box = RszFieldCache.AabbShape.Aabb.Get(shape);
                        if (gizmo.Cur.EditableAABB(transform, ref box, out var hid)) {
                            UndoRedo.RecordCallbackSetter(null, shape, RszFieldCache.AabbShape.Aabb.Get(shape), box, static (ss, vv) => RszFieldCache.AabbShape.Aabb.Set(ss, vv), $"{shape.GetHashCode()}{hid}");
                        }
                    } else {
                        gizmo.Cur.Add(transform, RszFieldCache.AabbShape.Aabb.Get(shape));
                    }
                    break;
                case "via.physics.BoxShape":
                    if (isActive) {
                        var box = RszFieldCache.BoxShape.Box.Get(shape);
                        if (gizmo.Cur.EditableOBB(transform, ref box, out var hid)) {
                            UndoRedo.RecordCallbackSetter(null, shape, RszFieldCache.BoxShape.Box.Get(shape), box, static (ss, vv) => RszFieldCache.BoxShape.Box.Set(ss, vv), $"{shape.GetHashCode()}{hid}");
                        }
                    } else {
                        gizmo.Cur.Add(transform, RszFieldCache.BoxShape.Box.Get(shape));
                    }
                    break;
                case "via.physics.CapsuleShape":
                case "via.physics.ContinuousCapsuleShape":
                    if (isActive) {
                        var cap = RszFieldCache.CapsuleShape.Capsule.Get(shape);
                        if (gizmo.Cur.EditableCapsule(transform, transform, ref cap, out var hid)) {
                            UndoRedo.RecordCallbackSetter(null, shape, RszFieldCache.CapsuleShape.Capsule.Get(shape), cap, static (ss, vv) => RszFieldCache.CapsuleShape.Capsule.Set(ss, vv), $"{shape.GetHashCode()}{hid}");
                        }
                    } else {
                        gizmo.Cur.Add(transform, RszFieldCache.CapsuleShape.Capsule.Get(shape));
                    }
                    break;
                case "via.physics.CylinderShape":
                    if (isActive) {
                        var cap = RszFieldCache.CylinderShape.Cylinder.Get(shape);
                        if (gizmo.Cur.EditableCylinder(transform, transform, ref cap, out var hid)) {
                            UndoRedo.RecordCallbackSetter(null, shape, RszFieldCache.CylinderShape.Cylinder.Get(shape), cap, static (ss, vv) => RszFieldCache.CylinderShape.Cylinder.Set(ss, vv), $"{shape.GetHashCode()}{hid}");
                        }
                    } else {
                        gizmo.Cur.Add(transform, RszFieldCache.CylinderShape.Cylinder.Get(shape));
                    }
                    break;
                case "via.physics.MeshShape": {
                        var mcolPath = RszFieldCache.MeshShape.Mesh.Get(shape);
                        if (!string.IsNullOrEmpty(mcolPath)) {
                            var mtx = RszFieldCache.MeshShape.TransformMatrix.Get(shape);
                            gizmo.Mesh(mcolPath, mtx.ToSystem() * transform, wireMaterial);
                        }
                        break;
                    }
                case "via.physics.StaticCompoundShape": {
                        var shapelist = RszFieldCache.StaticCompoundShape.Shapes.Get(shape).OfType<RszInstance>();
                        ShowCompoundShapes(gizmo, isActive, shapelist);
                        break;
                    }
                case "via.physics.HeightFieldShape": {
                        var hfFilepath = RszFieldCache.HeightFieldShape.HeightField.Get(shape);
                        gizmo.Mesh(hfFilepath, transform, lineMaterial);
                        break;
                    }
                default:
                    // unsupported
                    break;
            }
        }

        gizmo.PopMaterial();
        return gizmo;
    }

    private void ShowCompoundShapes(GizmoContainer gizmo, bool isActive, IEnumerable<RszInstance> shapelist)
    {
        ref readonly var transform = ref Transform.WorldTransform;
        if (isActive) {
            foreach (var shapeEntry in shapelist) {
                var subshape = shapeEntry.Get(RszFieldCache.StaticCompoundShape.Instance.Shape);
                switch (subshape.RszClass.name) {
                    case "via.physics.BoxShape": {
                            var box = RszFieldCache.BoxShape.Box.Get(subshape);
                            if (gizmo.Cur.EditableOBB(transform, ref box, out var hid)) {
                                UndoRedo.RecordCallbackSetter(null, subshape, RszFieldCache.BoxShape.Box.Get(subshape), box, static (ss, vv) => RszFieldCache.BoxShape.Box.Set(ss, vv), $"{subshape.GetHashCode()}{hid}");
                            }
                            break;
                        }
                    case "via.physics.AabbShape": {
                            var box = RszFieldCache.AabbShape.Aabb.Get(subshape);
                            if (gizmo.Cur.EditableAABB(transform, ref box, out var hid)) {
                                UndoRedo.RecordCallbackSetter(null, subshape, RszFieldCache.AabbShape.Aabb.Get(subshape), box, static (ss, vv) => RszFieldCache.AabbShape.Aabb.Set(ss, vv), $"{subshape.GetHashCode()}{hid}");
                            }
                            break;
                        }
                    case "via.physics.SphereShape": {
                            var sphere = RszFieldCache.SphereShape.Sphere.Get(subshape);
                            if (gizmo.Cur.EditableSphere(transform, ref sphere, out var hid)) {
                                UndoRedo.RecordCallbackSetter(null, subshape, RszFieldCache.SphereShape.Sphere.Get(subshape), sphere, static (ss, vv) => RszFieldCache.SphereShape.Sphere.Set(ss, vv), $"{subshape.GetHashCode()}{hid}");
                            }
                            gizmo.Cur.Add(transform, RszFieldCache.SphereShape.Sphere.Get(subshape));
                            break;
                        }
                    case "via.physics.CapsuleShape": {
                            var scap = RszFieldCache.CapsuleShape.Capsule.Get(subshape);
                            if (gizmo.Cur.EditableCapsule(transform, transform, ref scap, out var hid)) {
                                UndoRedo.RecordCallbackSetter(null, subshape, RszFieldCache.CapsuleShape.Capsule.Get(subshape), scap, static (ss, vv) => RszFieldCache.CapsuleShape.Capsule.Set(ss, vv), $"{subshape.GetHashCode()}{hid}");
                            }
                            break;
                        }
                    case "via.physics.CylinderShape": {
                            var cap = RszFieldCache.CylinderShape.Cylinder.Get(subshape);
                            if (gizmo.Cur.EditableCylinder(transform, transform, ref cap, out var hid)) {
                                UndoRedo.RecordCallbackSetter(null, subshape, RszFieldCache.CylinderShape.Cylinder.Get(subshape), cap, static (ss, vv) => RszFieldCache.CylinderShape.Cylinder.Set(ss, vv), $"{subshape.GetHashCode()}{hid}");
                            }
                            break;
                        }
                    case "via.physics.MeshShape": {
                            var mcolPath = RszFieldCache.MeshShape.Mesh.Get(subshape);
                            if (!string.IsNullOrEmpty(mcolPath)) {
                                var mtx = RszFieldCache.MeshShape.TransformMatrix.Get(subshape);
                                gizmo.Mesh(mcolPath, mtx.ToSystem() * transform, wireMaterial);
                            }
                            break;
                        }
                    default:
                        Logger.Debug($"Unsupported static compound shape type {subshape.RszClass.name}");
                        break;
                }
            }
        } else {
            foreach (var shapeEntry in shapelist) {
                var subshape = shapeEntry.Get(RszFieldCache.StaticCompoundShape.Instance.Shape);
                switch (subshape.RszClass.name) {
                    case "via.physics.BoxShape":
                        gizmo.Cur.Add(transform, RszFieldCache.BoxShape.Box.Get(subshape));
                        break;
                    case "via.physics.AabbShape":
                        gizmo.Cur.Add(transform, RszFieldCache.AabbShape.Aabb.Get(subshape));
                        break;
                    case "via.physics.SphereShape":
                        gizmo.Cur.Add(transform, RszFieldCache.SphereShape.Sphere.Get(subshape));
                        break;
                    case "via.physics.CapsuleShape":
                        gizmo.Cur.Add(transform, RszFieldCache.CapsuleShape.Capsule.Get(subshape));
                        break;
                    case "via.physics.CylinderShape":
                        gizmo.Cur.Add(transform, RszFieldCache.CylinderShape.Cylinder.Get(subshape));
                        break;
                    case "via.physics.MeshShape": {
                            var mcolPath = RszFieldCache.MeshShape.Mesh.Get(subshape);
                            if (!string.IsNullOrEmpty(mcolPath)) {
                                var mtx = RszFieldCache.MeshShape.TransformMatrix.Get(subshape);
                                gizmo.Mesh(mcolPath, mtx.ToSystem() * transform, wireMaterial);
                            }
                            break;
                        }
                    default:
                        Logger.Debug($"Unsupported static compound shape type {subshape.RszClass.name}");
                        break;
                }
            }
        }
    }
}
