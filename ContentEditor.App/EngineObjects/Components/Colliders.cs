using ContentEditor.App.Graphics;
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
            lineMaterial = Scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "lines")
                .Color("_MainColor", Colors.Colliders with { W = 0.6f })
                .Float("_FadeMaxDistance", 400)
                .Blend();
        }
        ref readonly var transform = ref Transform.WorldTransform;
        foreach (var collider in CollidersList.OfType<RszInstance>()) {
            gizmo ??= new GizmoContainer(Scene!, this);

            var shape = RszFieldCache.Collider.Shape.Get(collider);
            switch (shape.RszClass.name) {
                case "via.physics.SphereShape":
                case "via.physics.ContinuousSphereShape":
                    gizmo.Shape(0, lineMaterial).Add(RszFieldCache.SphereShape.Sphere.Get(shape));
                    break;
                case "via.physics.AabbShape":
                    gizmo.Shape(0, lineMaterial).Add(RszFieldCache.AabbShape.Aabb.Get(shape));
                    break;
                case "via.physics.BoxShape":
                    gizmo.Shape(0, lineMaterial).Add(RszFieldCache.BoxShape.Box.Get(shape));
                    break;
                case "via.physics.CapsuleShape":
                case "via.physics.ContinuousCapsuleShape":
                    gizmo.Shape(0, lineMaterial).Add(RszFieldCache.CapsuleShape.Capsule.Get(shape));
                    break;
                case "via.physics.CylinderShape":
                    gizmo.Shape(0, lineMaterial).Add(RszFieldCache.CylinderShape.Cylinder.Get(shape));
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
