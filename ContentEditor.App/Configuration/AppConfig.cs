using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using ContentEditor.App;
using ContentEditor.Core;
using ContentPatcher;
using ContentPatcher.FileFormats;
using ReeLib;

namespace ContentEditor;

public class AppConfig : Singleton<AppConfig>
{
    public static class Keys
    {
        public const string IsFirstTime = "first_time";
        public const string MaxFps = "max_fps";
        public const string BackgroundMaxFps = "max_fps_background";
        public const string MainWindowGame = "main_selected_game";
        public const string MainActiveBundle = "main_active_bundle";
        public const string BlenderPath = "blender_path";
        public const string RemoteDataSource = "remote_data_source";
        public const string UnpackMaxThreads = "unpack_max_threads";
        public const string GameConfigBaseFilepath = "game_configs_base_filepath";
        public const string BackgroundColor = "background_color";
        public const string LogLevel = "log_level";
        public const string LogToFile = "log_to_file";
        public const string ShowFps = "show_fps";
        public const string MaxUndoSteps = "max_undo_steps";
        public const string PrettyLabels = "pretty_labels";
        public const string RecentFiles = "recent_files";
        public const string LoadFromNatives = "load_natives";
        public const string BundleDefaultSaveFullPath = "bundle_save_full_path";
        public const string Theme = "theme";
        public const string LatestVersion = "latest_version";
        public const string EnableUpdateCheck = "enable_update_check";
        public const string EnableKeyboardNavigation = "enable_keyboard_nav";
        public const string LastUpdateCheck = "last_update_check";
        public const string AutoExpandFieldsCount = "auto_expand_fields_count";
        public const string FontSize = "font_size";
        public const string UsePakFilePreviewWindow = "use_pak_preview_window";
        public const string UsePakCompactFilePaths = "use_pak_compact_file_paths";
        public const string PakDisplayMode = "pak_display_mode";
        public const string ThumbnailCacheFilepath = "thumbnail_cache_path";
        public const string WindowRect = "window_rect";

        public const string RenderAxis = "render_axis";
        public const string RenderMeshes = "render_meshes";
        public const string RenderColliders = "render_colliders";
        public const string RenderRequestSetColliders = "render_rcol";
        public const string RenderLights = "render_lights";

        public const string Key_Undo = "key_undo";
        public const string Key_Redo = "key_redo";
        public const string Key_Save = "key_save";
        public const string Key_Back = "key_back";

        public const string Gamepath = "game_path";
        public const string GameExtractPath = "game_extract_path";
        public const string RszJsonPath = "rsz_json_path";
        public const string FilelistPath = "file_list_path";
    }

    private class AppGameConfig
    {
        public string gamepath = string.Empty;
        public string? gameExtractPath;

        public string? filelist;
        public string? rszJson;
    }

    private static readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

    private const string IniFilename = "ce_config.ini";
    private const string JsonFilename = "ce_config.json";

    public static readonly string Version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion ?? "";
    public static bool IsOutdatedVersion { get; internal set; }
    public static AppJsonSettings Settings => Instance.JsonSettings;

    public sealed class SettingWrapper<T>(string settingKey, ReaderWriterLockSlim _lock, T initial) where T : struct, IEquatable<T>
    {
        internal T value = initial;

        private readonly T Initial = initial;
        public readonly string SettingKey = settingKey;

        public event Action<T>? ValueChanged;

        public bool IsInitial => Initial.Equals(value);
        public T Get() => _lock.Read(() => value);
        public void Set(T value) => SetAndSave(value, (v) => {
            this.value = value;
            ValueChanged?.Invoke(v);
        });
        public void Reset() => Set(Initial);

        public override string ToString() => Get().ToString()!;

        public static implicit operator T(SettingWrapper<T> vv) => vv.Get();
    }

    public sealed class ClassSettingWrapper<T>(string settingKey, ReaderWriterLockSlim _lock, Func<T>? initial = default) where T : class
    {
        internal T? value = initial?.Invoke() ?? default;

        private readonly T? Initial = initial?.Invoke() ?? default;
        public readonly string SettingKey = settingKey;

        public event Action<T?>? ValueChanged;

        public bool IsInitial => value == null ? Initial == null : value.Equals(Initial);
        public T? Get() => _lock.Read(() => value);
        public void Set(T? value) => SetAndSave(value, (v) => {
            this.value = value;
            ValueChanged?.Invoke(v);
        });
        public void Reset() => Set(Initial);

