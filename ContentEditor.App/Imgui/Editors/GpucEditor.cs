using System.Numerics;
using System.Reflection;
using ContentEditor.BackgroundTasks;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Chain;
using ReeLib.Chain2;
using ReeLib.Common;
using ReeLib.Gpuc;
using ReeLib.Mesh;

namespace ContentEditor.App.ImguiHandling.Chain;

public class GpucEditor(ContentWorkspace env, FileHandle file, ContentEditor.App.GpuCloth? component)
    : FileEditor(file), IBoneReferenceHolder
{
    public override string HandlerName => $"{AppIcons.SI_FileType_GPUC} GPU Cloth";

    private Dictionary<uint, string>? _hashes;

    public ContentWorkspace Workspace { get; } = env;

    public GpucFile File => Handle.GetFile<GpucFile>();

    public App.GpuCloth? Target { get; } = component;

    private bool _requestedCache;

    protected override bool IsRevertable => context.Changed;

    public IEnumerable<MeshBone> GetBones() => Target?.GameObject.GetComponent<MeshComponent>()?.GetBones() ?? [];

    public MeshBone? FindBoneByHash(uint hash)
    {
        return Target?.GameObject.GetComponent<MeshComponent>()?.FindBoneByHash(hash);
    }

    public bool TryGetBoneTransform(uint hash, out Matrix4x4 matrix)
    {
        var comp = Target?.GameObject.GetComponent<MeshComponent>();
        if (comp == null) {
            matrix = Matrix4x4.Identity;
            return false;
        }

        return comp.TryGetBoneTransform(hash, out matrix);
    }

    protected override void DrawFileContents()
    {
        if (_hashes == null && MeshBoneHashCacheTask.TryResolveCache(Workspace, ref _requestedCache, ref _hashes)) {
            foreach (var item in File.CollisionPlanes) item.UpdateJointNames();
            foreach (var item in File.CollisionSpheres) item.UpdateJointNames();
            foreach (var item in File.CollisionCapsules) item.UpdateJointNames();
            foreach (var item in File.CollisionOBBs) item.UpdateJointNames();
            foreach (var item in File.Parts) item.UpdateJointNames();
        }

        var isEmpty = context.children.Count == 0;
        if (context.children.Count == 0) {
            // context.AddChild("Data", File, new ChainFileImguiHandler());
            context.AddChild("Data", File).AddDefaultHandler();
        }
        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(CollisionCapsule))]
[ObjectImguiHandler(typeof(CollisionPlane))]
[ObjectImguiHandler(typeof(CollisionSphere))]
[ObjectImguiHandler(typeof(CollisionOBB))]
public class GpucCollisionShapeHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var inst = context.Get<CollisionShape>();
            var boneHandler1 = new BoneNameHandler(c => c.parent!.Get<CollisionShape>().primaryJointNameHash, (c, v) => c.parent!.Get<CollisionShape>().primaryJointNameHash = v);
            context.AddChild("Primary Joint", inst, boneHandler1, c => c!.primaryJointName, (c, v) => c.primaryJointName = v ?? "");
            var boneHandler2 = new BoneNameHandler(c => c.parent!.Get<CollisionShape>().secondaryJointNameHash, (c, v) => c.parent!.Get<CollisionShape>().secondaryJointNameHash = v);
            context.AddChild("Secondary Joint", inst, boneHandler2, c => c!.secondaryJointName, (c, v) => c.secondaryJointName = v ?? "");

            WindowHandlerFactory.SetupObjectUIContext(context, inst.GetType(), orderFunc: (f, i) => {
                if (f.Name == nameof(CollisionShape.primaryJointName) || f.Name == nameof(CollisionShape.primaryJointNameHash) ||
                    f.Name == nameof(CollisionShape.secondaryJointName) || f.Name == nameof(CollisionShape.secondaryJointNameHash)
                ) {
                    return -1;
                }
                return i;
            });
        }
        context.ShowChildrenNestedUI();
    }
}