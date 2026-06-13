using System.Text.Json;
using ContentEditor;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher.FileFormats;
using ContentPatcher.StringFormatting;
using ReeLib;
using ReeLib.Pak;

namespace ContentPatcher;

public sealed class ContentWorkspace : IDisposable
{
    public Workspace Env { get; }
    public ResourceManager ResourceManager { get; }
    public ContentWorkspaceData Data { get; set; } = new();
    public PatchDataContainer Config { get; }
    public BundleManager BundleManager { get; set; }
    public BundleManager? EditedBundleManager { get; private set; }
    public Bundle? CurrentBundle { get; private set; }
    public DiffHandler Diff { get; }
    public MessageManager Messages { get; }
    public GameIdentifier Game => Env.Config.Game;
    public PlatformIdentifier Platform => Env.Config.Platform;
    public UIService UI { get; set; } = new UIServiceStub();
    public string VersionHash { get; private set; }

    public ContentWorkspace(Workspace env, PatchDataContainer patchConfig, BundleManager? rootBundleManager = null)
    {
        Env = env;
        Config = patchConfig;
        BundleManager = rootBundleManager ?? new() { VersionHash = VersionHash };
        BundleManager.GamePath = env.Config.GamePath;
        Diff = new DiffHandler(env);
        ResourceManager = new ResourceManager(patchConfig);
        Messages = new MessageManager(env);
        if (!patchConfig.IsLoaded) {
            var valid = false;
            try {
                var parser = env.RszParser;
                RszFieldCache.InitializeFieldOverrides(env.Config.Game.name, parser);
                valid = true;
            } catch (Exception e) {
                Logger.Error("RSZ files are not supported at the moment, the RSZ template json is either unconfigured or invalid. Error: " + e.Message);
                // fallback to a "game agnostic" rsz json so we can at least open meshes and other basic functionality
                env.Config.Resources.LocalPaths.RszPatchFiles = [Path.Combine(AppContext.BaseDirectory, "configs/global/agnostic_rsz.json")];
                var parser = env.RszParser;
            }
            if (valid) patchConfig.Load(this);
        }
        VersionHash = string.IsNullOrEmpty(env.Config.GamePath) ? "0000" : AppUtils.GetGameVersionHash(env.Config);
        ResourceManager.Setup(this);
    }

    private ContentWorkspace(ContentWorkspace source)
    {
        Env = source.Env;
        Config = source.Config;
        BundleManager = new () { VersionHash = VersionHash };
        BundleManager.GamePath = Env.Config.GamePath;
        Diff = new DiffHandler(Env);
        ResourceManager = new ResourceManager(source.Config);
        Messages = new MessageManager(Env);
        VersionHash = source.VersionHash;
        ResourceManager.Setup(this);
    }

    public ContentWorkspace CreateTempClone()
    {
        return new ContentWorkspace(this);
    }

    public void SetBundle(string? bundle)
    {
        if (bundle == null) {
            EditedBundleManager = null;
            if (Data.ContentBundle != null) {
                Data.ContentBundle = null;
            }
            CurrentBundle = null;
            Data.Name = null;
            ResourceManager.SetBundle(BundleManager, null);
            return;
        }

        if (Data.ContentBundle != bundle || EditedBundleManager == null) {
            if (Data.Name == null) Data.Name = bundle;
            Data.ContentBundle = bundle;
            EditedBundleManager = BundleManager.CreateBundleSpecificManager(bundle);
            CurrentBundle = EditedBundleManager.GetBundle(bundle, null);
            ResourceManager.SetBundle(EditedBundleManager, CurrentBundle);
        }
    }

    public int SaveModifiedFiles(IRectWindow? parent = null)
    {
        Logger.Info("Saving files ...");
        int count = 0;
        string? lastFile = null;
        try {
            foreach (var file in ResourceManager.GetModifiedResourceFiles()) {
                lastFile = file.Filepath;
                if (parent == null || file.References.Any(ff => ff.Parent == parent)) {
                    if (file.Save(this)) {
                        count++;
                    }
                }
            }
            SaveBundle();
        } catch (Exception e) {
            Logger.Error(e, $"Save failed. {lastFile}");
        }
        return count;
    }

