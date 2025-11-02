using System.Diagnostics;
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
public class RequestSetColliderComponent(GameObject gameObject, RszInstance data) : Component(gameObject, data), IFixedClassnameComponent, IGizmoComponent
{
    static string IFixedClassnameComponent.Classname => "via.physics.RequestSetCollider";

    private Material activeMaterial = null!;
    private Material inactiveMaterial = null!;
    private Material obscuredMaterial = null!;

    private readonly List<RcolFile?> rcols = new();

    public IEnumerable<RszInstance?> StoredGroups => RszFieldCache.RequestSetCollider.RequestSetGroups.Get(Data).Select(gg => gg as RszInstance);
    public IEnumerable<string?> StoredResources => RszFieldCache.RequestSetCollider.RequestSetGroups.Get(Data).OfType<RszInstance>().Select(grp => RszFieldCache.RequestSetGroup.Resource.Get(grp));

    // public AABB Bounds => AABB.Invalid;
    // public AABB LocalBounds => rcol == null ? default : AABB.Combine(rcol.Groups.Select(g => g.Shapes.BoundingBox));

    private HashSet<string>? missingRcols;
    private RcolEditor? editor;

    public bool IsEnabled => AppConfig.Instance.RenderRequestSetColliders.Get();

    public void OpenEditor(int rcolIndex)
    {
        var refRcols = RszFieldCache.RequestSetCollider.RequestSetGroups.Get(Data);
        if (refRcols == null || refRcols.Count == 0 || Scene?.IsActive != true || refRcols.Count <= rcolIndex) {
            return;
        }

        var group = ((RszInstance)refRcols[rcolIndex]);

        var rcolPath = RszFieldCache.RequestSetGroup.Resource.Get(group);
        if (Scene.Workspace.ResourceManager.TryResolveFile(rcolPath, out var file)) {
            OpenEditor(file);
        }
    }
    public void OpenEditor(FileHandle rcol)
    {
        if (editor != null) {
            // TODO idk
        }
        if (rcols.Count == 0) UpdateRcolFileList();
        Debug.Assert(rcols.Contains(rcol.GetFile<RcolFile>()));
        editor = EditorWindow.CurrentWindow!.AddSubwindow(new RcolEditor(Scene!.Workspace, rcol, this)).Handler as RcolEditor;
    }

    public void SetEditor(RcolEditor editor)
    {
        if (this.editor != null) {
            // TODO idk
        }

        Debug.Assert(rcols.Contains(editor.File));
        this.editor = editor;
    }

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

    private void UpdateRcolFileList()
    {
        var refRcols = RszFieldCache.RequestSetCollider.RequestSetGroups.Get(Data);
        rcols.Clear();
        if (refRcols == null || refRcols.Count == 0 || Scene?.IsActive != true) {
            return;
        }

        foreach (var group in refRcols.OfType<RszInstance>()) {
            var rcolPath = RszFieldCache.RequestSetGroup.Resource.Get(group);
            if (string.IsNullOrEmpty(rcolPath)) {
                rcols.Add(null);
                continue;
            }

            if (!Scene!.Workspace.ResourceManager.TryResolveFile(rcolPath, out var handle)) {
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

        ref readonly var transform = ref GameObject.Transform.WorldTransform;

        var selectedSet = (editor?.PrimaryTarget as RequestSet);
        if (editor?.PrimaryTarget is RequestSetInfo setInfo) {
            selectedSet = editor.File.RequestSets.FirstOrDefault(rs => rs.Info == setInfo);
        } else if (editor?.PrimaryTarget is RszInstance rszInst) {
            selectedSet = editor.File.RequestSets.FirstOrDefault(rs => rs.Instance == rszInst);
        }
        var selectedGroup = editor?.PrimaryTarget as RcolGroup;

        Matrix4X4<float> shapeMatrix = Matrix4X4<float>.Identity;
        foreach (var rcol in rcols) {
            if (rcol == null) continue;

            foreach (var group in rcol.Groups) {
                foreach (var shape in group.Shapes.Concat(group.ExtraShapes)) {
                    if (shape.shape != null) {
                        if (string.IsNullOrEmpty(shape.Info.primaryJointNameStr) || parentMesh == null) {
                            shapeMatrix = Matrix4X4<float>.Identity;
                        } else {
                            parentMesh.TryGetBoneTransform(shape.Info.primaryJointNameStr, out shapeMatrix);
                        }
                        if (group == selectedGroup || selectedSet?.Group == group) {
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
