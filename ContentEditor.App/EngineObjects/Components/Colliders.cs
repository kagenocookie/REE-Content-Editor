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
    private IEnumerable<RszInstance> EnumerableColliders => CollidersList.OfType<RszInstance>();

    private Material wireMaterial = null!;
    private Material lineMaterial = null!;
    private Material lineHighlightMaterial = null!;
    private readonly List<MeshHandle?> meshes = new();

    public bool HasMesh => meshes.Count != 0;

    public bool IsEnabled => AppConfig.Instance.RenderColliders.Get();

    internal override void OnActivate()
    {
        base.OnActivate();
        Scene!.RootScene.Gizmos.Add(this);
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        Scene!.RootScene.Gizmos.Remove(this);
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
        foreach (var collider in CollidersList.OfType<RszInstance>()) {
            gizmo ??= new GizmoContainer(RootScene!, this);

            var shape = RszFieldCache.Collider.Shape.Get(collider);
            switch (shape.RszClass.name) {
                case "via.physics.SphereShape":
                case "via.physics.ContinuousSphereShape":
                    if (isActive) {
                        var sphere = RszFieldCache.SphereShape.Sphere.Get(shape);
                        if (gizmo.Shape(2, lineHighlightMaterial).Priority(1).Push().EditableSphere(ref sphere, out var hid)) {
                            UndoRedo.RecordCallbackSetter(null, shape, RszFieldCache.SphereShape.Sphere.Get(shape), sphere, static (ss, vv) => RszFieldCache.SphereShape.Sphere.Set(ss, vv), $"{shape.GetHashCode()}{hid}");
                        }
                    } else {
                        gizmo.Shape(0, lineMaterial).Add(RszFieldCache.SphereShape.Sphere.Get(shape));
                    }
                    break;
                case "via.physics.AabbShape":
                    gizmo.Shape(0, lineMaterial).Add(RszFieldCache.AabbShape.Aabb.Get(shape));
                    if (isActive) {
                        var box = RszFieldCache.AabbShape.Aabb.Get(shape);
                        if (gizmo.Shape(2, lineHighlightMaterial).Priority(1).Push().EditableAABB(ref box, out var hid)) {
                            UndoRedo.RecordCallbackSetter(null, shape, RszFieldCache.AabbShape.Aabb.Get(shape), box, static (ss, vv) => RszFieldCache.AabbShape.Aabb.Set(ss, vv), $"{shape.GetHashCode()}{hid}");
                        }
                    } else {
                        gizmo.Shape(0, lineMaterial).Add(RszFieldCache.AabbShape.Aabb.Get(shape));
                    }
                    break;
                case "via.physics.BoxShape":
                    if (isActive) {
                        var box = RszFieldCache.BoxShape.Box.Get(shape);
                        if (gizmo.Shape(2, lineHighlightMaterial).Priority(1).Push().EditableOBB(ref box, out var hid)) {
                            UndoRedo.RecordCallbackSetter(null, shape, RszFieldCache.BoxShape.Box.Get(shape), box, static (ss, vv) => RszFieldCache.BoxShape.Box.Set(ss, vv), $"{shape.GetHashCode()}{hid}");
                        }
                    } else {
                        gizmo.Shape(0, lineMaterial).Add(RszFieldCache.BoxShape.Box.Get(shape));
                    }
                    break;
                case "via.physics.CapsuleShape":
                case "via.physics.ContinuousCapsuleShape":
                    if (isActive) {
                        var cap = RszFieldCache.CapsuleShape.Capsule.Get(shape);
                        if (gizmo.Shape(2, lineHighlightMaterial).Priority(1).Push().EditableCapsule(ref cap, out var hid)) {
                            UndoRedo.RecordCallbackSetter(null, shape, RszFieldCache.CapsuleShape.Capsule.Get(shape), cap, static (ss, vv) => RszFieldCache.CapsuleShape.Capsule.Set(ss, vv), $"{shape.GetHashCode()}{hid}");
                        }
                    } else {
                        gizmo.Shape(0, lineMaterial).Add(RszFieldCache.CapsuleShape.Capsule.Get(shape));
                    }
                    break;
                case "via.physics.CylinderShape":
                    if (isActive) {
                        var cap = RszFieldCache.CylinderShape.Cylinder.Get(shape);
                        if (gizmo.Shape(2, lineHighlightMaterial).Priority(1).Push().EditableCylinder(ref cap, out var hid)) {
                            UndoRedo.RecordCallbackSetter(null, shape, RszFieldCache.CylinderShape.Cylinder.Get(shape), cap, static (ss, vv) => RszFieldCache.CylinderShape.Cylinder.Set(ss, vv), $"{shape.GetHashCode()}{hid}");
                        }
                    } else {
                        gizmo.Shape(0, lineMaterial).Add(RszFieldCache.CylinderShape.Cylinder.Get(shape));
                    }
                    break;
                case "via.physics.MeshShape": {
                        var mcolPath = RszFieldCache.MeshShape.Mesh.Get(shape);
                        var mtx = RszFieldCache.MeshShape.TransformMatrix.Get(shape);
                        gizmo.Mesh(RszFieldCache.MeshShape.Mesh.Get(shape), mtx.ToGeneric() * transform, wireMaterial);
                        break;
                    }
                case "via.physics.StaticCompoundShape": {
                        var shapelist = RszFieldCache.StaticCompoundShape.Shapes.Get(shape);
                        // TODO
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

        return gizmo;
    }
}
