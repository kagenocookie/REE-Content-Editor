namespace ContentPatcher;

public static partial class ConfigKeys
{
    public const string GamePath = "game_path";
    public const string EnumPath = "enum_path";
}

public static class Config
{
    private static string _gamepath = string.Empty;
    private static string _enumpath = string.Empty;
    public static string GamePath => _gamepath;
    public static string EnumPath => _enumpath;

    public static void LoadConfigs(IEnumerable<(string key, string value)> values)
    {
        foreach (var (key, value) in values) {
            switch (key) {
                case ConfigKeys.GamePath:
                    _gamepath = value;
                    break;
                case ConfigKeys.EnumPath:
                    _enumpath = value;
                    break;
            }
        }
    }
}