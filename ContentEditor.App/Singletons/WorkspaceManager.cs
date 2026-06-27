using ContentEditor.App.Windowing;
using ContentEditor.BackgroundTasks;
using ReeLib;

namespace ContentEditor.App;

public class WorkspaceManager : Singleton<WorkspaceManager>
{
    private readonly Dictionary<GameIdentifier, RefCounted<Workspace>> Workspaces = new();

    public async Task<Workspace> GetWorkspaceAsync(GameIdentifier game)
    {
        if (!Workspaces.TryGetValue(game, out var workspace)) {
            var config = GameConfig.CreateFromRepository(game);
            Workspaces[game] = workspace = new RefCounted<Workspace>(new Workspace(config) {
                AllowUseLooseFiles = AppConfig.Instance.LoadFromNatives.Get(),
            });
            try {
                InitWorkspaceConfig(game, workspace.Instance);
                if (!ResourceRepository.DisableOnlineUpdater) {
                    if (!MainLoop.Instance.BackgroundTasks.HasPendingTask<ReeLibResourceUpdateTask>(t => t.Workspace == workspace.Instance)) {
                        await MainLoop.Instance.BackgroundTasks.QueueAwait(new ReeLibResourceUpdateTask(workspace.Instance));
                    } else {
                        await MainLoop.Instance.BackgroundTasks.Await<ReeLibResourceUpdateTask>(t => t.Workspace == workspace.Instance);
                    }
                }
            } catch {
                Workspaces.Remove(game);
                throw;
            }
        }

        workspace.AddRef();
        return workspace.Instance;
    }

    public Workspace GetWorkspace(GameIdentifier game)
    {
        if (!Workspaces.TryGetValue(game, out var workspace)) {
            var config = GameConfig.CreateFromRepository(game);
            Workspaces[game] = workspace = new RefCounted<Workspace>(new Workspace(config) {
                AllowUseLooseFiles = AppConfig.Instance.LoadFromNatives.Get(),
            });
            InitWorkspaceConfig(game, workspace.Instance);
        }

        workspace.AddRef();
        return workspace.Instance;
    }

    private static void InitWorkspaceConfig(GameIdentifier game, Workspace workspace)
    {
        workspace.Config.GamePath = AppConfig.Instance.GetGamePath(game) ?? string.Empty;
        var rszPath = AppConfig.Instance.GetGameRszJsonPath(game);
        var filelist = AppConfig.Instance.GetGameFilelist(game);
        if (!string.IsNullOrEmpty(rszPath)) workspace.Config.Resources.LocalPaths.RszPatchFiles = [rszPath];
        if (!string.IsNullOrEmpty(filelist)) workspace.Config.Resources.LocalPaths.FileList = filelist;
        workspace.Config.Platform = AppConfig.Instance.GetGamePlatform(game);
        if (workspace.Config.Platform.id == Platform.Unknown) {
            workspace.Config.Platform = PlatformIdentifier.IsX64Game(game) ? PlatformIdentifier.X64 : PlatformIdentifier.Steam;
        }
    }

    public void Release(Workspace env)
    {
        if (Workspaces.TryGetValue(env.Config.Game, out var wref)) {
            if (wref.Release()) {
                Workspaces.Remove(env.Config.Game);
            }
        }
    }

    public bool IsLastReference(Workspace env)
    {
        if (Workspaces.TryGetValue(env.Config.Game, out var wref)) {
            return wref.RefCount == 1;
        }

        return false;
    }
}