    public void SaveBundle(bool forceDiffAllFiles = false)
    {
        if (Data.ContentBundle == null) {
            if (ResourceManager.HasAnyActivatedEntities) {
                Logger.Warn("Entities may have been modified - A bundle is required to save such changes.");
            }
            return;
        }

        var bundle = EditedBundleManager?.GetBundle(Data.ContentBundle, null) ?? BundleManager.GetBundle(Data.ContentBundle, null);
        if (bundle == null) {
            WindowManager.Instance.ShowError($"Bundle '{Data.ContentBundle}' not found!");
            return;
        }
        if (bundle.HasResources) {
            // update the diffs for all open bundle resource files that are part of the bundle
            // we don't check for file.Modified because it can be marked as false but still be different from the current diff
            // e.g. if we manually replaced the file or undo'ed our changes
            if (forceDiffAllFiles) {
                foreach (var info in bundle.Resources) {
                    FileHandle? file;
                    try {
                        file = ResourceManager.GetFileHandle(info.Target);
                        if (file == null) {
                            Logger.Error("Failed to open bundle file " + info.Target);
                            continue;
                        }
                    } catch (Exception e) {
                        Logger.Error("Failed to open bundle file " + info.Target + ": " + e.Message);
                        continue;
                    }

                    TryExecuteDiff(bundle, file);
                    ResourceManager.CloseFile(file);
                }
            } else {
                foreach (var file in ResourceManager.GetOpenFiles()) {
                    if (file.HandleType == FileHandleType.Memory) continue;

                    TryExecuteDiff(bundle, file);
                }
            }
        }

        var deletes = new List<int>();
        var enums = bundle.Enums ??= new Dictionary<string, Dictionary<string, JsonElement>>();
        enums.Clear();
        for (int i = 0; i < bundle.Entities.Count; i++) {
            var entity = bundle.Entities[i];
            if (entity is not ResourceEntity resEntity) {
                // if it wasn't updated to a ResourceEntity, no resources were activated, therefore nothing was changed
                if (entity.Enums != null) {
                    // we do need to make sure to keep their enums though
                    foreach (var (en, ee) in entity.Enums) {
                        if (!enums.TryGetValue(en, out var eo)) {
                            enums[en] = eo = new Dictionary<string, JsonElement>();
                        }
                        foreach (var (k, v) in ee) {
                            eo[k] = v.Clone();
                        }
                    }
                }
                continue;
            }

            var activeEntity = ResourceManager.GetActiveEntityInstance(entity.Type, entity.Id);
            if (activeEntity == null) {
                deletes.Add(i);
                continue;
            }

            entity.Data = activeEntity?.CalculateDiff(this);
            if (entity.Data == null) {
                deletes.Add(i);
                continue;
            }

            if (resEntity.Config.Enums?.Length > 0) {
                resEntity.Enums = new();
                // store any expected custom enum entries into the bundle so the patcher can just use them as-is without needing to re-evaluate
                var enumFormatter = FormatterSettings.CreateFullEntityFormatter(resEntity.Config, this);
                foreach (var enumInfo in resEntity.Config.Enums) {
                    if (string.IsNullOrEmpty(enumInfo.format)) continue;

                    if (!enums.TryGetValue(enumInfo.name, out var enumData)) {
                        enums[enumInfo.name] = enumData = new Dictionary<string, JsonElement>();
                    }
                    var localEnum = resEntity.Enums[enumInfo.name] = new Dictionary<string, JsonElement>();

                    var fmt = new StringFormatter(enumInfo.format, enumFormatter);

                    var label = fmt.GetString(entity);
                    var value = JsonSerializer.SerializeToElement(entity.Id);
                    enumData[label] = value;
                    localEnum[label] = value.Clone();
                }
            } else {
                resEntity.Enums = null;
            }

            Logger.Debug($"Saving modified entity {entity.Label}");
        }

        deletes.Reverse();
        foreach (var del in deletes) {
            Logger.Info($"Removed entity {bundle.Entities[del].Label}");
            bundle.Entities.RemoveAt(del);
        }
        bundle.GameVersion = VersionHash;
        if (enums.Count == 0) {
            bundle.Enums = null;
        }

        bundle.Save();
    }

    public void SaveBundleFileDiff(FileHandle file)
    {
        if (CurrentBundle == null) {
            Logger.Error("No active bundle");
            return;
        }

        TryExecuteDiff(CurrentBundle, file);
        CurrentBundle.Save();
    }

    private static void TryExecuteDiff(Bundle bundle, FileHandle file)
    {
        if (file.TargetPath != null && bundle.TryFindResource(file.TargetPath, out var resourceListing, out var localPath) && file.DiffHandler != null) {
            try {
                var newdiff = file.DiffHandler.FindDiff(file);
                if (newdiff?.ToJsonString() != resourceListing.Diff?.ToJsonString()) {
                    resourceListing.Diff = newdiff;
                }
                resourceListing.DiffTime = DateTime.UtcNow;
            } catch (Exception e) {
                Logger.Error(e, $"Failed to generate file diff {file.TargetPath} (bundle local path: {localPath})");
            }
        }
    }

