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

        public const string Key_Undo = "key_undo";
        public const string Key_Redo = "key_redo";

        public const string Gamepath = "game_path";
    }

    private class AppGameConfig
    {
        public string gamepath = string.Empty;
    }

    private static readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

    private const string IniFilepath = "ce_config.ini";

    public static readonly string Version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).FileVersion ?? "";

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

        public override string ToString() => value.ToString()!;

        public static implicit operator T(SettingWrapper<T> vv) => vv.Get();
    }

    public sealed class RefSettingWrapper<T>(string settingKey, ReaderWriterLockSlim _lock, T? initial = default) where T : IEquatable<T>
    {
        internal T? value = initial;

        private readonly T? Initial = initial;
        public readonly string SettingKey = settingKey;

        public event Action<T?>? ValueChanged;

        public bool IsInitial => value == null ? Initial == null : value.Equals(Initial);
        public T? Get() => _lock.Read(() => value);
        public void Set(T? value) => SetAndSave(value, (v) => {
            this.value = value;
            ValueChanged?.Invoke(v);
        });
        public void Reset() => Set(Initial);

        public override string? ToString() => value?.ToString();

        public static implicit operator T?(RefSettingWrapper<T> vv) => vv.Get();
    }

    public static readonly float EventLoopMaxFrameTime = 1 / 60f;
    private Dictionary<string, AppGameConfig> gameConfigs = new();

    public readonly SettingWrapper<float> MaxFps = new SettingWrapper<float>(Keys.UnpackMaxThreads, _lock, 60);
    public float MaxFrameTime { get => MaxFps > 0 ? 1f / MaxFps : 0; set => MaxFps.Set(1 / value); }
    public readonly RefSettingWrapper<string> MainSelectedGame = new RefSettingWrapper<string>(Keys.MainWindowGame, _lock);
    public readonly RefSettingWrapper<string> MainActiveBundle = new RefSettingWrapper<string>(Keys.MainActiveBundle, _lock);
    public readonly RefSettingWrapper<string> BlenderPath = new RefSettingWrapper<string>(Keys.BlenderPath, _lock);
    public readonly RefSettingWrapper<string> RemoteDataSource = new RefSettingWrapper<string>(Keys.RemoteDataSource, _lock);
    public readonly RefSettingWrapper<string> GameConfigBaseFilepath = new RefSettingWrapper<string>(Keys.GameConfigBaseFilepath, _lock);
    public readonly SettingWrapper<int> UnpackMaxThreads = new SettingWrapper<int>(Keys.UnpackMaxThreads, _lock, 4);
    public readonly SettingWrapper<ReeLib.via.Color> BackgroundColor = new SettingWrapper<ReeLib.via.Color>(Keys.BackgroundColor, _lock, new ReeLib.via.Color(115, 140, 153, 255));
    public readonly SettingWrapper<bool> PrettyFieldLabels = new SettingWrapper<bool>(Keys.PrettyLabels, _lock, true);
    public readonly SettingWrapper<int> LogLevel = new SettingWrapper<int>(Keys.LogLevel, _lock, 0);
    public readonly SettingWrapper<int> MaxUndoSteps = new SettingWrapper<int>(Keys.MaxUndoSteps, _lock, 250);

    public readonly SettingWrapper<KeyBinding> Key_Undo = new SettingWrapper<KeyBinding>(Keys.Key_Undo, _lock, new KeyBinding(ImGuiKey.Z, ctrl: true));
    public readonly SettingWrapper<KeyBinding> Key_Redo = new SettingWrapper<KeyBinding>(Keys.Key_Redo, _lock, new KeyBinding(ImGuiKey.Y, ctrl: true));

    public string? GetGamePath(GameIdentifier game) => _lock.Read(() => Instance.gameConfigs.GetValueOrDefault(game.name)?.gamepath);
    public void SetGamePath(GameIdentifier game, string path) => SetForGameAndSave(game.name, path, (cfg, val) => cfg.gamepath = val);
    public bool HasAnyGameConfigured => _lock.Read(() => Instance.gameConfigs.Any(g => !string.IsNullOrEmpty(g.Value?.gamepath)));

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
            (Keys.MainWindowGame, instance.MainSelectedGame.ToString() ?? "", null),
            (Keys.MainActiveBundle, instance.MainActiveBundle.ToString() ?? "", null),
            (Keys.BlenderPath, instance.BlenderPath.ToString() ?? "", null),
            (Keys.UnpackMaxThreads, instance.UnpackMaxThreads.ToString(), null),
            (Keys.RemoteDataSource, instance.RemoteDataSource.ToString() ?? "", null),
            (Keys.GameConfigBaseFilepath, instance.GameConfigBaseFilepath.ToString() ?? "", null),
            (Keys.BackgroundColor, instance.BackgroundColor.ToString(), null),
            (Keys.LogLevel, instance.LogLevel.ToString(), null),
            (Keys.MaxUndoSteps, instance.MaxUndoSteps.ToString(), null),
            (Keys.PrettyLabels, instance.PrettyFieldLabels.ToString(), null),
            (Keys.Key_Undo, instance.Key_Undo.ToString(), "Keys"),
            (Keys.Key_Redo, instance.Key_Redo.ToString(), "Keys"),
        };
        foreach (var (game, data) in instance.gameConfigs) {
            if (!string.IsNullOrEmpty(data.gamepath)) {
                items.Add((Keys.Gamepath, data.gamepath, game));
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
                            instance.MainSelectedGame.value = value;
                            break;
                        case Keys.MainActiveBundle:
                            instance.MainActiveBundle.value = value;
                            break;
                        case Keys.BlenderPath:
                            instance.BlenderPath.value = value;
                            break;
                        case Keys.RemoteDataSource:
                            instance.RemoteDataSource.value = string.IsNullOrEmpty(value) ? null : value;
                            break;
                        case Keys.GameConfigBaseFilepath:
                            instance.GameConfigBaseFilepath.value = value;
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
                    }
                } else if (group == "Keys") {
                    switch (key) {
                        case Keys.Key_Undo:
                            if (KeyBinding.TryParse(value, out var _key)) instance.Key_Undo.value = _key;
                            break;
                        case Keys.Key_Redo:
                            if (KeyBinding.TryParse(value, out _key)) instance.Key_Redo.value = _key;
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