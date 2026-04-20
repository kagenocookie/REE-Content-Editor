using System.Numerics;
using ContentEditor.App.Graphics;
using ContentPatcher;
using ReeLib;
using ReeLib.Chain;
using ReeLib.Chain2;
using ReeLib.Mesh;
using ReeLib.via;

namespace ContentEditor.App;

[RszComponentClass("via.character.CollisionShapePreset")]
public class CollisionShapePreset(GameObject gameObject, RszInstance data) : Component(gameObject, data),
    IFixedClassnameComponent,
    IGizmoComponent
{
    static string IFixedClassnameComponent.Classname => "via.character.CollisionShapePreset";

    private Material activeMaterial = null!;
    private Material inactiveMaterial = null!;
    private Material obscuredMaterial = null!;

    private IEnumerable<string> FilePaths => Data.Get(RszFieldCache.CollisionShapePreset.ShapePresetInfos)
        .Cast<RszInstance>()
        .Select(info => info.Get(RszFieldCache.CollisionShapePresetInfo.Resource))
        .Where(p => !string.IsNullOrEmpty(p));

    private IEnumerable<string> EnabledFilePaths => Data.Get(RszFieldCache.CollisionShapePreset.ShapePresetInfos)
        .Cast<RszInstance>()
        .Where(info => info.Get(RszFieldCache.CollisionShapePresetInfo.Enabled))
        .Select(info => info.Get(RszFieldCache.CollisionShapePresetInfo.Resource))
        .Where(p => !string.IsNullOrEmpty(p));

    private ClspFile? overrideFile;
    public ClspFile? CurrentOverrideFile => overrideFile;

    public ChainGroupBase? activeGroup;

    public bool IsEnabled => AppConfig.Instance.RenderChains.Get();

    internal override void OnActivate()
    {
        base.OnActivate();
        Scene!.Root.Gizmos.Add(this);
    }

    internal override void OnDeactivate()
    {
        base.OnDeactivate();
        Scene!.Root.Gizmos.Remove(this);
    }

    public void ClearOverrideFile()
    {
        overrideFile = null;
    }

    public void SetOverrideFile(ClspFile? file)
    {
        if (file == overrideFile) return;

        overrideFile = file;
    }

    public GizmoContainer? Update(GizmoContainer? gizmo)
    {
        if (Scene?.IsActive != true) return null;

        IEnumerable<ClspFile> displayFiles;
        if (overrideFile != null) {
            displayFiles = [overrideFile];
        } else {
            var list = new List<ClspFile>();
            foreach (var path in EnabledFilePaths) {
                if (Scene.Workspace.ResourceManager.TryResolveGameFile(path, out var file) && file.Format.format == KnownFileFormats.CollisionShapePreset) {
                    list.Add(file.GetFile<ClspFile>());
                }
            }

            displayFiles = list;
        }

        if (!displayFiles.Any()) return null;

        if (inactiveMaterial == null) {
            var mat = Scene.RenderContext
                .GetMaterialBuilder(BuiltInMaterials.MonoColor)
                .Color("_MainColor", Color.FromVector4(Colors.FileTypeCLSP));

            (activeMaterial, inactiveMaterial, obscuredMaterial) = mat.Blend().Create3("clsp_active", "clsp", "clsp_obscured");
            obscuredMaterial.SetParameter("_MainColor", Color.FromVector4(Colors.FileTypeCLSP) with { A = 95 });
            inactiveMaterial.SetParameter("_MainColor", Color.FromVector4(Colors.FileTypeCLSP) with { A = 45 });
        }

        var parentMesh = GameObject.GetComponent<MeshComponent>()?.MeshHandle as AnimatedMeshHandle;
        if (parentMesh?.Bones == null) {
            return null;
        }

        ref readonly var transform = ref GameObject.Transform.WorldTransform;
        Matrix4x4 joint1;
        Matrix4x4 joint2;
        gizmo ??= new(RootScene!, this);
        gizmo.PushMaterial(activeMaterial, obscuredMaterial);
        foreach (var clsp in displayFiles) {
            foreach (var preset in clsp.Presets) {
                switch (preset.shapeType) {
                    case ReeLib.Clsp.CollisionShapeType.Capsule: {
                            if (preset.hash1 == 0 || preset.hash2 == 0) {
                                break;
                            }
                            if (!parentMesh.TryGetBoneTransform(preset.hash1, out joint1) || !parentMesh.TryGetBoneTransform(preset.hash2, out joint2)) {
                                break;
                            }
                            var shape = (Capsule)preset.shape!;
                            var p1 = (Matrix4x4.CreateTranslation(shape.p0) * joint1 * transform).Translation;
                            var p2 = (Matrix4x4.CreateTranslation(shape.p1) * joint2 * transform).Translation;
                            // p2 = Vector3.Transform(p2 - p1, coll.rotationOffset) + p1;
                            gizmo.Cur.Add(new Capsule(p1, p2, shape.R));
                        }
                        break;
                    case ReeLib.Clsp.CollisionShapeType.TaperedCapsule: {
                            if (preset.hash1 == 0 || preset.hash2 == 0) {
                                break;
                            }
                            if (!parentMesh.TryGetBoneTransform(preset.hash1, out joint1) || !parentMesh.TryGetBoneTransform(preset.hash2, out joint2)) {
                                break;
                            }
                            var shape = (TaperedCapsule)preset.shape!;
                            var p1 = (Matrix4x4.CreateTranslation(shape.p0) * joint1 * transform).Translation;
                            var p2 = (Matrix4x4.CreateTranslation(shape.p1) * joint2 * transform).Translation;
                            gizmo.Cur.Add(new Cone(p1, shape.r0, p2, shape.r1));
                        }
                        break;
                    case ReeLib.Clsp.CollisionShapeType.Sphere: {
                            if (preset.hash1 == 0) {
                                break;
                            }
                            if (!parentMesh.TryGetBoneTransform(preset.hash1, out joint1)) {
                                break;
                            }
                            var shape = (Sphere)preset.shape!;
                            var p1 = (Matrix4x4.CreateTranslation(shape.pos) * joint1 * transform).Translation;
                            gizmo.Cur.Add(new Sphere(p1, shape.r));
                        }
                        break;
                    default:
                        Logger.Debug("Unhandled clsp shape type " + preset.shape);
                        break;
                }
            }
        }
        gizmo.PopMaterial();

        return gizmo;
    }
}
