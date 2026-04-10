using ContentEditor.BackgroundTasks;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;

namespace ContentEditor.App.ImguiHandling.Chain;

public class ChainEditor(ContentWorkspace env, FileHandle file) : ChainEditorBase(env, file), IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => $"{AppIcons.SI_FileType_CHAIN} Chain";

    public ChainFile File => Handle.GetFile<ChainFile>();

    protected override bool IsRevertable => context.Changed;

    protected override void DrawFileContents()
    {
        if (HandleCache()) {
            foreach (var group in File.Groups) {
                if (string.IsNullOrEmpty(group.name)) {
                    group.name = Utils.HashedBoneNames.GetValueOrDefault(group.terminalNameHash) ?? "";
                }
            }
            foreach (var coll in File.Collisions) {
                coll.UpdateJointNames();
            }
        }

        var isEmpty = context.children.Count == 0;
        if (context.children.Count == 0) {
            context.AddChild("Data", File, new ChainFileImguiHandler());
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

public class Chain2Editor(ContentWorkspace env, FileHandle file) : ChainEditorBase(env, file), IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => $"{AppIcons.SI_FileType_CHAIN2} Chain2";

    public Chain2File File => Handle.GetFile<Chain2File>();

    protected override bool IsRevertable => context.Changed;

    protected override void DrawFileContents()
    {
        if (HandleCache()) {
            foreach (var group in File.Groups) {
                if (string.IsNullOrEmpty(group.name)) {
                    group.name = Utils.HashedBoneNames.GetValueOrDefault(group.terminalNameHash) ?? "";
                }
            }
            foreach (var coll in File.Collisions) {
                coll.UpdateJointNames();
            }
        }

        var isEmpty = context.children.Count == 0;
        if (context.children.Count == 0) {
            context.AddChild("Data", File, new Chain2FileImguiHandler());
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

public abstract class ChainEditorBase(ContentWorkspace env, FileHandle file) : FileEditor(file)
{
    private Dictionary<uint, string>? _hashes;
    internal Dictionary<uint, string>? HashesCache => _hashes;

    public ContentWorkspace Workspace { get; } = env;

    private bool _requestedCache;

    static ChainEditorBase()
    {
    }

    protected bool HandleCache()
    {
        if (_hashes == null) {
            var cachePath = MeshBoneHashCacheTask.GetCachePath(Workspace.Game);
            if (!_requestedCache) {
                _requestedCache = true;
                if (!System.IO.File.Exists(cachePath)) {
                    // OK
                } else if (!cachePath.TryDeserializeJsonFile<Dictionary<uint, string>>(out var db, out var error)) {
                    Logger.Warn("Could not load previous mmtr parameter cache from path " + cachePath + ":\n" + error);
                } else {
                    _hashes = db;
                }

                if (_hashes == null) {
                    MainLoop.Instance.BackgroundTasks.Queue(new MeshBoneHashCacheTask(Workspace.Env));
                }
            } else {
                if (!MainLoop.Instance.BackgroundTasks.HasPendingTask<MeshBoneHashCacheTask>() &&
                    System.IO.File.Exists(cachePath) &&
                    cachePath.TryDeserializeJsonFile<Dictionary<uint, string>>(out var db, out var error)) {
                    _hashes = db;
                }
            }
            if (_hashes != null) {
                foreach (var (hash, name) in _hashes) {
                    Utils.HashedBoneNames.TryAdd(hash, name);
                }
                return true;
            }
        }

        return false;
    }
}


[ObjectImguiHandler(typeof(Chain2File), Stateless = false)]
public class Chain2FileImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        // var file = context.Get<Chain2File>();
        if (context.children.Count == 0) {
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(Chain2File), false);
        }
        context.ShowChildrenUI();
    }
}


[ObjectImguiHandler(typeof(ChainFile), Stateless = false)]
public class ChainFileImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        // var file = context.Get<ChainFile>();
        if (context.children.Count == 0) {
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(ChainFile), false);
        }
        context.ShowChildrenUI();
    }
}
