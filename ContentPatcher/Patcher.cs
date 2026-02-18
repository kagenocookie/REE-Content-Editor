namespace ContentPatcher;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using ContentEditor;
using ContentEditor.Core;
using ContentPatcher.FileFormats;
using ReeLib;

public class Patcher : IDisposable
{
    private Workspace env;
    private ContentWorkspace? workspace;
    private string runtimeEnumsPath = string.Empty;
    private string nativesPath = string.Empty;

    private const string EnumsRelativePath = "reframework/data/injected_enums/";

    public Workspace Env => env;
    private GameConfig config => env.Config;

    public string? OutputFilepath { get; set; }
    public bool IsPublishingMod { get; set; }

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

    public bool Execute(bool reloadBundles = true)
    {
        if (string.IsNullOrEmpty(config.GamePath) || !Directory.Exists(config.GamePath)) {
            Logger.Error("Could not execute patch. Game path is incorrect or not configured.");
            return false;
        }

        var sw = Stopwatch.StartNew();
        // 1. setup the REE Lib environment
        // ensure the pak loader is ready, paths are resolved and the content editor stuff is setup
        // if the workspace is not null, assume it was already fully initialized
        _ = env.PakReader;
        if (workspace == null) {
            var configPath = $"configs/{env.Config.Game.name}";
            // 2. load game-specific patch config / overrides
            workspace = new ContentWorkspace(env, new PatchDataContainer(configPath));
        }
        runtimeEnumsPath = Path.Combine(config.GamePath, EnumsRelativePath);
        nativesPath = Path.Combine(config.GamePath, env.BasePath);

        Logger.Info("Setup workspace in", sw.Elapsed.TotalSeconds);
        sw.Restart();

        // 3. resolve / find all active mods
        if (reloadBundles) {
            workspace.BundleManager.LoadDataBundles();
            Logger.Info($"Loaded {workspace.BundleManager.AllBundles.Count} bundles ({workspace.BundleManager.ActiveBundles.Count} active) in {sw.Elapsed.TotalSeconds}");
        }

        // 4. check all active mods and fetch their diffs
        sw.Restart();
        PreparePatchDiffs();
        Logger.Info($"Calculated patches in {sw.Elapsed.TotalSeconds}s");
        // 5. clean up any previous patch state
        RevertPreviousPatch();
        // 6. write patched files to natives
        sw.Restart();
        var patch = ApplyPatches();
        if (patch == null) return false;

        Logger.Info($"Applied patches in {sw.Elapsed.TotalSeconds}s");
        patch.PatchTimeUtc = DateTime.UtcNow;
        // 7. dump metadata
        DumpPatchMetadata(patch);
        return true;
    }