        public override string? ToString() => Get()?.ToString();

        public static implicit operator T?(ClassSettingWrapper<T> vv) => vv.Get();
    }

    private Dictionary<string, AppGameConfig> gameConfigs = new();

    public static readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "REE-Content-Editor");

    private static readonly string IniFilepath = Path.Combine(AppDataPath, IniFilename);
    private static readonly string JsonFilepath = Path.Combine(AppDataPath, JsonFilename);

    public readonly SettingWrapper<int> MaxFps = new SettingWrapper<int>(Keys.MaxFps, _lock, 60);
    public readonly SettingWrapper<int> BackgroundMaxFps = new SettingWrapper<int>(Keys.BackgroundMaxFps, _lock, 30);
    public readonly ClassSettingWrapper<string> MainSelectedGame = new ClassSettingWrapper<string>(Keys.MainWindowGame, _lock);
    public readonly ClassSettingWrapper<string> MainActiveBundle = new ClassSettingWrapper<string>(Keys.MainActiveBundle, _lock);
    public readonly ClassSettingWrapper<string> BlenderPath = new ClassSettingWrapper<string>(Keys.BlenderPath, _lock);
    public readonly ClassSettingWrapper<string> RemoteDataSource = new ClassSettingWrapper<string>(Keys.RemoteDataSource, _lock);
    public readonly ClassSettingWrapper<string> GameConfigBaseFilepath = new ClassSettingWrapper<string>(Keys.GameConfigBaseFilepath, _lock);
    public readonly ClassSettingWrapper<string> ThumbnailCacheFilepath = new ClassSettingWrapper<string>(Keys.ThumbnailCacheFilepath, _lock, () => Path.Combine(AppDataPath, "thumbs"));
    public readonly ClassSettingWrapper<string> Theme = new ClassSettingWrapper<string>(Keys.Theme, _lock, () => "default");
    public readonly SettingWrapper<int> UnpackMaxThreads = new SettingWrapper<int>(Keys.UnpackMaxThreads, _lock, 4);
    public readonly SettingWrapper<ReeLib.via.Color> BackgroundColor = new SettingWrapper<ReeLib.via.Color>(Keys.BackgroundColor, _lock, new ReeLib.via.Color(115, 140, 153, 255));
    public readonly SettingWrapper<bool> PrettyFieldLabels = new SettingWrapper<bool>(Keys.PrettyLabels, _lock, true);
    public readonly SettingWrapper<int> LogLevel = new SettingWrapper<int>(Keys.LogLevel, _lock, 1);
    public readonly SettingWrapper<int> MaxUndoSteps = new SettingWrapper<int>(Keys.MaxUndoSteps, _lock, 250);
    public readonly SettingWrapper<int> AutoExpandFieldsCount = new SettingWrapper<int>(Keys.AutoExpandFieldsCount, _lock, 3);
    public readonly SettingWrapper<int> FontSize = new SettingWrapper<int>(Keys.FontSize, _lock, 20);
    public readonly SettingWrapper<bool> EnableUpdateCheck = new SettingWrapper<bool>(Keys.EnableUpdateCheck, _lock, true);
    public readonly SettingWrapper<bool> EnableKeyboardNavigation = new SettingWrapper<bool>(Keys.EnableKeyboardNavigation, _lock, true);
    public readonly SettingWrapper<bool> UsePakFilePreviewWindow = new SettingWrapper<bool>(Keys.UsePakFilePreviewWindow, _lock, true);
    public readonly SettingWrapper<bool> UsePakCompactFilePaths = new SettingWrapper<bool>(Keys.UsePakCompactFilePaths, _lock, true);
    public readonly SettingWrapper<bool> ShowFps = new SettingWrapper<bool>(Keys.ShowFps, _lock, false);
    public readonly SettingWrapper<bool> LogToFile = new SettingWrapper<bool>(Keys.LogToFile, _lock, true);
    public readonly SettingWrapper<bool> IsFirstTime = new SettingWrapper<bool>(Keys.IsFirstTime, _lock, true);
    public readonly SettingWrapper<bool> LoadFromNatives = new SettingWrapper<bool>(Keys.LoadFromNatives, _lock, false);
    public readonly SettingWrapper<bool> BundleDefaultSaveFullPath = new SettingWrapper<bool>(Keys.BundleDefaultSaveFullPath, _lock, false);
    public readonly SettingWrapper<Vector4> WindowRect = new SettingWrapper<Vector4>(Keys.WindowRect, _lock, new Vector4(50, 50, 1280, 720));
    public readonly SettingWrapper<DateTime> LastUpdateCheck = new SettingWrapper<DateTime>(Keys.LastUpdateCheck, _lock, DateTime.MinValue);
    public readonly ClassSettingWrapper<string> LatestVersion = new ClassSettingWrapper<string>(Keys.LatestVersion, _lock);

    public readonly SettingWrapper<int> PakDisplayModeValue = new SettingWrapper<int>(Keys.LogToFile, _lock, (int)FileDisplayMode.List);
    public FileDisplayMode PakDisplayMode { get => (FileDisplayMode)PakDisplayModeValue.Get(); set => PakDisplayModeValue.Set((int)value); }

    public readonly SettingWrapper<bool> RenderAxis = new SettingWrapper<bool>(Keys.RenderAxis, _lock, true);
    public readonly SettingWrapper<bool> RenderMeshes = new SettingWrapper<bool>(Keys.RenderMeshes, _lock, true);
    public readonly SettingWrapper<bool> RenderColliders = new SettingWrapper<bool>(Keys.RenderColliders, _lock, true);
    public readonly SettingWrapper<bool> RenderRequestSetColliders = new SettingWrapper<bool>(Keys.RenderRequestSetColliders, _lock, true);
    public readonly SettingWrapper<bool> RenderLights = new SettingWrapper<bool>(Keys.RenderLights, _lock, true);

    public readonly SettingWrapper<KeyBinding> Key_Undo = new SettingWrapper<KeyBinding>(Keys.Key_Undo, _lock, new KeyBinding(ImGuiKey.Z, ctrl: true));
    public readonly SettingWrapper<KeyBinding> Key_Redo = new SettingWrapper<KeyBinding>(Keys.Key_Redo, _lock, new KeyBinding(ImGuiKey.Y, ctrl: true));
    public readonly SettingWrapper<KeyBinding> Key_Save = new SettingWrapper<KeyBinding>(Keys.Key_Save, _lock, new KeyBinding(ImGuiKey.S, ctrl: true));
    public readonly SettingWrapper<KeyBinding> Key_Back = new SettingWrapper<KeyBinding>(Keys.Key_Back, _lock, new KeyBinding(ImGuiKey.Backspace));

    public string ConfigBasePath => GameConfigBaseFilepath.Get() ?? "configs";

    public AppJsonSettings JsonSettings { get; private set; } = new();

    public string? GetGamePath(GameIdentifier game) => _lock.Read(() => gameConfigs.GetValueOrDefault(game.name)?.gamepath);
    public void SetGamePath(GameIdentifier game, string path) => SetForGameAndSave(game.name, path, (cfg, val) => cfg.gamepath = val);
    public string? GetGameExtractPath(GameIdentifier game) => _lock.Read(() => gameConfigs.GetValueOrDefault(game.name)?.gameExtractPath);
    public void SetGameExtractPath(GameIdentifier game, string path) => SetForGameAndSave(game.name, path, (cfg, val) => cfg.gameExtractPath = val);
    public string? GetGameRszJsonPath(GameIdentifier game) => _lock.Read(() => gameConfigs.GetValueOrDefault(game.name)?.rszJson);
    public void SetGameRszJsonPath(GameIdentifier game, string path) => SetForGameAndSave(game.name, path, (cfg, val) => cfg.rszJson = val);
    public string? GetGameFilelist(GameIdentifier game) => _lock.Read(() => gameConfigs.GetValueOrDefault(game.name)?.filelist);
    public void SetGameFilelist(GameIdentifier game, string path) => SetForGameAndSave(game.name, path, (cfg, val) => cfg.filelist = val);
    public bool HasAnyGameConfigured => _lock.Read(() => gameConfigs.Any(g => !string.IsNullOrEmpty(g.Value?.gamepath)));
    public IEnumerable<string> ConfiguredGames => _lock.Read(() => gameConfigs.Where(kv => !string.IsNullOrEmpty(kv.Value.gamepath)).Select(kv => kv.Key));

    public void AddRecentFile(string file) => AddRecentString(file, JsonSettings.RecentFiles, 25);
    public void AddRecentBundle(string file) => AddRecentString(file, JsonSettings.RecentBundles, 25);
    public void AddRecentRcol(string file) => AddRecentString(file, JsonSettings.RecentRcols, 15);
    public void AddRecentMotlist(string file) => AddRecentString(file, JsonSettings.RecentMotlists, 15);
    public void AddRecentNavmesh(string file) => AddRecentString(file, JsonSettings.RecentNavmeshes, 15);
    private void AddRecentString(string file, List<string> list, int maxEntries)
    {
        var prevIndex = list.IndexOf(file);
        if (prevIndex == 0) return;

        if (prevIndex != -1) {
            list.RemoveAt(prevIndex);
            list.Insert(0, file);
        } else {
            list.Insert(0, file);
            if (list.Count > maxEntries) {
                list.RemoveAt(list.Count - 1);
            }
        }
        SaveJsonConfig();
    }

    public List<(string name, bool supported)> GetGamelist()
    {
        var names = Enum.GetNames<GameName>().Except([nameof(GameName.unknown)]).Select(name => (name, false)).ToList();
        var configured = _lock.Read(() => gameConfigs.Where(kv => !string.IsNullOrEmpty(kv.Value.gamepath)).Select(kv => kv.Key));
        foreach (var conf in configured) {
            var index = names.IndexOf((conf, false));
            if (index == -1) {
                names.Add((conf, true));
            } else {
                names[index] = (conf, true);
            }
        }
        return names;
    }

    private static void SetAndSave<TValue>(TValue value, Action<TValue> func)
    {
        _lock.Write(value, func);
        _lock.ReadCallback(SaveConfigToIni);
    }

    private static void SetForGameAndSave<TValue>(string game, TValue value, Action<AppGameConfig, TValue> func)
    {
        _lock.EnterWriteLock();
        try {
            var instance = Instance;
            if (!instance.gameConfigs.TryGetValue(game, out var data)){
                instance.gameConfigs[game] = data = new ();
            }
            func.Invoke(data, value);
        } finally {
            _lock.ExitWriteLock();
        }
        _lock.ReadCallback(SaveConfigToIni);
    }

    public void SaveJsonConfig()
    {
        using var fs = File.Create(JsonFilename);
        JsonSerializer.Serialize(fs, JsonSettings, JsonConfig.configJsonOptions);
    }

    public void LoadJsonConfig()
    {
        var path = JsonFilepath;
        if (!File.Exists(path)) {
            if (!File.Exists(JsonFilename)) return;
            path = JsonFilename;
        }

        using var fs = File.OpenRead(path);
        try {
            var settings = JsonSerializer.Deserialize<AppJsonSettings>(fs, JsonConfig.configJsonOptions);
            if (settings == null) throw new Exception("Null settings");
            JsonSettings = settings;
        } catch (Exception e) {
            Logger.Error("Failed to load app settings from JSON: " + e.Message);
        }
    }

    public static void SaveConfigToIni()
    {
        var instance = Instance;
        var items = new List<(string, string, string?)>() {
            (Keys.MaxFps, instance.MaxFps.value.ToString(CultureInfo.InvariantCulture), null),
            (Keys.BackgroundMaxFps, instance.BackgroundMaxFps.value.ToString(CultureInfo.InvariantCulture), null),
            (Keys.ShowFps, instance.ShowFps.value.ToString(), null),
            (Keys.LogToFile, instance.LogToFile.value.ToString(), null),
            (Keys.IsFirstTime, instance.IsFirstTime.value.ToString(), null),
            (Keys.LoadFromNatives, instance.LoadFromNatives.value.ToString(), null),
            (Keys.BundleDefaultSaveFullPath, instance.BundleDefaultSaveFullPath.value.ToString(), null),
            (Keys.MainWindowGame, instance.MainSelectedGame.value?.ToString() ?? "", null),
            (Keys.MainActiveBundle, instance.MainActiveBundle.value?.ToString() ?? "", null),
            (Keys.BlenderPath, instance.BlenderPath.value?.ToString() ?? "", null),
            (Keys.UnpackMaxThreads, instance.UnpackMaxThreads.value.ToString(), null),
            (Keys.RemoteDataSource, instance.RemoteDataSource.value?.ToString() ?? "", null),
            (Keys.GameConfigBaseFilepath, instance.GameConfigBaseFilepath.value?.ToString() ?? "", null),
            (Keys.ThumbnailCacheFilepath, instance.ThumbnailCacheFilepath.value?.ToString() ?? "", null),
            (Keys.Theme, instance.Theme.value?.ToString() ?? "", null),
            (Keys.BackgroundColor, instance.BackgroundColor.value.ToString(), null),
            (Keys.LogLevel, instance.LogLevel.value.ToString(), null),
            (Keys.MaxUndoSteps, instance.MaxUndoSteps.value.ToString(), null),
            (Keys.AutoExpandFieldsCount, instance.AutoExpandFieldsCount.value.ToString(), null),
            (Keys.FontSize, instance.FontSize.value.ToString(), null),
            (Keys.WindowRect, instance.WindowRect.value.ToString("", CultureInfo.InvariantCulture).Replace("<", "").Replace(">", ""), null),
            (Keys.LastUpdateCheck, instance.LastUpdateCheck.value.ToString("O"), null),
            (Keys.LatestVersion, instance.LatestVersion.value?.ToString() ?? "", null),
            (Keys.EnableUpdateCheck, instance.EnableUpdateCheck.value.ToString(), null),
            (Keys.EnableKeyboardNavigation, instance.EnableKeyboardNavigation.value.ToString(), null),
            (Keys.UsePakFilePreviewWindow, instance.UsePakFilePreviewWindow.value.ToString(), null),
            (Keys.UsePakCompactFilePaths, instance.UsePakCompactFilePaths.value.ToString(), null),
            (Keys.PakDisplayMode, instance.PakDisplayModeValue.value.ToString(), null),
            (Keys.PrettyLabels, instance.PrettyFieldLabels.value.ToString(), null),

            (Keys.RenderAxis, instance.RenderAxis.value.ToString(), null),
            (Keys.RenderMeshes, instance.RenderMeshes.value.ToString(), null),
            (Keys.RenderColliders, instance.RenderColliders.value.ToString(), null),
            (Keys.RenderRequestSetColliders, instance.RenderRequestSetColliders.value.ToString(), null),
            (Keys.RenderLights, instance.RenderLights.value.ToString(), null),

            (Keys.Key_Undo, instance.Key_Undo.value.ToString(), "Keys"),
            (Keys.Key_Redo, instance.Key_Redo.value.ToString(), "Keys"),
            (Keys.Key_Save, instance.Key_Save.value.ToString(), "Keys"),
            (Keys.Key_Back, instance.Key_Back.value.ToString(), "Keys"),
        };
        foreach (var (game, data) in instance.gameConfigs) {
            if (!string.IsNullOrEmpty(data.gamepath)) {
                items.Add((Keys.Gamepath, data.gamepath, game));
            }
            if (!string.IsNullOrEmpty(data.gameExtractPath)) {
                items.Add((Keys.GameExtractPath, data.gameExtractPath, game));
            }
            if (!string.IsNullOrEmpty(data.filelist)) {
                items.Add((Keys.FilelistPath, data.filelist, game));
            }
            if (!string.IsNullOrEmpty(data.rszJson)) {
                items.Add((Keys.RszJsonPath, data.rszJson, game));
            }
        }
        IniFile.WriteToFile(IniFilepath, items);
    }

    public static void LoadConfigs()
    {
        if (!File.Exists(IniFilepath)) {
            // migrate legacy configs to new path
            Instance.LoadConfigs(IniFile.ReadFile(IniFilename));
        } else {
            Instance.LoadConfigs(IniFile.ReadFile(IniFilepath));
        }
        Instance.LoadJsonConfig();
    }

    private void LoadConfigs(IEnumerable<(string key, string value, string? group)> values)
    {
        static bool ReadBool(string str) => str.Equals("true", StringComparison.OrdinalIgnoreCase) || str.Equals("yes", StringComparison.OrdinalIgnoreCase) || str == "1";
        static string? ReadString(string str) => string.IsNullOrEmpty(str) ? null : str;

        _lock.EnterWriteLock();
        try {
            foreach (var (key, value, group) in values) {
                if (group == null) {
                    switch (key) {
                        case Keys.MaxFps:
                            MaxFps.value = Math.Max(int.Parse(value), 10);
                            break;
                        case Keys.BackgroundMaxFps:
                            BackgroundMaxFps.value = Math.Clamp(int.Parse(value), 5, MaxFps.value);
                            break;
                        case Keys.ShowFps:
                            ShowFps.value = ReadBool(value);
                            break;
                        case Keys.LogToFile:
                            LogToFile.value = ReadBool(value);
                            break;
                        case Keys.IsFirstTime:
                            IsFirstTime.value = ReadBool(value);
                            break;
                        case Keys.LoadFromNatives:
                            LoadFromNatives.value = ReadBool(value);
                            break;
                        case Keys.BundleDefaultSaveFullPath:
                            BundleDefaultSaveFullPath.value = ReadBool(value);
                            break;
                        case Keys.MainWindowGame:
                            MainSelectedGame.value = ReadString(value);
                            break;
                        case Keys.MainActiveBundle:
                            MainActiveBundle.value = ReadString(value);
                            break;
                        case Keys.BlenderPath:
                            BlenderPath.value = ReadString(value);
                            break;
                        case Keys.RemoteDataSource:
                            RemoteDataSource.value = ReadString(value);
                            break;
                        case Keys.GameConfigBaseFilepath:
                            GameConfigBaseFilepath.value = ReadString(value);
                            break;
                        case Keys.ThumbnailCacheFilepath:
                            ThumbnailCacheFilepath.value = ReadString(value);
                            break;
                        case Keys.Theme:
                            Theme.value = string.IsNullOrEmpty(value) ? "default" : value;
                            break;
                        case Keys.UnpackMaxThreads:
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) UnpackMaxThreads.value = Math.Clamp(parsed, 1, 64);
                            break;
                        case Keys.BackgroundColor:
                            if (ReeLib.via.Color.TryParse(value, out var _col)) BackgroundColor.value =  _col;
                            break;
                        case Keys.LogLevel:
                            if (int.TryParse(value, out var _intvalue)) LogLevel.value =  _intvalue;
                            break;
                        case Keys.MaxUndoSteps:
                            if (int.TryParse(value, out _intvalue)) MaxUndoSteps.value = Math.Max(_intvalue, 0);
                            break;
                        case Keys.AutoExpandFieldsCount:
                            if (int.TryParse(value, out _intvalue)) AutoExpandFieldsCount.value = Math.Max(_intvalue, 0);
                            break;
                        case Keys.FontSize:
                            if (int.TryParse(value, out _intvalue)) FontSize.value = Math.Max(_intvalue, 10);
                            break;
                        case Keys.PrettyLabels:
                            PrettyFieldLabels.value = ReadBool(value);
                            break;
                        case Keys.RecentFiles:
                            JsonSettings.RecentFiles.AddRange(value.Split('|', StringSplitOptions.RemoveEmptyEntries));
                            break;
                        case Keys.EnableUpdateCheck:
                            EnableUpdateCheck.value = ReadBool(value);
                            break;
                        case Keys.EnableKeyboardNavigation:
                            EnableKeyboardNavigation.value = ReadBool(value);
                            break;
                        case Keys.UsePakFilePreviewWindow:
                            UsePakFilePreviewWindow.value = ReadBool(value);
                            break;
                        case Keys.PakDisplayMode:
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) PakDisplayModeValue.value = Math.Clamp(parsed, (int)FileDisplayMode.List, (int)FileDisplayMode.Grid);
                            break;
                        case Keys.UsePakCompactFilePaths:
                            UsePakCompactFilePaths.value = ReadBool(value);
                            break;
                        case Keys.LastUpdateCheck:
                            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var _updateCheck)) LastUpdateCheck.value = _updateCheck;
                            break;
                        case Keys.WindowRect: {
                                var vals = value.Split(',');
                                var vec = new Vector4(
                                    float.Parse(vals[0].Trim(), CultureInfo.InvariantCulture),
                                    float.Parse(vals[1].Trim(), CultureInfo.InvariantCulture),
                                    float.Parse(vals[2].Trim(), CultureInfo.InvariantCulture),
                                    float.Parse(vals[3].Trim(), CultureInfo.InvariantCulture)
                                );
                                if (vec.Z > 0 && vec.W > 0) {
                                    WindowRect.value = vec;
                                }
                                break;
                            }
                        case Keys.LatestVersion:
                            LatestVersion.value = ReadString(value);
                            break;

                        case Keys.RenderAxis:
                            RenderAxis.value = ReadBool(value);
                            break;
                        case Keys.RenderMeshes:
                            RenderMeshes.value = ReadBool(value);
                            break;
                        case Keys.RenderColliders:
                            RenderColliders.value = ReadBool(value);
                            break;
                        case Keys.RenderRequestSetColliders:
                            RenderRequestSetColliders.value = ReadBool(value);
                            break;
                        case Keys.RenderLights:
                            RenderLights.value = ReadBool(value);
                            break;
                    }
                } else if (group == "Keys") {
                    switch (key) {
                        case Keys.Key_Undo:
                            if (KeyBinding.TryParse(value, out var _key)) Key_Undo.value = _key;
                            break;
                        case Keys.Key_Redo:
                            if (KeyBinding.TryParse(value, out _key)) Key_Redo.value = _key;
                            break;
                        case Keys.Key_Save:
                            if (KeyBinding.TryParse(value, out _key)) Key_Save.value = _key;
                            break;
                        case Keys.Key_Back:
                            if (KeyBinding.TryParse(value, out _key)) Key_Back.value = _key;
                            break;
                    }
                } else {
                    var config = gameConfigs.GetValueOrDefault(group);
                    if (config == null) {
                        gameConfigs[group] = config = new AppGameConfig();
                    }

                    switch (key) {
                        case Keys.Gamepath:
                            config.gamepath = value;
                            break;
                        case Keys.GameExtractPath:
                            config.gameExtractPath = value;
                            break;
                        case Keys.RszJsonPath:
                            config.rszJson = value;
                            break;
                        case Keys.FilelistPath:
                            config.filelist = value;
                            break;
                    }
                }
            }
        } finally {
            _lock.ExitWriteLock();
        }
    }
}

