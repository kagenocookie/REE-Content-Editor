using ContentEditor.App;
using ReeLib;

namespace ContentEditor.BackgroundTasks;

public class ReeLibResourceUpdateTask(Workspace workspace) : IBackgroundTask
{
    public override string ToString() => Lang.TranslateGame(Workspace.Config.Game.name);

    public string? Status { get; private set; } = "Checking for Updated Resources";

    public float Progress { get; private set; } = -1;

    public TaskStatus TaskStatus { get; set; }

    public Workspace Workspace { get; } = workspace;

    public async Task Execute(CancellationToken token = default)
    {
        ResourceRepository.UpdateAndGet(Workspace.Config.Game);
        Status = "Loading List File";
        _ = Workspace.ListFile;
        Status = "Loading RSZ JSON";
        try {
            _ = Workspace.RszParser;
        } catch {
            // ignore, likely unsupported game
        }
        Status = "Loading Type Cache";
        _ = Workspace.TypeCache;
    }
}
