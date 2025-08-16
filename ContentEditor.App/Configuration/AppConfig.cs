using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using ContentPatcher;
using ContentPatcher.FileFormats;
using ImGuiNET;
using ReeLib;

namespace ContentEditor;

public class AppConfig : Singleton<AppConfig>
{
    public static class Keys
    {
        public const string MaxFps = "max_fps";
        public const string MainWindowGame = "main_selected_game";
        public const string MainActiveBundle = "main_active_bundle";
        public const string BlenderPath = "blender_path";
        public const string RemoteDataSource = "remote_data_source";
        public const string UnpackMaxThreads = "unpack_max_threads";
        public const string GameConfigBaseFilepath = "game_configs_base_filepath";
        public const string BackgroundColor = "background_color";
        public const string LogLevel = "log_level";
        public const string MaxUndoSteps = "max_undo_steps";
        public const string PrettyLabels = "pretty_labels";
        public const string RecentFiles = "recent_files";
        public const string Theme = "theme";
        public const string LatestVersion = "latest_version";
        public const string EnableUpdateCheck = "enable_update_check";
        public const string LastUpdateCheck = "last_update_check";

        public const string Key_Undo = "key_undo";
        public const string Key_Redo = "key_redo";
        public const string Key_Save = "key_save";

        public const string Gamepath = "game_path";
        public const string RszJsonPath = "rsz_json_path";
        public const string FilelistPath = "file_list_path";
    }

    private class AppGameConfig
    {
        public string gamepath = string.Empty;

        public string? filelist;
        public string? rszJson;
    }

    private static readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

    private const string IniFilepath = "ce_config.ini";

    public static readonly string Version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion ?? "";
    public static bool IsOutdatedVersion { get; internal set; }

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

    public static readonly float EventLoopMaxFrameTime = 1 / 60f;
    private Dictionary<string, AppGameConfig> gameConfigs = new();

    public readonly SettingWrapper<float> MaxFps = new SettingWrapper<float>(Keys.UnpackMaxThreads, _lock, 60);
    public float MaxFrameTime { get => MaxFps > 0 ? 1f / MaxFps : 0; set => MaxFps.Set(1 / value); }
    public readonly ClassSettingWrapper<string> MainSelectedGame = new ClassSettingWrapper<string>(Keys.MainWindowGame, _lock);
    public readonly ClassSettingWrapper<string> MainActiveBundle = new ClassSettingWrapper<string>(Keys.MainActiveBundle, _lock);
    public readonly ClassSettingWrapper<string> BlenderPath = new ClassSettingWrapper<string>(Keys.BlenderPath, _lock);
    public readonly ClassSettingWrapper<string> RemoteDataSource = new ClassSettingWrapper<string>(Keys.RemoteDataSource, _lock);
    public readonly ClassSettingWrapper<string> GameConfigBaseFilepath = new ClassSettingWrapper<string>(Keys.GameConfigBaseFilepath, _lock);
    public readonly ClassSettingWrapper<string> Theme = new ClassSettingWrapper<string>(Keys.Theme, _lock, () => "default");
    public readonly SettingWrapper<int> UnpackMaxThreads = new SettingWrapper<int>(Keys.UnpackMaxThreads, _lock, 4);
    public readonly SettingWrapper<ReeLib.via.Color> BackgroundColor = new SettingWrapper<ReeLib.via.Color>(Keys.BackgroundColor, _lock, new ReeLib.via.Color(115, 140, 153, 255));
    public readonly SettingWrapper<bool> PrettyFieldLabels = new SettingWrapper<bool>(Keys.PrettyLabels, _lock, true);
    public readonly SettingWrapper<int> LogLevel = new SettingWrapper<int>(Keys.LogLevel, _lock, 0);
    public readonly SettingWrapper<int> MaxUndoSteps = new SettingWrapper<int>(Keys.MaxUndoSteps, _lock, 250);
    public readonly SettingWrapper<bool> EnableUpdateCheck = new SettingWrapper<bool>(Keys.EnableUpdateCheck, _lock, true);
    public readonly SettingWrapper<DateTime> LastUpdateCheck = new SettingWrapper<DateTime>(Keys.LastUpdateCheck, _lock, DateTime.MinValue);
    public readonly ClassSettingWrapper<string> LatestVersion = new ClassSettingWrapper<string>(Keys.LatestVersion, _lock);

