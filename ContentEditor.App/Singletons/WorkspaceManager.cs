using ReeLib;

namespace ContentEditor.App;

public class WorkspaceManager : Singleton<WorkspaceManager>
{
    private readonly Dictionary<GameIdentifier, RefCounted<Workspace>> Workspaces = new();

    public Workspace GetWorkspace(GameIdentifier game)
    {
        if (!Workspaces.TryGetValue(game, out var workspace)) {
            var config = GameConfig.CreateFromRepository(game);
            Workspaces[game] = workspace = new RefCounted<Workspace>(new Workspace(config) {
                AllowUseLooseFiles = AppConfig.Instance.LoadFromNatives.Get(),
            });
            workspace.Instance.Config.GamePath = AppConfig.Instance.GetGamePath(game) ?? string.Empty;
            var rszPath = AppConfig.Instance.GetGameRszJsonPath(game);
            var filelist = AppConfig.Instance.GetGameFilelist(game);
            if (!string.IsNullOrEmpty(rszPath)) workspace.Instance.Config.Resources.LocalPaths.RszPatchFiles = [rszPath];
            if (!string.IsNullOrEmpty(filelist)) workspace.Instance.Config.Resources.LocalPaths.FileList = filelist;
        }

        workspace.AddRef();
        return workspace.Instance;
    }

    public void Release(Workspace env)
    {
        if (Workspaces.TryGetValue(env.Config.Game, out var wref)) {
            if (wref.Release()) {
                Workspaces.Remove(env.Config.Game);
            }
        }
    }
}