using System.Diagnostics;
using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling.Rcol;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ReeLib;
using ReeLib.Rcol;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

[RszComponentClass("via.physics.RequestSetCollider")]
public class RequestSetColliderComponent(GameObject gameObject, RszInstance data) : Component(gameObject, data),
    IFixedClassnameComponent,
    IGizmoComponent,
    IEditableComponent<RcolEditMode>
{
    static string IFixedClassnameComponent.Classname => "via.physics.RequestSetCollider";

    private Material activeMaterial = null!;
    private Material inactiveMaterial = null!;
    private Material obscuredMaterial = null!;

    private readonly List<RcolFile?> rcols = new();
    private RcolFile? overrideFile;
    public IEnumerable<RcolFile> ActiveRcolFiles => rcols.Where(a => a != null)!;

    public IEnumerable<RszInstance?> StoredGroups => RszFieldCache.RequestSetCollider.RequestSetGroups.Get(Data).Select(gg => gg as RszInstance);
    public IEnumerable<string> StoredResources => RszFieldCache.RequestSetCollider.RequestSetGroups.Get(Data).OfType<RszInstance>().Select(grp => RszFieldCache.RequestSetGroup.Resource.Get(grp));

    // public AABB Bounds => AABB.Invalid;
    // public AABB LocalBounds => rcol == null ? default : AABB.Combine(rcol.Groups.Select(g => g.Shapes.BoundingBox));

    private HashSet<string>? missingRcols;
    public RcolGroup? activeGroup;

    public bool IsEnabled => AppConfig.Instance.RenderRequestSetColliders.Get();

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

    public void SetOverrideFile(RcolFile? file)
    {
        if (file == overrideFile) return;

        overrideFile = file;
        UpdateRcolFileList();
    }

    private void UpdateRcolFileList()
    {
        var refRcols = RszFieldCache.RequestSetCollider.RequestSetGroups.Get(Data);
        rcols.Clear();
        if (overrideFile != null) rcols.Add(overrideFile);
        if (refRcols == null || Scene?.IsActive != true) {
            return;
        }

        foreach (var group in refRcols.OfType<RszInstance>()) {
            var rcolPath = RszFieldCache.RequestSetGroup.Resource.Get(group);
            if (string.IsNullOrEmpty(rcolPath)) {
                rcols.Add(null);
                continue;
            }

            if (!Scene.Workspace.ResourceManager.TryResolveGameFile(rcolPath, out var handle)) {
                missingRcols ??= new();
                Logger.ErrorIf(missingRcols.Add(rcolPath), "Failed to resolve rcol file " + rcolPath);
                rcols.Add(null);
                continue;
            }

            rcols.Add(handle.GetFile<RcolFile>());
        }
    }

    public GizmoContainer? Update(GizmoContainer? gizmo)
    {
        UpdateRcolFileList();
        if (rcols.Count == 0 || Scene?.IsActive != true) return null;

        if (inactiveMaterial == null) {
            var mat = Scene.RenderContext
                .GetMaterialBuilder(BuiltInMaterials.MonoColor)
                .Color("_MainColor", Color.FromVector4(Colors.RequestSetColliders));
            activeMaterial = mat.Blend(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.SrcAlpha).Create("rcol_active");

            (inactiveMaterial, obscuredMaterial) = mat.Blend().Create2("rcol", "rcol_obscured");
            obscuredMaterial.SetParameter("_MainColor", Color.FromVector4(Colors.RequestSetColliders) with { A = 90 });
            inactiveMaterial.SetParameter("_MainColor", Color.FromVector4(Colors.RequestSetColliders) with { A = 56 });
        }

        gizmo ??= new(RootScene!, this);

        var parentMesh = GameObject.GetComponent<MeshComponent>()?.MeshHandle as AnimatedMeshHandle;

        var transform = GameObject.Transform.WorldTransform.ToSystem();

        Matrix4x4 shapeMatrix = Matrix4x4.Identity;
        foreach (var rcol in rcols) {
            if (rcol == null) continue;

            foreach (var group in rcol.Groups) {
                foreach (var shape in group.Shapes.Concat(group.ExtraShapes)) {
                    if (shape.shape != null) {
                        if (string.IsNullOrEmpty(shape.Info.primaryJointNameStr) || parentMesh == null) {
                            shapeMatrix = transform;
                        } else {
                            parentMesh.TryGetBoneTransform(shape.Info.primaryJointNameStr, out shapeMatrix);
                            shapeMatrix = shapeMatrix * transform;
                        }
                        if (group == activeGroup) {
                            gizmo.PushMaterial(activeMaterial, obscuredMaterial, priority: 1);
                            gizmo.BeginControl();
                            if (gizmo.Cur.EditableBoxed(in shapeMatrix, shape.shape, out var newShape, out int handleId)) {
                                UndoRedo.RecordCallbackSetter(null, shape, shape.shape, newShape, static (ss, vv) => ss.shape = vv, $"{shape.GetHashCode()}{handleId}");
                            }
                        } else {
                            gizmo.PushMaterial(inactiveMaterial);
                            gizmo.Cur.AddBoxed(in shapeMatrix, shape.shape);
                        }
                        gizmo.PopMaterial();
                    }
                }
            }
        }

        return gizmo;
    }
}
