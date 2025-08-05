namespace ContentPatcher;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ContentEditor;
using ContentEditor.Core;
using ContentPatcher.FileFormats;
using ReeLib;

public class Patcher : IDisposable
{
    public readonly GameIdentifier game;
    private Workspace env;
    private ContentWorkspace? workspace;
    private string runtimeEnumsPath = string.Empty;
    private string nativesPath = string.Empty;

    public Workspace Env => env;
    private GameConfig config => env.Config;

    public string? OutputFolder { get; set; }

    public Patcher(GameConfig config)
    {
        env = new Workspace(config);
    }

    public Patcher(ContentWorkspace workspace)
    {
        this.env = workspace.Env;
        this.workspace = workspace;
    }

    public void LoadIniConfig(string inifile)
    {
        var data = IniFile.ReadFileIgnoreKeyCasing("content_patcher.ini").ToList();
        LoadConfig(data);
    }

    public void LoadConfig(IEnumerable<KeyValuePair<string, string>> values)
    {
        config.LoadValues(values);
    }

    public void Execute()
    {
        if (string.IsNullOrEmpty(config.GamePath) || !Directory.Exists(config.GamePath)) {
            Logger.Error("Could not execute patch. Game path is incorrect or not configured.");
            return;
        }

        var sw = Stopwatch.StartNew();
        // 1. setup the REE Lib environment
        // ensure the pak loader is ready, paths are resolved and the content editor stuff is setup
        // if the workspace is not null, assume it was already fully initialized
        _ = env.PakReader;
        if (workspace == null) {
            var configPath = $"configs/{game.name}";
            // 2. load game-specific patch config / overrides
            workspace = new ContentWorkspace(env, new PatchDataContainer(configPath));
        }
        runtimeEnumsPath = Path.Combine(config.GamePath, "reframework/data/usercontent/enums/");
        nativesPath = Path.Combine(config.GamePath, env.BasePath);

        Logger.Info("Setup workspace in", sw.Elapsed.TotalSeconds);
        sw.Restart();

        // 3. resolve / find all active mods
        workspace.BundleManager.LoadDataBundles();
        Logger.Info($"Loaded {workspace.BundleManager.AllBundles.Count} bundles ({workspace.BundleManager.ActiveBundles.Count} active) in {sw.Elapsed.TotalSeconds}");

        // 4. check all active mods and fetch their diffs
        sw.Restart();
        PreparePatchDiffs();
        Logger.Info($"Calculated patches in {sw.Elapsed.TotalSeconds}s");
        // 5. clean up any previous patch state
        RevertPreviousPatch();
        // 6. write patched files to natives
        sw.Restart();
        var patch = ApplyPatches();
        Logger.Info($"Applied patches in {sw.Elapsed.TotalSeconds}s");
        patch.PatchTimeUtc = DateTime.UtcNow;
        // 7. dump metadata
        DumpPatchMetadata(patch);
    }

    private void PreparePatchDiffs()
    {
        if (workspace == null) throw new NullReferenceException("Workspace was not setup");

        foreach (var bundle in workspace.BundleManager.ActiveBundles) {
            if (!(bundle.ResourceListing?.Count > 0)) continue;

            var hasAnyUndiffedResources = bundle.ResourceListing?.Any(e => e.Value.Diff == null) == true;
            if (hasAnyUndiffedResources) {
                // NOTE: we could skip ResourceManager.ClearInstances() if active bundle != null
                // also, we could avoid loading _everything_ and instead only calculate diffs for anything that's missing them
                // although considering this only happens, maybe, one time, and then just reuses the precomputed diff, not very high priority
                workspace.SetBundle(bundle.Name);
                Logger.Info("Re-generating bundle resource file diffs " + bundle.Name);
                workspace.ResourceManager.LoadActiveBundle();
                // TODO Get list of modified resources?
                workspace.SaveBundle();
                if (bundle.DependsOn?.Count > 0) {
                    workspace.SetBundle(null);
                    workspace.ResourceManager.ClearInstances();
                }
            }
        }
    }

    private PatchInfo ApplyPatches()
    {
        var patch = new PatchInfo();
        workspace!.SetBundle(null);
        workspace!.ResourceManager.ClearInstances();
        workspace!.ResourceManager.LoadBaseBundleData();
        var outputPath = OutputFolder ?? config.GamePath;
        // var stagingOutput = Path.Combine(config.GamePath, "content-patcher-staging");
        foreach (var file in workspace!.ResourceManager.GetModifiedResourceFiles()) {
            var nativePath = file.NativePath ?? file.Filepath;
            var fileOutput = Path.Combine(outputPath, nativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fileOutput)!);
            file.Loader.Save(workspace, file, fileOutput);
            patch.Resources[nativePath] = new PatchedResourceMetadata() {
                TargetFilepath = fileOutput,
                SourceFilepath = file.Filepath,
            };
        }
        return patch;
    }

    public void RevertPreviousPatch()
    {
        // note: if we implement patch-to-PAK, this won't be always needed, then we just find our PAK file
        var path = workspace!.BundleManager.ResourcePatchLogPath;
        if (!File.Exists(path)) return;

        if (TryDeserialize<PatchInfo>(path, out var data)) {
            Logger.Info("Clearing previous patch data based on metadata in " + path);
            foreach (var file in data.Resources) {
                // var looseFilePath = Path.Combine(config.GamePath, file.Key);
                if (File.Exists(file.Value.TargetFilepath)) {
                    Logger.Info("Deleting", file.Value.TargetFilepath);
                    File.Delete(file.Value.TargetFilepath);
                }
            }
            File.Delete(path);
            Logger.Info("Cleared previous patch data");
        }
    }

    private void DumpPatchMetadata(PatchInfo patch)
    {
        var path = OutputFolder != null ? Path.Combine(OutputFolder, "_patch_metadata.json") : workspace!.BundleManager.ResourcePatchLogPath;
        using var fs = File.Create(path);
        JsonSerializer.Serialize(fs, patch, JsonConfig.jsonOptions);
        Logger.Info("Patch metadata written to " + path);
        Logger.Info("File list:\n", string.Join("\n", patch.Resources.Select(r => r.Key)));
    }

    private sealed class PatchInfo
    {
        public DateTime PatchTimeUtc { get; set; }

        public Dictionary<string, PatchedResourceMetadata> Resources { get; set; } = new();
    }

    private sealed class PatchedResourceMetadata
    {
        /// <summary>
        /// The target filepath of this resource. This is the fully qualified path for the game's filesystem, e.g. "natives/stm/file.user.2".
        /// </summary>
        public required string TargetFilepath { get; init; }
        /// <summary>
        /// Source path of the last bundle modifying the targeted resource. Only used when diffs are not supported or fail.
        /// </summary>
        public string? SourceFilepath { get; set; }

        public IResourceFilePatcher? fileHandler { get; init; }
        public int OriginalFileSize { get; set; }
    }

    private static bool TryDeserialize<T>(string filepath, [MaybeNullWhen(false)] out T data)
    {
        if (!File.Exists(filepath)) {
            data = default;
            return false;
        }
        using var fs = File.OpenRead(filepath);
        data = JsonSerializer.Deserialize<T>(fs, JsonConfig.jsonOptions);
        return data != null;
    }

    public void Dispose()
    {
        env.Dispose();
        GC.SuppressFinalize(this);
    }
}