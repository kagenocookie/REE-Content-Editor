using ContentEditor.App;
using ReeLib;

namespace ContentEditor.BackgroundTasks;

public class ReeLibResourceUpdateTask(Workspace workspace) : IBackgroundTask
{
    public override string ToString() => Languages.TranslateGame(Workspace.Config.Game.name);

    public string? Status { get; private set; } = "Checking for Updated Resources";

    public float Progress { get; private set; } = -1;

    public bool IsCancelled { get; set; }

    public Workspace Workspace { get; } = workspace;

    public void Execute(CancellationToken token = default)
    {
        ResourceRepository.UpdateAndGet(Workspace.Config.Game);
        Status = "Fetching List File";
        _ = Workspace.ListFile;
        Status = "Fetching RSZ JSON";
        _ = Workspace.RszParser;
        Status = "Fetching Type Cache";
        _ = Workspace.TypeCache;
    }
}
