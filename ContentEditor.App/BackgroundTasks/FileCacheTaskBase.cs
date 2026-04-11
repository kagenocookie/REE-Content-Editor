using System.Diagnostics;
using System.Text.Json;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;

namespace ContentEditor.BackgroundTasks;

public abstract class FileCacheTaskBase : IBackgroundTask
{
    protected abstract string GetCacheFilePath(GameIdentifier game);

    protected static string GetBaseCacheDir(GameIdentifier game) => Path.Combine(AppConfig.Instance.CacheFilepath.Get() ?? Path.Combine(AppConfig.AppDataPath, "cache"), game.name);

    public string Status { get; private set; }
    public bool IsCancelled { get; set; }
    public Workspace Workspace { get; }

    private int filesProcessed = 0;
    private int totalFiles = 0;

    public float Progress => totalFiles <= 0 ? -1 : (float)filesProcessed / totalFiles;

    public FileCacheTaskBase(Workspace workspace)
    {
        Status = "Processing";
        Workspace = workspace;
    }

    public abstract override string ToString();

    protected abstract void HandleFile(string path, Stream stream);

    protected abstract string FilterPattern { get; }
    protected abstract string FileExtension { get; }

    public unsafe void Execute(CancellationToken token = default)
    {
        totalFiles = Workspace.ListFile?.FilterAllFiles(FilterPattern).Length ?? 1;
        filesProcessed = 0;
        foreach (var (path, stream) in Workspace.GetFilesWithExtension(FileExtension, token)) {
            filesProcessed++;
            var mesh = new MeshFile(new FileHandler(stream, path));
            try {
                HandleFile(path, stream);
            } catch (Exception) {
                continue;
            }
        }

        var outputFilepath = GetCacheFilePath(Workspace.Config.Game);
        var outputDir = Path.GetDirectoryName(outputFilepath);
        if (string.IsNullOrEmpty(outputDir)) {
            Logger.Error("Failed to determine cache output path");
            return;
        }

        try {
            Directory.CreateDirectory(outputDir);
            Serialize(outputFilepath);
        } catch (Exception e) {
            Logger.Error("Failed to save cache to path " + outputFilepath + ":\n" + e.Message);
        }
    }

    protected abstract void Serialize(string outputFilepath);
}
