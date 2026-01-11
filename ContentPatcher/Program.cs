using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ContentEditor;
using ContentPatcher;
using ContentPatcher.FileFormats;
using ReeLib;

var baseSettings = IniFile.ReadFileIgnoreKeyCasing("content_patcher.ini").ToDictionary();

Console.WriteLine("Content Patcher " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion ?? "");
string action = "patch";
var cliSettings = new Dictionary<string, string>();
bool launchAfterPatch = false;
string? game = null;
string? gamepath = null;

var interactive = false;
if (args.Length == 0) {
    PrintHelp(false);
    Console.WriteLine();
    interactive = true;
}

if (interactive) {
    string? ShowOption(string label, bool lowercase = true)
    {
        System.Console.Write(label);
        return lowercase ? System.Console.In.ReadLine()?.ToLowerInvariant() : System.Console.In.ReadLine();
    }
    var builtinNames = string.Join(", ", Enum.GetNames<GameName>());
    game = ShowOption($" Game short name (Built in options: {builtinNames})\nCurrent setting: {baseSettings.GetValueOrDefault("game")}\nEnter game, or leave empty to use the current one:\n", true);
    if (string.IsNullOrEmpty(game)) game = baseSettings.GetValueOrDefault("game");
    gamepath = ShowOption($" Path to game\nLast setting: {baseSettings.GetValueOrDefault("gamepath")}\nEnter path, or leave empty to use the last used one:\n", false);
    if (string.IsNullOrEmpty(gamepath)) gamepath = baseSettings.GetValueOrDefault("gamepath");
    if (!Directory.Exists(gamepath)) {
        Console.Error.WriteLine("Invalid path");
        Console.In.ReadLine();
        return;
    }
}

for (int i = 0; i < args.Length; ++i) {
    var arg = args[i];
    if (arg == "patch") {
        action = "patch";
    } else if (arg == "--game") {
        game = args[++i];
    } else if (arg == "--path") {
        gamepath = args[++i];
    } else if (arg == "--config" || arg == "-c") {
        var cfg = args[++i];
        if (cfg.Contains('=')) {
            string[] split = cfg.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
            cliSettings.TryAdd(split[0].Replace("_", "").Replace(" ", "").ToLowerInvariant(), split[1]);
        } else {
            cliSettings.TryAdd(cfg.Replace("_", "").Replace(" ", "").ToLowerInvariant(), args[++i]);
        }
    } else if (arg == "--help" || arg == "help") {
        PrintHelp(true);
        Environment.Exit(0);
    } else if (arg == "--launch") {
        launchAfterPatch = true;
    } else {
        PrintCliError("Unknown command line argument " + arg, 1);
    }
}


switch(action) {
    case "patch":
        ExecutePatcher();
        break;
    default:
        Console.Error.WriteLine("Unknown action: " + args[0]);
        PrintHelp(true);
        return;
}

void PrintHelp(bool stop, int exitCode = 0)
{
    Console.WriteLine();
    Console.WriteLine("USAGE:");
    Console.WriteLine("content-patcher.exe [patch] [--game <gamename>] [--path <path_to_game_folder>]");
    Console.WriteLine("Game and path settings only need to be specified once, will be remembered for next time");
    if (stop) {
        Console.WriteLine("");
        Console.WriteLine("Press any key to close");
        Console.ReadKey();
        Environment.Exit(exitCode);
    }
}

[DoesNotReturn]
void PrintCliError(string error, int exitCode = 0)
{
    Console.WriteLine(error);
    PrintHelp(true, exitCode);
    Environment.Exit(exitCode);
}

void ExecutePatcher()
{
    var cfgGame = baseSettings.GetValueOrDefault("game");
    if (game != cfgGame) {
        baseSettings.Remove("gamepath");
    }
    var cfgPath = baseSettings.GetValueOrDefault("gamepath");
    game ??= cfgGame;
    gamepath ??= cfgPath;
    if (game == null) {
        PrintCliError("Game to patch was not specified", 2);
    }
    if (game != cfgGame || gamepath != cfgPath) {
        baseSettings["game"] = game;
        baseSettings["gamepath"] = gamepath ?? string.Empty;
        IniFile.WriteToFile("content_patcher.ini", baseSettings.ToArray());
    }

    var levelstr = baseSettings.GetValueOrDefault("loglevel") ?? cliSettings.GetValueOrDefault("loglevel");
    Logger.CurrentLogger.LoggingLevel = int.TryParse(levelstr, out var logLevel) ? (LogSeverity)logLevel : LogSeverity.Info;

    var cacheOverride = cliSettings.GetValueOrDefault("resourcecache")
        ?? baseSettings.GetValueOrDefault("resourcecache");
    if (cacheOverride != null) {
        ResourceRepository.LocalResourceRepositoryFilepath = cacheOverride;
    }

    var sw = Stopwatch.StartNew();
    var patcher = new Patcher(GameConfig.CreateFromRepository(game));
    patcher.LoadIniConfig("content_patcher.ini");
    patcher.LoadIniConfig($"configs/{game}/game.ini");
    patcher.LoadConfig(cliSettings);
    patcher.Execute();
    Logger.Info($"Patching finished in {sw.Elapsed.TotalSeconds} s");
    if (launchAfterPatch) {
        Logger.Info($"Attempting to launch game...");
        var exe = AppUtils.FindGameExecutable(patcher.Env.Config.GamePath, game);
        if (exe != null) {
            Logger.Info("Launching exe " + exe);
            Process.Start(new ProcessStartInfo(exe) {
                UseShellExecute = false,
            });
        } else {
            Logger.Error("Could not determine game exe");
        }
    }
}

Console.WriteLine("Press any key to close");
Console.ReadKey();
