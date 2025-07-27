using System.Diagnostics;

namespace ContentEditor.App;

public static class FileSystemUtils
{
    public static void ShowFileInExplorer(string? file)
    {
        if (File.Exists(file)) {
            Logger.Info("Filename: " + file);
            Process.Start(new ProcessStartInfo("explorer.exe") {
                UseShellExecute = false,
                Arguments = $"/select, \"{file.Replace('/', '\\')}\"",
            });
        } else {
            Logger.Error("File not found: " + file);
        }
    }
}