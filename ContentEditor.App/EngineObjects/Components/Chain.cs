using System.Numerics;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.Chain;
using ReeLib.Chain2;
using ReeLib.Mesh;
using ReeLib.via;

namespace ContentEditor.App;

[RszComponentClass("via.motion.Chain")]
public class Chain(GameObject gameObject, RszInstance data) : Component(gameObject, data),
    IFixedClassnameComponent,
    IGizmoComponent,
    IEditableComponent<ChainEditMode>
{
    static string IFixedClassnameComponent.Classname => "via.motion.Chain";

    private Material activeMaterial = null!;
    private Material inactiveMaterial = null!;
    private Material obscuredMaterial = null!;
    private Material inactiveCollisionMaterial = null!;
    private Material obscuredCollisionMaterial = null!;

    private BaseFile? overrideFile;
    public BaseFile? CurrentFile => overrideFile;

    public string ChainAsset => RszFieldCache.Chain.ChainAsset.Get(Data);

    public ChainGroupBase? activeGroup;

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

    public void SetOverrideFile(ChainFile? file)
    {
        if (file == overrideFile) return;

        overrideFile = file;
    }
    public void SetOverrideFile(Chain2File? file)
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
            obscuredMaterial.SetParameter("_MainColor", Color.FromVector4(Colors.FileTypeCHAIN) with { A = 95 });
            inactiveMaterial.SetParameter("_MainColor", Color.FromVector4(Colors.FileTypeCHAIN) with { A = 45 });

            (inactiveCollisionMaterial, obscuredCollisionMaterial) = mat.Blend().Create2("chain_coll", "chain_coll_obscured");
            inactiveCollisionMaterial.SetParameter("_MainColor", Color.FromVector4(Colors.Default) with { A = 200 });
            obscuredCollisionMaterial.SetParameter("_MainColor", Color.FromVector4(Colors.Default) with { A = 100 });
        }

        var parentMesh = GameObject.GetComponent<MeshComponent>()?.MeshHandle as AnimatedMeshHandle;
        if (parentMesh?.Bones == null) {
            return null;
        }

        ref readonly var transform = ref GameObject.Transform.WorldTransform;

        var groups = (file as ChainFile)?.Groups.Cast<ChainGroupBase>() ?? (file as Chain2File)?.Groups;
        if (groups == null) {
            return null;
        }

        var collisions = (file as ChainFile)?.Collisions.Cast<CollisionDataBase>() ?? (file as Chain2File)?.Collisions!;
        var links = (file as ChainFile)?.ChainLinks.Cast<ChainLink>() ?? (file as Chain2File)?.ChainLinks!;
        gizmo ??= new(RootScene!, this);

        gizmo.PushMaterial(activeMaterial, obscuredMaterial);
        List<MeshBone> jointChain = new();
        foreach (var group in groups) {
            // var settings = (group as ChainGroup)?.Settings as ChainSettingBase ?? (group as Chain2Group)?.Settings;
            var endJoint = parentMesh.Bones.GetByHash(group.terminalNameHash);
            if (endJoint == null) continue;
            var nodeCount = group.ChainNodes.Count();
            jointChain.Clear();
            jointChain.Add(endJoint);
            int i = 0;
            for (i = 0; i < nodeCount - 1; i++) {
                var parent = jointChain[0].Parent;
                if (parent == null) {
                    break;
                }

                jointChain.Insert(0, parent);
            }

            i = 0;
            foreach (var node in group.ChainNodes) {
                if (i >= jointChain.Count) {
                    break;
                }
                var bone = jointChain[i++];
                if (!parentMesh.TryGetBoneTransform(bone.name, out var boneTransform)) {
                    continue;
                }
                parentMesh.TryGetBoneTransform(bone.Parent?.name ?? "------", out var parentBoneTransform);

                switch (node.collisionShape) {
                    case ChainNodeCollisionShape.Capsule:
                    case ChainNodeCollisionShape.StretchCapsule:
                        gizmo.Cur.Add(new Capsule((boneTransform * transform).Translation, (parentBoneTransform * transform).Translation, node.collisionRadius));
                        break;
                    case ChainNodeCollisionShape.Sphere:
                        gizmo.Cur.Add(new Sphere((boneTransform * transform).Translation, node.collisionRadius));
                        break;
                }
            }
        }
        gizmo.PopMaterial();

        Matrix4x4 joint1;
        Matrix4x4 joint2;
        gizmo.PushMaterial(inactiveCollisionMaterial, obscuredCollisionMaterial);
        foreach (var coll in collisions) {
            //
            switch (coll.shape) {
                case ChainCollisionShape.Capsule: {
                        if (coll.jointNameHash == 0 || coll.pairJointNameHash == 0) {
                            break;
                        }
                        if (!parentMesh.TryGetBoneTransform(coll.jointNameHash, out joint1) || !parentMesh.TryGetBoneTransform(coll.pairJointNameHash, out joint2)) {
                            break;
                        }
                        var p1 = (Matrix4x4.CreateTranslation(coll.position) * joint1 * transform).Translation;
                        var p2 = (Matrix4x4.CreateTranslation(coll.pairPosition) * joint2 * transform).Translation;
                        p2 = Vector3.Transform(p2 - p1, coll.rotationOffset) + p1;
                        gizmo.Cur.Add(new Capsule(p1, p2, coll.radius));
                    }
                    break;
                case ChainCollisionShape.Sphere:{
                        if (coll.jointNameHash == 0) {
                            break;
                        }
                        if (!parentMesh.TryGetBoneTransform(coll.jointNameHash, out joint1)) {
                            break;
                        }
                        var p1 = (Matrix4x4.CreateTranslation(coll.position) * joint1 * transform).Translation;
                        gizmo.Cur.Add(new Sphere(p1, coll.radius));
                    }
                    break;
                default:
                    Logger.Debug("Unhandled collision shape type " + coll.shape);
                    break;
            }
        }
        gizmo.PopMaterial();

        gizmo.PushMaterial(activeMaterial, inactiveMaterial);
        foreach (var link in links) {
            if (!parentMesh.TryGetBoneTransform(link.terminalNodeNameHashA, out joint1) ||
                !parentMesh.TryGetBoneTransform(link.terminalNodeNameHashB, out joint2)) {
                continue;
            }

            gizmo.Cur.Add(new LineSegment(joint1.Translation, joint2.Translation));
        }
        gizmo.PopMaterial();

        return gizmo;
    }
}
