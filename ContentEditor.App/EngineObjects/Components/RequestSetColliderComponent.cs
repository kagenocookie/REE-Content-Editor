using System.Diagnostics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling.Rcol;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ReeLib;
using ReeLib.Rcol;
using ReeLib.via;

namespace ContentEditor.App;

[RszComponentClass("via.physics.RequestSetCollider")]
public class RequestSetColliderComponent(GameObject gameObject, RszInstance data) : Component(gameObject, data), IFixedClassnameComponent, IGizmoComponent
{
    static string IFixedClassnameComponent.Classname => "via.physics.RequestSetCollider";

    private Material mainMaterial = null!;
    private Material obscuredMaterial = null!;

    private readonly List<RcolFile?> rcols = new();

    public IEnumerable<RszInstance?> StoredGroups => RszFieldCache.RequestSetCollider.RequestSetGroups.Get(Data).Select(gg => gg as RszInstance);
    public IEnumerable<string?> StoredResources => RszFieldCache.RequestSetCollider.RequestSetGroups.Get(Data).OfType<RszInstance>().Select(grp => RszFieldCache.RequestSetGroup.Resource.Get(grp));

    // public AABB Bounds => AABB.Invalid;
    // public AABB LocalBounds => rcol == null ? default : AABB.Combine(rcol.Groups.Select(g => g.Shapes.BoundingBox));

    private HashSet<string>? missingRcols;
    private RcolEditor? editor;

    public bool IsEnabled => Scene?.RenderContext.RenderTargetTextureHandle > 0 || AppConfig.Instance.RenderRequestSetColliders.Get();

    public void OpenEditor(FileHandle rcol)
    {
        if (editor != null) {
            // TODO idk
        }
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

    public GizmoContainer? Update(GizmoContainer? gizmo)
    {
        var refRcols = RszFieldCache.RequestSetCollider.RequestSetGroups.Get(Data);
        if (refRcols == null || refRcols.Count == 0 || Scene?.IsActive != true) {
            return null;
        }

        if (mainMaterial == null) {
            var mat = Scene.RenderContext
                .GetMaterialBuilder(BuiltInMaterials.Wireframe)
                .Color("_InnerColor", Color.FromVector4(Colors.RequestSetColliders));
            (mainMaterial, obscuredMaterial) = mat.Create2("rcol", "rcol_obscured");
            obscuredMaterial.SetParameter("_InnerColor", Color.FromVector4(Colors.RequestSetColliders) with { A = 80 });
        }

        rcols.Clear();

        var parentMesh = GameObject.GetComponent<MeshComponent>()?.MeshHandle as AnimatedMeshHandle;
        if (parentMesh != null) {
            // parentMesh.BoneMatrices
        }

        gizmo ??= new(Scene, this);

        ref readonly var transform = ref GameObject.Transform.WorldTransform;
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

        var selectedSet = editor?.PrimaryTarget as RequestSet;
        var selectedGroup = editor?.PrimaryTarget as RcolGroup;

        foreach (var rcol in rcols) {
            if (rcol == null) continue;

            // TODO maybe add a toggle for set vs group display?

            foreach (var group in rcol.Groups) {
                foreach (var shape in group.Shapes) {
                    if (shape.shape != null) {
                        if (group == selectedGroup) {
                            if (gizmo.Shape(2, mainMaterial, obscuredMaterial).EditableBoxed(shape.shape, out var newShape)) {
                                shape.shape = newShape;
                            }
                        } else {
                            gizmo.Shape(1, mainMaterial).AddBoxed(shape.shape);
                        }
                    }
                }
                // foreach (var shape in group.ExtraShapes) {
                //     if (shape.shape != null) {
                //         if (group == selectedGroup) {
                //             if (gizmo.Shape(2, mainMaterial, obscuredMaterial).EditableBoxed(shape.shape, out var newShape)) {
                //                 shape.shape = newShape;
                //             }
                //         } else {
                //             gizmo.Shape(1, mainMaterial).AddBoxed(shape.shape);
                //         }
                //     }
                // }
            }

            // foreach (var set in rcol.RequestSets) {
            //     if (set.Group == null) continue;

            //     foreach (var shape in set.Group.Shapes) {
            //         if (shape.shape != null) {
            //             gizmo.Shape(0, mainMaterial).AddBoxed(shape.shape);
            //             if (set == selectedSet) {
            //                 if (gizmo.Shape(2, mainMaterial, obscuredMaterial).EditableBoxed(shape.shape, out var newShape)) {
            //                     shape.shape = newShape;
            //                 }
            //             } else {
            //                 gizmo.Shape(0, mainMaterial).AddBoxed(shape.shape);
            //             }
            //         }
            //     }
            // }
        }

        return gizmo;
    }
}