    public void CreateBundleFromPAK(string bundleName, string pakFilepath)
    {
        if (BundleManager.GetBundle(bundleName, null) != null) {
            Logger.Error($"Bundle {bundleName} already exists!");
            return;
        }
        var bundlePath = BundleManager.ConstructBundleFolder(bundleName);

        var pak = new PakReader();
        pak.PakFilePriority = [pakFilepath];
        if (!pak.TryReadManifestFileList(pakFilepath)) {
            pak.AddFiles(Env.ListFile?.Files ?? []);
            var gen = new FileListGenerator(Env.Config.GamePath, Platform) {
                Flags = FileListGenerator.ScanFlags.Files
            };
            gen.PakFiles.Add(pakFilepath);
            var scannedFiles = gen.Scan();
            pak.AddFiles(scannedFiles);
        }
        Directory.CreateDirectory(bundlePath);
        pak.UnpackFilesTo(bundlePath);
        InitializeUnlabelledBundle(bundlePath, null, bundleName);
    }

    public void InitializeUnlabelledBundle(string bundlePath, string? sourcePath = null, string? name = null)
    {
        var bundleName = name ?? Path.GetFileName(bundlePath);

        if (!Path.IsPathFullyQualified(bundlePath)) bundlePath = BundleManager.ConstructBundleFolder(bundlePath);
        if (BundleManager.GetBundle(bundleName, null) != null) {
            Logger.Error($"Bundle {bundleName} already exists!");
            return;
        }
        if (sourcePath != null && sourcePath.NormalizeFilepath() != bundlePath.NormalizeFilepath()) {
            // copy all files from source to the bundle path
            foreach (var srcfile in Directory.EnumerateFiles(sourcePath, "*.*", SearchOption.AllDirectories)) {
                var destFile = Path.Combine(bundlePath, srcfile.NormalizeFilepath().Replace(sourcePath.NormalizeFilepath(), "").TrimStart('/'));
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(srcfile, destFile, true);
            }
        }
        var originalBundle = this.CurrentBundle;
        if (originalBundle != null) {
            SetBundle(null);
        }
        var bundleJsonPath = Path.Combine(bundlePath, "bundle.json");
        var bundle = BundleManager.GetOrCreateBundle(bundleName, bundlePath);
        bundle.Name = bundleName;
        var modIni = Path.Combine(bundlePath, "modinfo.ini");
        if (File.Exists(modIni)) {
            foreach (var (key, value) in IniFile.ReadFileIgnoreKeyCasing(modIni)) {
                if (key == "author") {
                    bundle.Author = value;
                } else if (key == "description") {
                    bundle.Description = bundle.Description == null ? value : bundle.Description + "\n\n" + value;
                } else if (key == "name") {
                    bundle.Description = "Mod name: " + (bundle.Description == null ? value : value + "\n\n" + bundle.Description);
                }
            }
        }

        var hasPreviousBundleData = false;
        if (Path.Exists(bundleJsonPath)) {
            using var fs = File.OpenRead(bundleJsonPath);
            var data = JsonSerializer.Deserialize<Bundle>(fs);
            if (data != null) {
                bundle.CopyFrom(data);
                bundle.Name = bundleName;
                bundle.Init(BundleManager);
                hasPreviousBundleData = true;
            }
        }

        var bundlePathNorm = bundlePath.NormalizeFilepath();
        if (!hasPreviousBundleData) {
            bundle.GameVersion = VersionHash;
            foreach (var file in Directory.EnumerateFiles(bundlePath, "*.pak")) {
                if (file.EndsWith(".pak")) {
                    Logger.Info("Unpacking PAK file: " + file);
                    var pak = new PakReader();
                    pak.PakFilePriority = [file];
                    if (!pak.TryReadManifestFileList(file)) {
                        pak.AddFiles(Env.ListFile?.Files ?? []);
                        var gen = new FileListGenerator(Env.Config.GamePath, Platform) {
                            Flags = FileListGenerator.ScanFlags.Files
                        };
                        gen.PakFiles.Add(file);
                        var scannedFiles = gen.Scan();
                        pak.AddFiles(scannedFiles);
                    }
                    var count = pak.UnpackFilesTo(bundlePath);
                    if (count == 0) {
                        Logger.Warn("Couldn't find any files within PAK file: " + file);
                    }
                }
            }
        }

        foreach (var file in Directory.EnumerateFiles(bundlePath, "*.*", SearchOption.AllDirectories)) {
            var localFile = file.NormalizeFilepath().Replace(bundlePathNorm, "").TrimStart('/');
            if (localFile == "modinfo.ini") continue;
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".txt" || ext == ".json") {
                // probably cover images
                continue;
            }
            if (localFile.EndsWith(".pak")) {
                continue;
            }

            if (localFile.StartsWith("natives/")) {
                bundle.AddResource(localFile, Env.RemoveBasePath(localFile).ToString(), true);
            } else if (localFile.StartsWith("reframework/data/usercontent/bundles/") && localFile.EndsWith(".json")) {
                Logger.Error("Found nested bundle file, the mod was not installed correctly. Aborting.\nFile: " + file);
                // we'll need a different flow for upgrading legacy DD2 bundles, as well as some sort of migration of legacy data
                return;
            } else if (localFile.StartsWith("reframework/")) {
                bundle.AddResource(localFile, localFile, true);
            } else {
                var listfile = Env.ListFile;
                var nativePath = localFile;
                var isProbablyCorrect = false;
                if (listfile != null) {
                    var fn = Path.GetFileName(localFile);
                    var possibleNatives = listfile.GetFiles($".*/{fn}$");
                    if (possibleNatives.Length == 1) {
                        nativePath = possibleNatives[0];
                        isProbablyCorrect = true;
                    } else if (possibleNatives.Length > 1) {
                        nativePath = possibleNatives[0];
                    }
                    listfile.ResetResultCache();
                }
                var fmt = PathUtils.ParseFileFormat(localFile).format;
                bundle.AddResource(localFile, Env.RemoveBasePath(localFile).ToString(), fmt.IsDefaultReplacedBundleResource());
                if (!isProbablyCorrect) {
                    Logger.Warn("Resource file at the bundle root. Unable to guarantee correctly determined native path. Please recheck the generated bundle file list.\nFile: " + localFile);
                }
            }
        }

