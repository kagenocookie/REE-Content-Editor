namespace ContentEditor.Core;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

public class BundleManager
{
    public List<Bundle> AllBundles { get; } = new();
    private List<Bundle>? _activeBundles;
    public List<Bundle> ActiveBundles {
        get {
            if (_activeBundles == null) {
                LoadDataBundles();
            }
            return _activeBundles ??= new();
        }
    }
    public List<SerializedEnum> Enums { get; } = new();

    public List<string> UninitializedBundleFolders { get; } = new();

    public event Action<Dictionary<string, Bundle>>? EntitiesCreated;
    public event Action<Dictionary<string, Bundle>>? EntitiesUpdated;

    public string? GamePath { get; set; }
    public string? VersionHash { get; set; }

    public string BundlePath => Path.Combine(GamePath ?? string.Empty, "reframework/data/usercontent/bundles/").Replace('\\', '/');
    public string EnumsPath => Path.Combine(GamePath ?? string.Empty, "reframework/data/usercontent/enums/").Replace('\\', '/');
    public string SettingsPath => Path.Combine(GamePath ?? string.Empty, "reframework/data/usercontent/editor_settings.json").Replace('\\', '/');
    public string ResourcePatchLogPath => Path.Combine(GamePath ?? string.Empty, "reframework/content_patch_metadata.json").Replace('\\', '/');

    private EditorSettings? settings;

    public bool IsLoaded => settings != null;

    private readonly struct ImportSummary(Dictionary<string, Bundle> created, Dictionary<string, Bundle> updated)
    {
        public Dictionary<string, Bundle> Created => created;
        public Dictionary<string, Bundle> Updated => updated;
    }

    public void LoadSettings()
    {
        if (!TryDeserialize<EditorSettings>(SettingsPath, out settings)) {
            settings = new EditorSettings();
        }
        settings.BundleOrder ??= new();
    }

    public void SaveSettings()
    {
        using var fs = File.Create(SettingsPath);
        JsonSerializer.Serialize(fs, settings, JsonConfig.luaJsonOptions);
    }

    public void LoadDataBundles()
    {
        AllBundles.Clear();
        _activeBundles ??= new();
        _activeBundles.Clear();
        if (settings == null) LoadSettings();
        if (settings == null) throw new Exception();

        var orderedBundles = new SortedList<int, Bundle>();
        var unorderedBundles = new List<Bundle>();
        var bundleImports = new ImportSummary(new (), new());
        Directory.CreateDirectory(BundlePath);
        UninitializedBundleFolders.Clear();
        foreach (var entry in Directory.EnumerateFileSystemEntries(BundlePath)) {
            var finfo = new FileInfo(entry);
            var isFolder = (finfo.Attributes & FileAttributes.Directory) != 0;
            string bundleJsonFile;
            if (isFolder) {
                // CE.NET+ bundle
                bundleJsonFile = Path.Combine(entry, "bundle.json");
                // maybe handle folders with no bundle as a raw "resource-only" bundle instead? (and autogenerate the json)
                if (!File.Exists(bundleJsonFile)) {
                    if (Directory.EnumerateFiles(entry).Any()) {
                        UninitializedBundleFolders.Add(entry);
                    }
                    continue;
                }
            } else if (entry.EndsWith(".json")) {
                bundleJsonFile = entry;
            } else {
                // not a bundle
                continue;
            }

            if (!TryDeserialize<Bundle>(bundleJsonFile, out var bundle)) {
                Logger.Error("Failed to open bundle " + bundleJsonFile);
                continue;
            }
            if (string.IsNullOrEmpty(bundle.Name)) {
                Logger.Error("Found invalid, unnamed bundle " + bundleJsonFile);
                continue;
            }
            bundle.SaveFilepath = Path.GetFileNameWithoutExtension(entry);

            var idx = settings.BundleOrder.IndexOf(bundle.Name);
            if (idx != -1) {
                orderedBundles.Add(idx, bundle);
            } else {
                unorderedBundles.Add(bundle);
            }
        }

        foreach (var bundle in orderedBundles.Values) {
            if (settings.BundleSettings.TryGetValue(bundle.Name, out var bundleSettings)) {
                if (!bundleSettings.Disabled) {
                    _activeBundles.Add(bundle);
                }
            } else {
                _activeBundles.Add(bundle);
            }
            AllBundles.Add(bundle);
        }

        foreach (var bundle in unorderedBundles) {
            _activeBundles.Add(bundle);
            settings.BundleOrder.Add(bundle.Name);
            AllBundles.Add(bundle);
        }

        if (unorderedBundles.Count > 0) {
            SaveSettings();
        }

        RefreshEnums();
        if (bundleImports.Created.Count > 0) EntitiesCreated?.Invoke(bundleImports.Created);
        if (bundleImports.Updated.Count > 0) EntitiesUpdated?.Invoke(bundleImports.Updated);
    }

