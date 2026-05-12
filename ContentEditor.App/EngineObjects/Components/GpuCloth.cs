using System.Numerics;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.Chain;
using ReeLib.Chain2;
using ReeLib.Gpuc;
using ReeLib.Mesh;
using ReeLib.via;

namespace ContentEditor.App;

[RszComponentClass("via.dynamics.GpuCloth")]
public class GpuCloth(GameObject gameObject, RszInstance data) : Component(gameObject, data),
    IFixedClassnameComponent,
    IGizmoComponent,
    IEditableComponent<GpucEditMode>
{
    static string IFixedClassnameComponent.Classname => "via.dynamics.GpuCloth";

    // [Flags]
    // public enum RenderFlags
    // {
    //     ControlPointTriangles = 1,
    // }

    private Material activeMaterial = null!;
    private Material inactiveMaterial = null!;
    private Material obscuredMaterial = null!;
    private Material inactiveCollisionMaterial = null!;
    private Material obscuredCollisionMaterial = null!;
    private Material limitsMaterial = null!;

    private GpucFile? overrideFile;
    public GpucFile? CurrentFile => overrideFile;

    public string Resource => RszFieldCache.GpuCloth.Resource.Get(Data);

    public Batch? activeGroup;

    public bool IsEnabled => AppConfig.Instance.RenderChains.Get();

    internal override void OnActivate()
    {
        base.OnActivate();
        Scene!.Root.Gizmos.Add(this);
        Scene!.Root.RegisterEditableComponent(this);
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        Scene!.Root.Gizmos.Remove(this);
        Scene!.Root.UnregisterEditableComponent(this);
    }

    public void ClearOverrideFile()
    {
        overrideFile = null;
    }

    public void SetOverrideFile(GpucFile? file)
    {
        if (file == overrideFile) return;

        overrideFile = file;
    }

    public GizmoContainer? Update(GizmoContainer? gizmo)
    {
        var file = overrideFile;
        if (file == null || Scene?.IsActive != true) return null;

        if (inactiveMaterial == null) {
            var mat = Scene.RenderContext
                .GetMaterialBuilder(BuiltInMaterials.MonoColor)
                .Color("_MainColor", Color.FromVector4(Colors.FileTypeCHAIN));
            activeMaterial = mat.Blend().Create("chain_active");

            (inactiveMaterial, obscuredMaterial) = mat.Blend().Create2("chain", "chain_obscured");
            obscuredMaterial.SetParameter("_MainColor", Color.FromVector4(Colors.Gizmo_Chain_Node) with { A = 95 });
            inactiveMaterial.SetParameter("_MainColor", Color.FromVector4(Colors.Gizmo_Chain_Node) with { A = 45 });

            (inactiveCollisionMaterial, obscuredCollisionMaterial) = mat.Blend().Create2("chain_coll", "chain_coll_obscured");
            inactiveCollisionMaterial.SetParameter("_MainColor", Color.FromVector4(Colors.Gizmo_Chain_Collision) with { A = 200 });
            obscuredCollisionMaterial.SetParameter("_MainColor", Color.FromVector4(Colors.Gizmo_Chain_Collision) with { A = 100 });

            limitsMaterial = mat.Blend().Color("_MainColor", Color.FromVector4(Colors.Gizmo_Chain_Limits)).Create("chain_limits");
        }

        var parentMesh = GameObject.GetComponent<MeshComponent>()?.MeshHandle as AnimatedMeshHandle;
        if (parentMesh?.Bones == null) {
            return null;
        }

        ref readonly var transform = ref GameObject.Transform.WorldTransform;

        gizmo ??= new(RootScene!, this);
        gizmo.PushMaterial(activeMaterial, obscuredMaterial);
        List<MeshBone> jointChain = new();
        foreach (var group in file.Batches) {
            if (activeGroup != null && activeGroup != group) continue;

            foreach (var link in group.DistanceLinks) {
                // link.indexA
            }
        }
        gizmo.PopMaterial();

        Matrix4x4 joint1;
        Matrix4x4 joint2;
        gizmo.PushMaterial(inactiveCollisionMaterial, obscuredCollisionMaterial);
        foreach (var coll in file.CollisionPlanes) {
            if (coll.primaryJointNameHash == 0) {
                continue;
            }
            if (!parentMesh.TryGetBoneTransform(coll.primaryJointNameHash, out joint1)) {
                continue;
            }

            // TODO gpuc planes
        }
        foreach (var coll in file.CollisionSpheres) {
            if (coll.primaryJointNameHash == 0) {
                continue;
            }
            if (!parentMesh.TryGetBoneTransform(coll.primaryJointNameHash, out joint1)) {
                continue;
            }
            var p1 = (Matrix4x4.CreateTranslation(coll.sphere.pos) * joint1 * transform).Translation;
            gizmo.Cur.Add(new Sphere(p1, coll.sphere.r));
        }
        foreach (var coll in file.CollisionCapsules) {
            if (coll.primaryJointNameHash == 0 || coll.secondaryJointNameHash == 0) {
                continue;
            }
            if (!parentMesh.TryGetBoneTransform(coll.primaryJointNameHash, out joint1) || !parentMesh.TryGetBoneTransform(coll.secondaryJointNameHash, out joint2)) {
                continue;
            }
            var p1 = (Matrix4x4.CreateTranslation(coll.capsule.p0) * joint1 * transform).Translation;
            var p2 = (Matrix4x4.CreateTranslation(coll.capsule.p1) * joint2 * transform).Translation;
            gizmo.Cur.Add(new TaperedCapsule(p1, coll.capsule.r0, p2, coll.capsule.r1));
        }
        foreach (var coll in file.CollisionOBBs) {
            if (coll.primaryJointNameHash == 0 || coll.secondaryJointNameHash == 0) {
                continue;
            }
            if (!parentMesh.TryGetBoneTransform(coll.primaryJointNameHash, out joint1) || !parentMesh.TryGetBoneTransform(coll.secondaryJointNameHash, out joint2)) {
                continue;
            }
            gizmo.Cur.Add(new OBB(coll.box.Coord.ToSystem() * joint1, coll.box.Extent));
        }
        gizmo.PopMaterial();

        return gizmo;
    }
}