public struct KeyBinding : IEquatable<KeyBinding>
{
    public ImGuiKey Key;
    public bool ctrl;
    public bool shift;
    public bool alt;

    private ImGuiKey ModKey => Key | (ctrl ? ImGuiKey.ModCtrl : 0) | (shift ? ImGuiKey.ModShift : 0) | (alt ? ImGuiKey.ModAlt : 0);

    public KeyBinding(ImGuiKey key, bool ctrl = false, bool shift = false, bool alt = false)
    {
        Key = key;
        this.ctrl = ctrl;
        this.shift = shift;
        this.alt = alt;
    }

    public static bool TryParse(string str, out KeyBinding binding)
    {
        var plus = str.IndexOf('+');
        if (plus == -1) {
            if (Enum.TryParse<ImGuiKey>(str, out var key)) {
                binding = new KeyBinding(key);
                return true;
            }
        } else {
            var mods = str.AsSpan().Slice(plus + 1);
            if (Enum.TryParse<ImGuiKey>(str.AsSpan().Slice(0, plus).TrimEnd(), out var key)) {
                binding = new KeyBinding(key, ctrl: mods.Contains('C'), shift: mods.Contains('S'), alt: mods.Contains('A'));
                return true;
            }
        }
        binding = default;
        return false;
    }