    private void RefreshEnums()
    {
        Directory.CreateDirectory(EnumsPath);
        foreach (var file in Directory.EnumerateFiles(EnumsPath, "*.json")) {
            if (TryDeserialize<SerializedEnum>(file, out var data)) {
                Enums.Add(data);
            }
        }
    }

    private static bool TryDeserialize<T>(string filepath, [MaybeNullWhen(false)] out T data)
    {
        if (!File.Exists(filepath)) {
            data = default;
            return false;
        }
        using var fs = File.OpenRead(filepath);
        try {
            data = JsonSerializer.Deserialize<T>(fs, JsonConfig.luaJsonOptions);
            return data != null;
        } catch (Exception e) {
            Logger.Error($"Failed to deserialize {filepath} as {typeof(T)}: {e.Message}");
            data = default;
            return false;
        }
    }

    public Bundle? GetBundle(string bundleName, bool? active)
    {
        if (_activeBundles == null) LoadDataBundles();
        var bundleList = active == null ? AllBundles : active == true ? ActiveBundles : AllBundles.Where(b => !ActiveBundles.Contains(b));
        return bundleList.FirstOrDefault(b => b.Name == bundleName);
    }

    public string GetBundleFolder(Bundle bundle)
    {
        return Path.Combine(BundlePath, bundle.Name).Replace('\\', '/');
    }

    public string? GetBundleResourcePath(Bundle bundle, string filepath)
    {
        if (bundle.ResourceListing == null) return null;
        if (bundle.TryFindResourceByNativePath(filepath, out var localPath)) {
            return ResolveBundleLocalPath(bundle, localPath);
        }

        return null;
    }

    public string ResolveBundleLocalPath(Bundle bundle, string localPath)
    {
        return Path.Combine(BundlePath, bundle.Name, localPath);
    }

    public bool IsBundleActive(Bundle bundle)
    {
        return ActiveBundles.Contains(bundle);
    }

    public void SwapBundleOrders(Bundle bundle1, Bundle bundle2)
    {
        var idx1 = AllBundles.IndexOf(bundle1);
        var idx2 = AllBundles.IndexOf(bundle2);
        if (idx1 != -1 && idx2 != -1) {
            AllBundles[idx1] = bundle2;
            AllBundles[idx2] = bundle1;
        }

        idx1 = ActiveBundles.IndexOf(bundle1);
        idx2 = ActiveBundles.IndexOf(bundle2);
        if (idx1 != -1 && idx2 != -1) {
            ActiveBundles[idx1] = bundle2;
            ActiveBundles[idx2] = bundle1;
        }

        if (settings == null) return;

        idx1 = settings.BundleOrder.IndexOf(bundle1.Name);
        idx2 = settings.BundleOrder.IndexOf(bundle2.Name);
        if (idx1 != -1 && idx2 != -1) {
            settings.BundleOrder[idx1] = bundle2.Name;
            settings.BundleOrder[idx2] = bundle1.Name;
        }
    }

