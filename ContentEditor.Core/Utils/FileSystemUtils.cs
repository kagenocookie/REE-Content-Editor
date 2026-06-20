using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace ContentEditor;

public static class FileSystemUtils
{
    public static void ShowFileInExplorer(string? file)
    {
        if (file == null) {
            Logger.Error("Invalid filepath");
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            if (File.Exists(file)) {
                Logger.Info("Filename: " + file);
                Process.Start(new ProcessStartInfo("explorer.exe") {
                    UseShellExecute = false,
                    Arguments = $"/select, \"{file.Replace('/', '\\')}\"",
                });
            } else if (Directory.Exists(file)) {
                Logger.Info("Directory: " + file);
                Process.Start(new ProcessStartInfo("explorer.exe") {
                    UseShellExecute = false,
                    Arguments = $"\"{file.Replace('/', '\\')}\"",
                });
            } else {
                Logger.Error("File not found: " + file);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            if (File.Exists(file)) {
                Logger.Info("Filename: " + file);
                // navigate up because xdg-open would otherwise open the file itself in whatever the default app is
                file = Path.GetDirectoryName(file)!;
            } else if (Directory.Exists(file)) {
                Logger.Info("Directory: " + file);
            } else {
                Logger.Error("File not found: " + file);
            }

            Process.Start(new ProcessStartInfo("xdg-open") {
                UseShellExecute = false,
                Arguments = $"\"{file.Replace('\\', '/')}\"",
            });
        } else {
            Logger.Error("File not found: " + file);
        }
    }

    public static void OpenInExternalEditor(string? fileOrFolder, string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) {
            Logger.Error("External editor not configured or does not exist");
            return;
        }
        if (string.IsNullOrEmpty(fileOrFolder)) {
            Logger.Error("Invalid filepath");
            return;
        }

        var escapedFile = $"\"{fileOrFolder}\"";
        var (exe, args) = FormatProcessParams(exePath, escapedFile);
        Process.Start(new ProcessStartInfo(exe) {
            UseShellExecute = false,
            Arguments = args,
        });
    }

    public static (string exe, string args) FormatProcessParams(string toolPath, string ourArgs)
    {
        var split = toolPath?.Split('|', 2) ?? [];
        var exePath = split.FirstOrDefault() ?? toolPath;
        if (string.IsNullOrEmpty(exePath)) {
            return default;
        }

        var args = split.Skip(1).FirstOrDefault();
        if (string.IsNullOrEmpty(args)) {
            args = ourArgs;
        } else if (args.Contains("{COMMAND}")) {
            args = args.Replace("{COMMAND}", ourArgs);
        } else {
            args = $"{args} {ourArgs}";
        }

        return (exePath, args);
    }

    public static void OpenURL(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static bool EnsureDirectoryExists([NotNullWhen(true)] string directory)
    {
        try {
            Directory.CreateDirectory(directory);
            return true;
        } catch (Exception) {
            return false;
        }
    }
}