    public readonly ClassSettingWrapper<string[]> RecentFiles = new ClassSettingWrapper<string[]>(Keys.RecentFiles, _lock, () => []);

    public readonly SettingWrapper<KeyBinding> Key_Undo = new SettingWrapper<KeyBinding>(Keys.Key_Undo, _lock, new KeyBinding(ImGuiKey.Z, ctrl: true));
    public readonly SettingWrapper<KeyBinding> Key_Redo = new SettingWrapper<KeyBinding>(Keys.Key_Redo, _lock, new KeyBinding(ImGuiKey.Y, ctrl: true));
    public readonly SettingWrapper<KeyBinding> Key_Save = new SettingWrapper<KeyBinding>(Keys.Key_Save, _lock, new KeyBinding(ImGuiKey.S, ctrl: true));

    public string? GetGamePath(GameIdentifier game) => _lock.Read(() => gameConfigs.GetValueOrDefault(game.name)?.gamepath);
    public void SetGamePath(GameIdentifier game, string path) => SetForGameAndSave(game.name, path, (cfg, val) => cfg.gamepath = val);
    public string? GetGameRszJsonPath(GameIdentifier game) => _lock.Read(() => gameConfigs.GetValueOrDefault(game.name)?.rszJson);
    public void SetGameRszJsonPath(GameIdentifier game, string path) => SetForGameAndSave(game.name, path, (cfg, val) => cfg.rszJson = val);
    public string? GetGameFilelist(GameIdentifier game) => _lock.Read(() => gameConfigs.GetValueOrDefault(game.name)?.filelist);
    public void SetGameFilelist(GameIdentifier game, string path) => SetForGameAndSave(game.name, path, (cfg, val) => cfg.filelist = val);
    public bool HasAnyGameConfigured => _lock.Read(() => gameConfigs.Any(g => !string.IsNullOrEmpty(g.Value?.gamepath)));
    public IEnumerable<string> ConfiguredGames => _lock.Read(() => gameConfigs.Where(kv => !string.IsNullOrEmpty(kv.Value.gamepath)).Select(kv => kv.Key));