    private void PreparePatchDiffs()
    {
        if (workspace == null) throw new NullReferenceException("Workspace was not setup");

        foreach (var bundle in workspace.BundleManager.ActiveBundles) {
            if (!(bundle.ResourceListing?.Count > 0)) continue;

            var hasAnyUndiffedResources = bundle.ResourceListing?.Any(e => e.Value.Diff == null && e.Value.DiffTime < new DateTime(2025, 1, 1)) == true;
            if (hasAnyUndiffedResources) {
                // NOTE: we could skip ResourceManager.ClearInstances() if active bundle != null
                // also, we could avoid loading _everything_ and instead only calculate diffs for anything that's missing them
                // although considering this only happens, maybe, one time, and then just reuses the precomputed diff, not very high priority
                workspace.SetBundle(bundle.Name);
                Logger.Info("Re-generating bundle resource file diffs for " + bundle.Name);
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

    private PatchInfo? ApplyPatches()
    {
        var patch = new PatchInfo();
        workspace!.SetBundle(null);
        workspace!.ResourceManager.ClearInstances();
        workspace!.ResourceManager.LoadBaseBundleData();
        string outputDirMain;
        string outputDirSub;

        var outfile = OutputFilepath;
        var isPak = Path.GetExtension(OutputFilepath) == ".pak";
        if (isPak) {
            var outputDir = Path.Combine(config.GamePath, ".content-patcher-staging");
            outputDirMain = Path.Combine(outputDir, "main");
            outputDirSub = Path.Combine(outputDir, "sub");
            if (Directory.Exists(outputDir)) {
                Directory.Delete(outputDir, true);
            }
            outfile = OutputFilepath!;
        } else {
            outputDirMain = OutputFilepath ?? config.GamePath;
            outfile = outputDirMain;
            outputDirSub = Path.Combine(outputDirMain, "sub");
        }
        var needsSubPak = Env.RequiresSubPaksForTextures;
        var hasTextures = false;
        foreach (var file in workspace!.ResourceManager.GetModifiedResourceFiles()) {
            var nativePath = file.NativePath ?? file.Filepath;
            string fileOutput;
            if (needsSubPak && file.Format.format == KnownFileFormats.Texture) {
                hasTextures = true;
                fileOutput = Path.Combine(outputDirSub, nativePath);
            } else {
                fileOutput = Path.Combine(outputDirMain, nativePath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fileOutput)!);
            file.Loader.Save(workspace, file, fileOutput);
            patch.Resources[nativePath] = new PatchedResourceMetadata() {
                TargetFilepath = fileOutput,
                SourceFilepath = file.Filepath,
            };
        }
        if (isPak) {
            if (!Directory.Exists(outputDirMain) && !Directory.Exists(outputDirSub))
            {
                Logger.Error("No files have been modified by the active bundles");
                return null;
            }

            var writer = new PakWriter();
            writer.AddFilesFromDirectory(outputDirMain, true);
            if (IsPublishingMod && workspace.BundleManager.ActiveBundles.LastOrDefault() != null) {
                var bundle = workspace.BundleManager.ActiveBundles.Last();
                var modConfigPath = Path.Combine(workspace.BundleManager.GetBundleFolder(bundle), "modinfo.ini");
                if (File.Exists(modConfigPath)) {
                    writer.AddFile("modinfo.ini", modConfigPath);
                } else {
                    writer.AddFile("modinfo.ini", Encoding.Default.GetBytes(bundle.ToModConfigIni()));
                }
                writer.AddFile("bundle.json", Encoding.Default.GetBytes(JsonSerializer.Serialize(bundle, JsonConfig.jsonOptions)));
            }
            writer.SaveTo(outfile);
            Logger.Info("Patch saved to PAK file: " + outfile);
            patch.PakSize = new FileInfo(outfile).Length;
        }
        foreach (var bundle in workspace.BundleManager.ActiveBundles) {
            if (!(bundle.Enums?.Count > 0)) continue;

            // if the output is PAK, output any custom enums into a reframework dir next to it
            var outputBaseDir = Path.GetExtension(outfile) == ".pak" ? Path.GetDirectoryName(outfile)! : outfile;
            var enumFile = Path.Combine(outputBaseDir, EnumsRelativePath, bundle.Name + ".txt");
            Directory.CreateDirectory(Path.GetDirectoryName(enumFile)!);
            var enumData = new StringBuilder();
            enumData.AppendLine("# This file was auto generated by REE Content Editor").AppendLine();
            foreach (var data in bundle.Enums) {
                enumData.Append('@').AppendLine(data.Key);
                foreach (var (key, val) in data.Value) {
                    enumData.Append(key).Append(' ').AppendLine(val.ToString());
                }
                enumData.AppendLine();
            }
            File.WriteAllText(enumFile, enumData.ToString());
            patch.Resources[enumFile] = new PatchedResourceMetadata() {
                TargetFilepath = enumFile,
                SourceFilepath = enumFile,
            };
        }

        if (needsSubPak && hasTextures) {
            string subOutFile = PakUtils.GetNextSubPakFilepath(Path.HasExtension(outfile) ? Path.GetDirectoryName(outfile)! : outfile);
            var writerSub = new PakWriter();
            writerSub.AddFilesFromDirectory(outputDirSub, true);
            writerSub.SaveTo(subOutFile);
            Logger.Info("Sub pak saved to file: " + outfile);
            patch.SubPakSize = new FileInfo(subOutFile).Length;

            // store the sub pak as a separate file so it gets cleaned up on revert
            patch.Resources[subOutFile] = new PatchedResourceMetadata() {
                TargetFilepath = subOutFile,
                SourceFilepath = subOutFile,
            };
        }

        return patch;
    }

    public void RevertPreviousPatch()
    {
        // note: if we implement patch-to-PAK, this won't be always needed, then we just find our PAK file
        var loosePatchMetaFile = workspace!.BundleManager.ResourcePatchLogPath;
        if (File.Exists(loosePatchMetaFile)) {
            DeletePatchInfoResources(loosePatchMetaFile);
        }

        var pak = FindActivePatchPak();
        if (pak != null) {
            Logger.Info("Deleting previous patch PAK: " + pak);
            File.Delete(pak);
            if (File.Exists(pak + ".patch_metadata.json")) {
                DeletePatchInfoResources(pak + ".patch_metadata.json");
                File.Delete(pak + ".patch_metadata.json");
            }
        }
    }

    private static void DeletePatchInfoResources(string loosePatchMetaFile)
    {
        if (TryDeserialize<PatchInfo>(loosePatchMetaFile, out var data)) {
            Logger.Info("Clearing previous patch data based on metadata in " + loosePatchMetaFile);
            foreach (var file in data.Resources) {
                // var looseFilePath = Path.Combine(config.GamePath, file.Key);
                if (File.Exists(file.Value.TargetFilepath)) {
                    Logger.Info("Deleting", file.Value.TargetFilepath);
                    File.Delete(file.Value.TargetFilepath);
                }
            }
            File.Delete(loosePatchMetaFile);
            Logger.Info("Cleared previous patch data");
        }
    }

    public string? FindActivePatchPak()
    {
        var previousPatchMeta = Directory.EnumerateFiles(env.Config.GamePath, "*.pak.patch_metadata.json").FirstOrDefault();
        if (previousPatchMeta != null) {
            var pakPath = previousPatchMeta.Replace(".patch_metadata.json", "");
            if (File.Exists(pakPath) && TryDeserialize<PatchInfo>(previousPatchMeta, out var meta)) {
                // if the file size does not match the last patch metadata info, something's wrong
                // either the patch PAK got renamed/reordered or straight deleted, in either case we can treat it as missing
                // may be more reliably implemented by adding a marker file inside the PAK at some point
                if (meta.PakSize != 0 && meta.PakSize == new FileInfo(pakPath).Length) {
                    return pakPath;
                }
            }
        }
        return null;
    }

    private void DumpPatchMetadata(PatchInfo patch)
    {
        string metaPath;
        if (Path.GetExtension(OutputFilepath) == ".pak" && Path.Exists(OutputFilepath)) {
            metaPath = OutputFilepath + ".patch_metadata.json";
        } else {
            metaPath = OutputFilepath != null ? Path.Combine(OutputFilepath, "_patch_metadata.json") : workspace!.BundleManager.ResourcePatchLogPath;
        }
        using var fs = File.Create(metaPath);
        JsonSerializer.Serialize(fs, patch, JsonConfig.jsonOptions);
        Logger.Info("Patch metadata written to " + metaPath);
        Logger.Info("File list:\n", string.Join("\n", patch.Resources.Select(r => r.Key)));
    }

    private sealed class PatchInfo
    {
        public DateTime PatchTimeUtc { get; set; }
        public long PakSize { get; set; }
        public long SubPakSize { get; set; }

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