        bundle.Touch();
        BundleManager.SetBundleActive(bundle, true);
        SetBundle(bundle.Name);
        SaveBundle(true);
        BundleManager.UninitializedBundleFolders.Remove(bundlePath);
        SetBundle(originalBundle?.Name);
    }

    public void RescanFilesInBundle(Bundle bundle)
    {
        var folder = BundleManager.GetBundleFolder(bundle);
        var filesAdded = false;
        foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)) {
            var localPath = Path.GetRelativePath(folder, file).NormalizeFilepath();
            if (bundle.TryFindResourceByLocalPath(localPath, out var item, out var storedLocalPath)) {
                if (storedLocalPath != localPath) {
                    if (!OperatingSystem.IsWindows() && File.Exists(Path.Combine(folder, storedLocalPath))) {
                        Logger.Warn($"""
                            Found files in bundle with only difference in casing:
                            Previous path: {storedLocalPath}
                            New path: {localPath}

                            This is not supported and only one file will be resolved.
                            """);
                    } else {
                        Logger.Info($"""
                            Updating file path casing:
                            Previous path: {storedLocalPath}
                            New path: {localPath}
                            """);
                        bundle.AddResource(localPath, item.Target, item.Replace);
                        filesAdded = true;
                    }
                }
            } else {
                var format = PathUtils.ParseFileFormat(localPath);
                if (format.format != KnownFileFormats.Unknown) {
                    Logger.Info($"Found new file in active bundle {localPath}, adding to bundle");
                    if (ResourceManager.TryForceLoadFile(file, out var handle)) {
                        handle.Dispose();
                        var originalTargetPath = Env.ListFile?.GetFiles(localPath).FirstOrDefault();
                        bundle.AddResource(localPath, Env.RemoveBasePath(originalTargetPath ?? localPath).ToString(), format.format.IsDefaultReplacedBundleResource());
                        filesAdded = true;
                    } else {
                        Logger.Warn($"Unable to open new bundle file {localPath}, ignoring");
                    }
                } else if (localPath.StartsWith("reframework")) {
                    bundle.AddResource(localPath, localPath, true);
                    filesAdded = true;
                }
            }
        }

        var filesMissing = false;
        foreach (var localPath in bundle.ResourceLocalPaths) {
            var fullPath = Path.Combine(folder, localPath);
            if (!File.Exists(fullPath)) {
                Logger.Warn($"File {localPath} is missing from the bundle folder and may have been deleted or moved manually. If this was intended, you can delete it properly from the bundle manager.");
                filesMissing = true;
            }
        }

        if (filesAdded) {
            bundle.Save();
        }
        if (!filesAdded && !filesMissing) {
            Logger.Info("No files seem to have been added to or removed from the bundle folder.");
        }
    }

    public override string ToString() => Data.Name ?? "New Workspace";

    public void Dispose()
    {
        ResourceManager?.Dispose();
    }
}

// serialized
public class ContentWorkspaceData
{
    public string? Name { get; set; }
    public string? ContentBundle { get; set; }
    public List<WindowData> Windows { get; set; } = new();
}