    private const int MaxRecentFileCount = 25;
    public void AddRecentFile(string file)
    {
        _lock.EnterWriteLock();
        try {
            if (RecentFiles.value == null || RecentFiles.value.Length == 0) {
                RecentFiles.value = [file];
                return;
            }

            var arr = RecentFiles.value;
            var prevIndex = Array.IndexOf(arr, file);
            if (prevIndex == 0) return;
            if (prevIndex != -1) {
                Array.Copy(arr, 0, arr, 1, prevIndex);
                arr[0] = file;
            } else if (arr.Length < MaxRecentFileCount) {
                RecentFiles.value = new string[] { file }.Concat(arr).ToArray();
            } else {
                RecentFiles.value = new string[MaxRecentFileCount];
                RecentFiles.value[0] = file;
                Array.Copy(arr, 0, RecentFiles.value, 1, MaxRecentFileCount - 1);
            }
        } finally {
            _lock.ExitWriteLock();
        }
        _lock.ReadCallback(SaveConfigToIni);
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

    public static void SaveConfigToIni()
    {
        var instance = Instance;
        var items = new List<(string, string, string?)>() {
            (Keys.MaxFps, instance.MaxFps.value.ToString(CultureInfo.InvariantCulture), null),
            (Keys.MainWindowGame, instance.MainSelectedGame.value?.ToString() ?? "", null),
            (Keys.MainActiveBundle, instance.MainActiveBundle.value?.ToString() ?? "", null),
            (Keys.BlenderPath, instance.BlenderPath.value?.ToString() ?? "", null),
            (Keys.UnpackMaxThreads, instance.UnpackMaxThreads.value.ToString(), null),
            (Keys.RemoteDataSource, instance.RemoteDataSource.value?.ToString() ?? "", null),
            (Keys.GameConfigBaseFilepath, instance.GameConfigBaseFilepath.value?.ToString() ?? "", null),
            (Keys.Theme, instance.Theme.value?.ToString() ?? "", null),
            (Keys.BackgroundColor, instance.BackgroundColor.value.ToString(), null),
            (Keys.LogLevel, instance.LogLevel.value.ToString(), null),
            (Keys.MaxUndoSteps, instance.MaxUndoSteps.value.ToString(), null),
            (Keys.LastUpdateCheck, instance.LastUpdateCheck.value.ToString("O"), null),
            (Keys.LatestVersion, instance.LatestVersion.value?.ToString() ?? "", null),
            (Keys.EnableUpdateCheck, instance.EnableUpdateCheck.value.ToString(), null),
            (Keys.PrettyLabels, instance.PrettyFieldLabels.value.ToString(), null),
            (Keys.RecentFiles, string.Join("|", instance.RecentFiles.value ?? Array.Empty<string>()), null),
            (Keys.Key_Undo, instance.Key_Undo.value.ToString(), "Keys"),
            (Keys.Key_Redo, instance.Key_Redo.value.ToString(), "Keys"),
            (Keys.Key_Save, instance.Key_Save.value.ToString(), "Keys"),
        };
        foreach (var (game, data) in instance.gameConfigs) {
            if (!string.IsNullOrEmpty(data.gamepath)) {
                items.Add((Keys.Gamepath, data.gamepath, game));
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

    public static void LoadConfigs() => LoadConfigs(IniFile.ReadFile(IniFilepath));

    private static void LoadConfigs(IEnumerable<(string key, string value, string? group)> values)
    {
        _lock.EnterWriteLock();
        var instance = Instance;
        try {
            foreach (var (key, value, group) in values) {
                if (group == null) {
                    switch (key) {
                        case Keys.MaxFps:
                            instance.MaxFps.value = float.Parse(value);
                            break;
                        case Keys.MainWindowGame:
                            instance.MainSelectedGame.value = string.IsNullOrEmpty(value) ? null : value;
                            break;
                        case Keys.MainActiveBundle:
                            instance.MainActiveBundle.value = string.IsNullOrEmpty(value) ? null : value;
                            break;
                        case Keys.BlenderPath:
                            instance.BlenderPath.value = string.IsNullOrEmpty(value) ? null : value;
                            break;
                        case Keys.RemoteDataSource:
                            instance.RemoteDataSource.value = string.IsNullOrEmpty(value) ? null : value;
                            break;
                        case Keys.GameConfigBaseFilepath:
                            instance.GameConfigBaseFilepath.value = string.IsNullOrEmpty(value) ? null : value;
                            break;
                        case Keys.Theme:
                            instance.Theme.value = string.IsNullOrEmpty(value) ? "default" : value;
                            break;
                        case Keys.UnpackMaxThreads:
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) instance.UnpackMaxThreads.value = Math.Clamp(parsed, 1, 64);
                            break;
                        case Keys.BackgroundColor:
                            if (ReeLib.via.Color.TryParse(value, out var _col)) instance.BackgroundColor.value =  _col;
                            break;
                        case Keys.LogLevel:
                            if (int.TryParse(value, out var _log)) instance.LogLevel.value =  _log;
                            break;
                        case Keys.MaxUndoSteps:
                            if (int.TryParse(value, out var _undoSteps)) instance.MaxUndoSteps.value = Math.Max(_undoSteps, 0);
                            break;
                        case Keys.PrettyLabels:
                            instance.PrettyFieldLabels.value = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value == "1";
                            break;
                        case Keys.RecentFiles:
                            instance.RecentFiles.value = value.Split('|', StringSplitOptions.RemoveEmptyEntries);
                            break;
                        case Keys.EnableUpdateCheck:
                            instance.EnableUpdateCheck.value = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value == "1";
                            break;
                        case Keys.LastUpdateCheck:
                            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var _updateCheck)) instance.LastUpdateCheck.value = _updateCheck;
                            break;
                        case Keys.LatestVersion:
                            instance.LatestVersion.value = string.IsNullOrEmpty(value) ? null : value;
                            break;
                    }
                } else if (group == "Keys") {
                    switch (key) {
                        case Keys.Key_Undo:
                            if (KeyBinding.TryParse(value, out var _key)) instance.Key_Undo.value = _key;
                            break;
                        case Keys.Key_Redo:
                            if (KeyBinding.TryParse(value, out _key)) instance.Key_Redo.value = _key;
                            break;
                        case Keys.Key_Save:
                            if (KeyBinding.TryParse(value, out _key)) instance.Key_Save.value = _key;
                            break;
                    }
                } else {
                    var config = instance.gameConfigs.GetValueOrDefault(group);
                    if (config == null) {
                        instance.gameConfigs[group] = config = new AppGameConfig();
                    }

                    switch (key) {
                        case Keys.Gamepath:
                            config.gamepath = value;
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