namespace ContentPatcher;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using ContentEditor;
using ContentEditor.Core;
using ContentPatcher.FileFormats;
using ReeLib;
using ReeLib.Common;

public class Patcher : IDisposable
{
    private Workspace env;
    private ContentWorkspace? workspace;
    private string nativesPath = string.Empty;

    private const string EnumsRelativePath = "reframework/data/injected_enums/";

    public Workspace Env => env;
    private GameConfig config => env.Config;

    public PatchParameters Parameters { get; set; } = new() { OutputFilepath = "" };

    private string? LoosePublishMetdataFilepath => Directory.Exists(Parameters?.OutputFilepath) ? Path.Combine(Parameters.OutputFilepath, "_patch_metadata.json") : null;

    private bool _symlinkFailed;

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
        var data = IniFile.ReadFileIgnoreKeyCasing(inifile).ToList();
        LoadConfig(data);
    }

    public void LoadConfig(IEnumerable<KeyValuePair<string, string>> values)
    {
        config.LoadValues(values);
    }

    public bool Execute(PatchParameters parameters)
    {
        if (string.IsNullOrEmpty(config.GamePath) || !Directory.Exists(config.GamePath)) {
            Logger.Error("Could not execute patch. Game path is incorrect or not configured.");
            return false;
        }

        Parameters = parameters;
        if (string.IsNullOrEmpty(parameters.OutputFilepath) && parameters.OutputType == PatchOutputType.GamePatch) {
            parameters.OutputFilepath = config.GamePath;
        }

        if (string.IsNullOrEmpty(parameters.OutputFilepath)) {
            Logger.Error("No patch output file path was given.");
            return false;
        }

        if (parameters.ExportAsPak && !parameters.OutputFilepath.EndsWith(".pak")) {
            Logger.Error("Attempted PAK export with no exact pak file path given.");
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
        nativesPath = Path.Combine(config.GamePath, env.BasePath);

        Logger.Info("Setup workspace in", sw.Elapsed.TotalSeconds);
        sw.Restart();

        // 3. resolve / find all active mods
        if (parameters.ReloadBundles) {
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
            if (!bundle.HasResources) continue;

            var hasAnyUndiffedResources = bundle.Resources.Any(e => e.Diff == null && e.DiffTime < new DateTime(2025, 1, 1)) == true;
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
        string outputDirLoose;
        string outputDirMain;
        string outputDirSub;

        var publishBundle = Parameters.OutputType is PatchOutputType.Publish or PatchOutputType.BundlePublish ? workspace.BundleManager.ActiveBundles.LastOrDefault() : null;
        if (Parameters.ExportAsPak) {
            var outputDir = Path.Combine(config.GamePath, ".content-patcher-staging");
            outputDirLoose = Path.GetDirectoryName(Parameters.OutputFilepath)!;
            outputDirMain = Path.Combine(outputDir, "main");
            outputDirSub = Path.Combine(outputDir, "sub");
            if (Directory.Exists(outputDir)) {
                Directory.Delete(outputDir, true);
            }
        } else {
            outputDirLoose = Parameters.OutputFilepath;
            outputDirMain = Parameters.OutputFilepath;
            outputDirSub = Path.Combine(outputDirMain, ".content-patcher-staging/sub");
            if (Directory.Exists(outputDirSub)) {
                Directory.Delete(outputDirSub, true);
            }
        }

        // prepare all modified files
        var needsSubPak = Parameters.StoreGDeflateTexturesAsSubPak && Env.RequiresSubPaksForTextures;
        var hasTextures = false;
        foreach (var file in workspace!.ResourceManager.GetOpenFiles()) {
            var targetPath = file.TargetPath ?? file.Filepath;
            string fileOutput;
            if (needsSubPak && file.Format.format == KnownFileFormats.Texture) {
                hasTextures = true;
                fileOutput = Path.Combine(outputDirSub, workspace.Env.PrependBasePath(targetPath));
            } else if (file.TargetPath?.StartsWith("reframework") == true) {
                fileOutput = Path.Combine(outputDirLoose, targetPath);
            } else {
                fileOutput = Path.Combine(outputDirMain, workspace.Env.PrependBasePath(targetPath));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fileOutput)!);
            if (!Parameters.ExportAsPak && !file.Modified && Parameters.AllowSymlinks && !_symlinkFailed && File.Exists(file.Filepath)) {
                try {
                    File.CreateSymbolicLink(fileOutput, file.Filepath);
                } catch (Exception e) {
                    Logger.Warn($"Symblic link creation failed. Failling back to plain copy. Error: " + e.Message);
                    _symlinkFailed = true;
                }
            } else {
                file.Loader.Save(workspace, file, fileOutput);
            }
            patch.Resources[targetPath] = new PatchedResourceMetadata() {
                TargetFilepath = fileOutput,
                SourceFilepath = file.Filepath,
            };
        }

        if (publishBundle != null) {
            var modinfoRelativePath = "modinfo.ini";
            string? bundleRelativePath = null;
            if (Parameters.OutputType is PatchOutputType.Publish && Parameters.IncludeBundleJsonForPublish) {
                bundleRelativePath = $"content/installed/{publishBundle.Name}.bundle.json";
            } else if (Parameters.OutputType is PatchOutputType.BundlePublish) {
                bundleRelativePath = $"content/bundles/{publishBundle.Name}/bundle.json";
            }

            var modConfigPath = Path.Combine(workspace.BundleManager.GetBundleFolder(publishBundle), modinfoRelativePath);
            var modinfoOutputPath = Path.Combine(outputDirMain, modinfoRelativePath);
            if (File.Exists(modConfigPath)) {
                File.Copy(modConfigPath, modinfoOutputPath, true);
            } else {
                File.WriteAllBytes(modinfoOutputPath, Encoding.Default.GetBytes(publishBundle.ToModConfigIni()));
            }
            if (Parameters.ExportAsPak) {
                // include modinfo as both loose and inside the pak to be safe
                File.Copy(modinfoOutputPath, Path.Combine(outputDirLoose, modinfoRelativePath), true);
            }

            if (bundleRelativePath != null) {
                var modBundleOutputPath = Path.Combine(outputDirMain, bundleRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(modBundleOutputPath)!);
                using var fs = File.Create(modBundleOutputPath);
                JsonSerializer.Serialize(fs, publishBundle, JsonConfig.jsonOptions);
                if (Parameters.ExportAsPak) {
                    File.Copy(modBundleOutputPath, Path.Combine(outputDirLoose, bundleRelativePath));
                }
            }
        }

        // full pak handling
        if (Parameters.ExportAsPak) {
            if (!Directory.Exists(outputDirMain) && !Directory.Exists(outputDirSub))
            {
                Logger.Error("No files have been modified by the active bundles");
                return null;
            }

            var writer = new PakWriter();
            writer.AddFilesFromDirectory(outputDirMain, true);
            writer.AddFilesFromDirectory(outputDirSub, true);
            writer.SaveTo(Parameters.OutputFilepath);
            Logger.Info("Patch saved to PAK file: " + Parameters.OutputFilepath);
            patch.PakSize = new FileInfo(Parameters.OutputFilepath).Length;
        }

        // handle enums
        foreach (var bundle in workspace.BundleManager.ActiveBundles) {
            if (!(bundle.Enums?.Count > 0)) continue;

            var enumFile = Path.Combine(outputDirLoose, EnumsRelativePath, bundle.Name + ".txt");
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

        // TODO handle runtime bundle.json if needed

        // loose files additional sub pak for textures
        if (!Parameters.ExportAsPak && needsSubPak && hasTextures) {
            string texPakFile = PakUtils.GetNextSubPakFilepath(outputDirMain);
            try {
                var writerSub = new PakWriter();
                writerSub.AddFilesFromDirectory(outputDirSub, true);
                writerSub.SaveTo(texPakFile);
                Logger.Info("Texture pak saved to file: " + texPakFile);
                patch.SubPakSize = new FileInfo(texPakFile).Length;
            } catch (Exception e) {
                Logger.Error($"Failed to update texture sub pak: {e.Message} (file {texPakFile})");
                // keep going with the patch - if the game is running, it might be prevented from writing
                // but the loose files should still be possible to reload so it might still be desired
            }

            // store the sub pak as a separate file so it can get cleaned up on revert
            patch.Resources[texPakFile] = new PatchedResourceMetadata() {
                TargetFilepath = texPakFile,
                SourceFilepath = texPakFile,
            };
        }

        return patch;
    }

    public void RevertPreviousPatch()
    {
        // note: if we implement patch-to-PAK, this won't be always needed, then we just find our PAK file
        var activeLoosePatchMetaFile = workspace!.BundleManager.ResourcePatchLogPath;
        if (File.Exists(activeLoosePatchMetaFile)) {
            DeletePatchInfoResources(activeLoosePatchMetaFile);
        }

        var publishLooseMeta = LoosePublishMetdataFilepath;
        if (File.Exists(publishLooseMeta)) {
            DeletePatchInfoResources(publishLooseMeta);
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
        // attempt to find a currently active patch pak based on a patch_metadata.json
        // if the file size doesn't match the last patch metadata info, something's wrong
        // either the patch PAK got renamed/reordered or straight deleted, in either case we can treat it as missing
        // may be more reliably implemented by adding a marker file inside the PAK at some point

        if (previousPatchMeta == null) {
            // try handle sub pak when used together with loose files
            var loosePatchMetaFile = workspace!.BundleManager.ResourcePatchLogPath;
            if (File.Exists(loosePatchMetaFile) && TryDeserialize<PatchInfo>(loosePatchMetaFile, out var meta) && meta.SubPakSize != 0) {
                var pakPath = meta.Resources.FirstOrDefault(r => r.Key.EndsWith(".pak")).Key;
                if (File.Exists(pakPath) && meta.SubPakSize != 0 && meta.SubPakSize == new FileInfo(pakPath).Length) {
                    return pakPath;
                }
            }
        } else if (previousPatchMeta != null) {
            var pakPath = previousPatchMeta.Replace(".patch_metadata.json", "");
            if (File.Exists(pakPath) && TryDeserialize<PatchInfo>(previousPatchMeta, out var meta)) {
                if (meta.PakSize != 0 && meta.PakSize == new FileInfo(pakPath).Length) {
                    return pakPath;
                }
                if (meta.SubPakSize != 0 && meta.SubPakSize == new FileInfo(pakPath).Length) {
                    return pakPath;
                }
            }
        }
        return null;
    }

    private void DumpPatchMetadata(PatchInfo patch)
    {
        if (Parameters.IncludePatchMetadataJson) {
            string metaPath;
            if (Parameters.ExportAsPak && Path.Exists(Parameters.OutputFilepath)) {
                metaPath = Parameters.OutputFilepath + ".patch_metadata.json";
            } else {
                metaPath = LoosePublishMetdataFilepath ?? workspace!.BundleManager.ResourcePatchLogPath;
            }
            using var fs = File.Create(metaPath);
            JsonSerializer.Serialize(fs, patch, JsonConfig.jsonOptions);
            Logger.Info("Patch metadata written to " + metaPath);
        }
        Logger.Info("File list:\n", string.Join("\n", patch.Resources.Select(r => r.Key)));
    }

    private sealed class PatchInfo
    {
        public DateTime PatchTimeUtc { get; set; }
        public long PakSize { get; set; }
        public long SubPakSize { get; set; }

        public Dictionary<string, PatchedResourceMetadata> Resources { get; set; } = new(PakHashedPathComparer.Instance);
    }

    private sealed class PatchedResourceMetadata
    {
        /// <summary>
        /// The output filepath of this resource. This is the fully qualified absolute path, e.g. "C:/games/RE4/natives/stm/file.user.2".
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