    public void SetBundleActive(Bundle bundle, bool active)
    {
        if (active == IsBundleActive(bundle)) return;

        if (settings == null) LoadSettings();
        if (!settings!.BundleSettings.TryGetValue(bundle.Name, out var set)) {
            settings.BundleSettings[bundle.Name] = set = new();
        }
        set.Disabled = !active;

        if (!active) {
            ActiveBundles.Remove(bundle);
            SaveSettings();
            return;
        }

        // ensure the active bundles are inserted in the load order by comparing other active bundle indices
        var bundleIndex = AllBundles.IndexOf(bundle);
        if (bundleIndex == -1) {
            AllBundles.Add(bundle);
            ActiveBundles.Add(bundle);
            return;
        }

        var lowerIndexActive = AllBundles.Where((b, i) => i < bundleIndex && IsBundleActive(b)).FirstOrDefault();
        if (lowerIndexActive != null) {
            var index = ActiveBundles.IndexOf(lowerIndexActive);
            ActiveBundles.Insert(index + 1, bundle);
            return;
        }

        var higherIndexActive = AllBundles.Where((b, i) => i > bundleIndex && IsBundleActive(b)).FirstOrDefault();
        if (higherIndexActive != null) {
            var index = ActiveBundles.IndexOf(higherIndexActive);
            ActiveBundles.Insert(index, bundle);
            return;
        }

        ActiveBundles.Add(bundle);
        SaveSettings();
    }

    public Bundle? CreateBundle(string newBundleName)
    {
        var existing = GetBundle(newBundleName, null);
        if (existing != null) return null;

        var bundle = new Bundle() {
            Name = newBundleName,
            Author = GetEditorRootSetting("author_name")?.GetString() ?? "<unknown>",
            Description = GetEditorRootSetting("author_description")?.GetString() ?? "<unknown>",
            GameVersion = VersionHash,
        };
        bundle.Touch();
        AllBundles.Add(bundle);
        ActiveBundles.Add(bundle);
        return bundle;
    }

    public void SaveBundle(Bundle bundle)
    {
        bundle.Touch();
        bundle.SaveFilepath ??= bundle.Name;
        var outfilepath = Path.Combine(BundlePath, bundle.SaveFilepath, "bundle.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outfilepath)!);
        using var fs = File.Create(outfilepath);
        JsonSerializer.Serialize(fs, bundle, JsonConfig.luaJsonOptions);
    }

    public BundleManager GetBundleSpecificManager(string? bundleName)
    {
        if (bundleName == null) {
            return this;
        }
        var manager = new BundleManager() { GamePath = GamePath, VersionHash = VersionHash };
        manager._activeBundles = new();
        var bundle = GetBundle(bundleName, null);
        if (bundle == null) {
            Logger.Info($"Bundle not found: {bundleName}. Creating new bundle");
            bundle = CreateBundle(bundleName);
            if (bundle == null) {
                Logger.Error($"Could not create bundle {bundleName}");
                return this;
            }
        }

        manager.AllBundles.AddRange(AllBundles);
        if (bundle.DependsOn?.Count > 0) {
            foreach (var dep in bundle.DependsOn) {
                var depBundle = GetBundle(dep, null);
                if (depBundle == null) {
                    WindowManager.Instance.ShowError($"Missing dependency bundle {dep}!\nData may not be correct, please check your installed content bundles.");
                    continue;
                }

                manager._activeBundles.Add(depBundle);
            }
        }
        manager._activeBundles.Add(bundle);
        manager.settings = settings;
        manager.Enums.AddRange(Enums);
        return manager;
    }

    private JsonElement? GetEditorRootSetting(string propName) => settings?.IngameEditor?.RootElement.TryGetProperty(propName, out var prop) == true ? prop : null;
}
