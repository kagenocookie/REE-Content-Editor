using ContentEditor.App;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ReeLib;
using ReeLib.Pak;

namespace ContentEditor.BackgroundTasks;

public class ListFileGeneratorTask(ContentWorkspace workspace) : IBackgroundTask
{
    private FileListGenerator generator = new FileListGenerator(workspace.Env.Config.GamePath, workspace.Platform);

    public override string ToString() => $"Generating File List";

    public string? Status => generator.Phase.ToString();

    public float Progress => generator.PhaseProgress;

    public TaskStatus TaskStatus { get; set; }
    public bool LatestPAKsOnly { get; set; }
    public FileListGenerator.ScanFlags? Flags { get; set; }
    public bool IncludeOtherGameLists { get; set; }

    public Task Execute(CancellationToken token = default)
    {
        var paks = PakUtils.ScanPakFiles(generator.GameDirectory);
        if (LatestPAKsOnly) {
            var dated = paks.Select(pak => (pak, new FileInfo(pak).LastWriteTime)).OrderByDescending(x => x.LastWriteTime).ToList();
            var latest = dated.First().LastWriteTime;
            var leeway = latest - TimeSpan.FromMinutes(15);
            paks = dated.Where(d => d.LastWriteTime >= leeway).Select(x => x.pak).ToList();
            Logger.Info($"Scanning PAK files:\n" + string.Join('\n', paks));
        }
        generator.PakFiles.AddRange(paks);
        if (workspace.Env.Config.Resources.TryGetListFilePath(out var listPath)) {
            generator.PreviousListFile = listPath;
        }

        if (Flags != null) {
            generator.Flags = Flags.Value;
        }

        if (IncludeOtherGameLists) {
            generator.ReferenceListFiles = ResourceRepository.Initialize()?.LocalInfo
                .Select(loc => loc.Value.TryGetListFilePath(out var ff) ? ff : null!)
                .Where(ff => ff != null)
                .ToArray() ?? [];
        }

        var files = generator.Scan();
        var outputPath = Path.Combine(Directory.GetCurrentDirectory(), $"output/{workspace.Game.name}.list");
        FileSystemUtils.EnsureDirectoryExists(Path.GetDirectoryName(outputPath)!);
        File.WriteAllLines(outputPath, files);
        Logger.Info($"Generated file list written to {outputPath}");
        return Task.CompletedTask;
    }
}

public class ListFileGeneratorTaskWindow : BaseWindowHandler
{
    public override string HandlerName => "List File Generator";

    private FileListGenerator.ScanFlags options = FileListGenerator.ScanFlags.Executable|FileListGenerator.ScanFlags.Files|FileListGenerator.ScanFlags.MaintainPreviousList;
    private bool includeOtherGameLists;
    private bool latestPAKsOnly;

    public override void OnIMGUI()
    {
        if (MainLoop.Instance.BackgroundTasks.HasPendingTask<ListFileGeneratorTask>()) {
            ImGui.TextColored(Colors.Note, "List file generation is in progress. Please wait for it to finish or restart Content Editor to cancel it.");
            return;
        }
        if (context.children.Count == 0) {
            context.AddChild("Options", this, new CsharpFlagsEnumFieldHandler<FileListGenerator.ScanFlags, int>() { HideNumberInput = true }, x => x!.options, (x, v) => x.options = v);
            context.AddChild("Include other game file lists", this, getter: x => x!.includeOtherGameLists, setter: (x, v) => x.includeOtherGameLists = v).AddDefaultHandler();
            context.AddChild("Latest PAK files only (based on last 15mins file modified date)", this, getter: x => x!.latestPAKsOnly, setter: (x, v) => x.latestPAKsOnly = v).AddDefaultHandler();
            context.options |= UIOptions.DisableUndoRedo;
        }
        context.ShowChildrenUI();
        if (ImGui.Button("Generate")) {
            MainLoop.Instance.BackgroundTasks.Queue(new ListFileGeneratorTask(workspace) {
                Flags = options,
                IncludeOtherGameLists = includeOtherGameLists,
                LatestPAKsOnly = latestPAKsOnly,
            });
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) {
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }
    }
}