    public bool IsPressed()
    {
        if (!ImGui.IsKeyPressed(Key)) return false;
        if (ctrl && !ImGui.IsKeyDown(ImGuiKey.ModCtrl)) return false;
        if (shift && !ImGui.IsKeyDown(ImGuiKey.ModShift)) return false;
        if (alt && !ImGui.IsKeyDown(ImGuiKey.ModAlt)) return false;
        return true;
    }

    public bool Equals(KeyBinding other) => other.Key == Key && other.ctrl == ctrl && other.shift == shift && other.alt == alt;

    public override string ToString() => ctrl || shift || alt ? $"{Key}+{(ctrl ? "C" : "")}{(shift ? "S" : "")}{(alt ? "A" : "")}" : Key.ToString();
}

public class AppJsonSettings
{
    public SceneViewSettings SceneView { get; init; } = new();
    public MeshViewerSettings MeshViewer { get; init; } = new();
    public ImportSettings Import { get; init; } = new();
    public List<string> RecentBundles { get; init; } = new();
    public List<string> RecentFiles { get; init; } = new();
    public List<string> RecentRcols { get; init; } = new();
    public List<string> RecentMotlists { get; init; } = new();
    public List<string> RecentNavmeshes { get; init; } = new();

    public void Save() => AppConfig.Instance.SaveJsonConfig();
}

public record SceneViewSettings
{
    public float MoveSpeed { get; set; } = 8f;
}

public record ImportSettings
{
    public float Scale { get; set; } = 1f;
    public bool ForceRootIdentity { get; set; } = true;
    public bool ConvertZToYUpRootRotation { get; set; } = false;
}

public record MeshViewerSettings
{
    public CameraProjection DefaultProjection { get; set; } = CameraProjection.Orthographic;
    public float MoveSpeed { get; set; } = 5f;
}
