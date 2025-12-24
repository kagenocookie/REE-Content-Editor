using System.Diagnostics;
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
}