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

    public bool IsCancelled { get; set; }
    public bool LatestPAKsOnly { get; set; }

    public void Execute(CancellationToken token = default)
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

        // generator.ReferenceListFiles = ResourceRepository.Initialize()?.LocalInfo
        //     .Select(loc => loc.Value.TryGetListFilePath(out var ff) ? ff : null!)
        //     .Where(ff => ff != null)
        //     .ToArray() ?? [];

        var files = generator.Scan();
        var outputPath = Path.Combine(Directory.GetCurrentDirectory(), $"output/{workspace.Game.name}.list");
        FileSystemUtils.EnsureDirectoryExists(Path.GetDirectoryName(outputPath)!);
        File.WriteAllLines(outputPath, files);
        Logger.Info($"Generated file list written to {outputPath}");
    }
}
