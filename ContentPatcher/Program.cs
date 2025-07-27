using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ContentEditor;
using ContentPatcher;
using ContentPatcher.FileFormats;
using ReeLib;

var baseSettings = IniFile.ReadFileIgnoreKeyCasing("content_patcher.ini").ToDictionary();

string action = "patch";
var cliSettings = new Dictionary<string, string>();
bool launchAfterPatch = false;
string? game = null;
for (int i = 0; i < args.Length; ++i) {
    var arg = args[i];
    if (arg == "patch") {
        action = "patch";
    } else if (arg == "--game") {
        game = args[++i];
    } else if (arg == "--config" || arg == "-c") {
        var cfg = args[++i];
        if (cfg.Contains('=')) {
            string[] split = cfg.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
            cliSettings.TryAdd(split[0].Replace("_", "").Replace(" ", "").ToLowerInvariant(), split[1]);
        } else {
            cliSettings.TryAdd(cfg.Replace("_", "").Replace(" ", "").ToLowerInvariant(), args[++i]);
        }
    } else if (arg == "--help" || arg == "help") {
        PrintHelp();
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
        PrintHelp();
        return;
}

[DoesNotReturn]
void PrintHelp(int exitCode = 0)
{
    Console.WriteLine();
    Console.WriteLine("USAGE:");
    Console.WriteLine("content-patcher.exe [patch] [--game <gamename>]");
    Environment.Exit(exitCode);
}

[DoesNotReturn]
void PrintCliError(string error, int exitCode = 0)
{
    Console.WriteLine(error);
    PrintHelp(exitCode);
}

void ExecutePatcher()
{
    game ??= baseSettings.GetValueOrDefault("lastgame");
    if (game == null) {
        PrintCliError("Game to patch was not specified", 2);
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
        var exe = ExeUtils.FindGameExecutable(patcher.Env.Config.GamePath, game);
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
