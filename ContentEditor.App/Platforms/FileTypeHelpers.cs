#if WINDOWS
using System.Runtime.InteropServices;
#endif
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ContentEditor.Core;
using NativeFileDialogNET;
using ReeLib;
using ReeLib.Common;
using Silk.NET.Windowing;

namespace ContentEditor.App;

public static partial class FileTypeHelpers
{
    private const string GeneralMimeType = "application/x-re-engine-resource";

    [GeneratedRegex(@" (\p{Lu})")]
    private static partial Regex KebabCaser();

    private static bool IsDistrobox() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CONTAINER_ID"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISTROBOX_ENTER_PATH"));

    public static void SetupLinuxFileRegistrations()
    {
        try {
            var mimes = SetupLinuxMimeTypes();
            SetupDesktopFile(mimes);
            DumpIconFile();
            RegisterMimesWithApp(mimes);
        } catch (Exception e) {
            Logger.Warn("File registration may have failed: " + e.Message);
        }
    }

    private static void DumpIconFile()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ContentEditor.App.images.app_icon_light.png");
        if (stream == null) return;

        var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var icondir = Path.Combine(userPath, ".local/share/icons/hicolor/256x256/apps");
        var iconpath = Path.Combine(icondir, "ContentEditor.App.png");
        if (!FileSystemUtils.EnsureDirectoryExists(icondir)) {
            Logger.Error("Failed to create app icon folder " + icondir);
            return;
        }

        using var fs = File.Create(iconpath);
        stream.CopyTo(fs);
        Logger.Info("Saved icon to " + iconpath);
    }

    private static readonly Dictionary<KnownFileFormats, KnownFileFormats[]> AltFormats = new () {
        { KnownFileFormats.Clip, [KnownFileFormats.Clip, KnownFileFormats.Timeline, KnownFileFormats.UserCurve, KnownFileFormats.UserCurveList] },
        { KnownFileFormats.Skeleton, [KnownFileFormats.FbxSkeleton, KnownFileFormats.Skeleton, KnownFileFormats.RefSkeleton,] }
    };

    public static string[] SetupLinuxMimeTypes()
    {
        var fileTypes = Utils.GetSupportedFileContentFormats();
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine(@"<mime-info xmlns=""http://www.freedesktop.org/standards/shared-mime-info"">");
        var mimes = new List<string>();
        foreach (var (magic, offset, format) in fileTypes) {
            var formatName = "RE Engine " + format.ToString().PrettyPrint();
            var kebabFormat = KebabCaser().Replace(format.ToString().PrettyPrint(), static f => f.Value.Replace(' ', '-')).ToLowerInvariant();
            var mimeType = "application/x-re-engine-" + kebabFormat;
            mimes.Add(mimeType);
            sb.AppendLine($@"    <mime-type type=""{mimeType}"">");
            sb.AppendLine($"        <comment>{formatName}</comment>");
            sb.AppendLine($@"        <sub-class-of type=""{GeneralMimeType}""/>");
            sb.AppendLine(@"        <icon name=""ContentEditor.App""/>");
            sb.AppendLine(@"        <magic priority=""60"">");
            sb.AppendLine($@"            <match offset=""{offset}"" type=""little32"" value=""0x{magic.ToString("X")}""/>");
            sb.AppendLine("        </magic>");

            var altFormats = AltFormats.GetValueOrDefault(format) ?? [format];
            var exts = altFormats.SelectMany(fmt2 => FileFormatExtensions.KnownFileExtensions.Where(ext => FileFormatExtensions.ExtensionToEnum(ext) == fmt2)).ToArray();
            foreach (var ext in exts) {
                sb.AppendLine($@"        <glob pattern=""*.{ext}.*"" weight=""20"" />");
            }
            sb.AppendLine("    </mime-type>");
        }
        sb.AppendLine("</mime-info>");
        var xml = sb.ToString();
        var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var mimeDir = Path.Combine(userPath, ".local/share/mime");
        var basedir = Path.Combine(mimeDir, "packages");
        var xmlFilename = "re-engine-formats.xml";
        var targetPath = Path.Combine(basedir, xmlFilename);

        var status = WriteFileOrFallback(targetPath, xml, xmlFilename);
        if (status == FileWriteStatus.Fail) {
            return mimes.ToArray();
        }
        if (status == FileWriteStatus.Fallback) {
            Logger.Warn("Afterwards, run the following command from a console: update-mime-database ~/.local/share/mime");
        }
        if (status == FileWriteStatus.OK) {
            try {
                Process.Start(new ProcessStartInfo("/usr/bin/update-mime-database") {
                    UseShellExecute = true,
                    Arguments = $"\"{mimeDir}\"",
                });
            } catch (Exception) {
                Logger.Warn("Failed to force a mime database update, re-login or manually run the following command from a console: update-mime-database ~/.local/share/mime");
            }
        }
        try {
            Directory.CreateDirectory(basedir);
            File.WriteAllText(targetPath, xml);
            Process.Start(new ProcessStartInfo("/usr/bin/update-mime-database") {
                UseShellExecute = true,
                ArgumentList = { mimeDir },
            });
        } catch (IOException) {
            try {
                var fallbackPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
                File.WriteAllText(fallbackPath, xml);
                Logger.Warn($"Failed to write mime type xml file to {targetPath}.\nSaved a backup file to {fallbackPath} instead. Copy it manually to the target path: {targetPath}");
                Logger.Warn("Afterwards, run the following command from a console: update-mime-database ~/.local/share/mime");
            } catch (Exception) {
                Logger.Error($"Failed to write mime type xml file to {targetPath}.");
                Logger.Error("Unable to save mime type xml to disk. Try playing around with folder permissions or re-running with sudo.");
            }
        } catch (Exception) {
            Logger.Error($"Failed to save xml file or directly update the mime database.\nVerify if the file exists: {targetPath}.\nIf it does, manually run the following command from a console: update-mime-database ~/.local/share/mime");
        }
        return mimes.ToArray();
    }

    private static FileWriteStatus SetupDesktopFile(string[] mimes)
    {
        var mimeString = string.Join(';', mimes);
        var currentPath = System.Reflection.Assembly.GetEntryAssembly()!.Location;
        currentPath = Path.ChangeExtension(currentPath, null);

        var desktopFileContent = $"""
            [Desktop Entry]
            Categories=FileEditor;Modding;Development;
            Comment=RE Engine Content Editor
            Exec={currentPath} %F
            GenericName=File Editor
            Icon=ContentEditor.App
            MimeType={GeneralMimeType};{mimeString};
            Name=REE Content Editor
            NoDisplay=false
            Path=
            PrefersNonDefaultGPU=false
            StartupNotify=false
            StartupWMClass=ContentEditorApp
            Terminal=false
            TerminalOptions=
            Type=Application
            Terminal=false
            """;

        var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appdir = Path.Combine(userPath, ".local/share/applications");
        var filename = "ContentEditor.App.desktop";
        var desktopFilePath = Path.Combine(appdir, filename);
        var status = WriteFileOrFallback(desktopFilePath, desktopFileContent, filename);

        if (IsDistrobox()) {
            Logger.Info("A container setup / distrobox has been detected. The desktop file likely won't work directly and you'll have to tweak it manually a bit.");
            Logger.Info(".desktop file: " + desktopFilePath);
            Logger.Info($"Adjust the \"Exec\" field to (replace with your actual launch command) e.g. Exec=distrobox enter <container_name> -- {currentPath}");
        }
        return status;
    }

    private static void RegisterMimesWithApp(string[] mimes)
    {
        var isDistrobox = IsDistrobox();
        try {
            foreach (var mime in mimes) {
                ProcessStartInfo pi;
                if (isDistrobox) {
                    pi = new ProcessStartInfo {
                        FileName = "distrobox-host-exec",
                        ArgumentList =
                        {
                            "xdg-mime",
                            "default",
                            "ContentEditor.App.desktop",
                            mime,
                        },
                        UseShellExecute = false
                    };
                } else {
                    pi = new ProcessStartInfo {
                        FileName = "xdg-mime",
                        ArgumentList =
                        {
                            "default",
                            "ContentEditor.App.desktop",
                            mime,
                        },
                        UseShellExecute = false
                    };
                }
                Process.Start(pi)?.WaitForExit();
            }
        } catch (Exception e) {
            Logger.Error(e, "Failed to register the tool with all known mime types. You might have to do it manually.");
        }
    }

    private enum FileWriteStatus
    {
        OK,
        Fallback,
        Fail,
    }

    private static FileWriteStatus WriteFileOrFallback(string targetFilepath, string fileContent, string filename)
    {
        try {
            var dir = Path.GetDirectoryName(targetFilepath)!;
            if (!FileSystemUtils.EnsureDirectoryExists(dir)) {
                Logger.Error("Failed to find or create system user directory " + dir);
                return FileWriteStatus.Fail;
            }
            File.WriteAllText(targetFilepath, fileContent);
            Logger.Info("Saved file " + targetFilepath);
            return FileWriteStatus.OK;
        } catch (Exception) {
            Logger.Error("Failed to save file " + targetFilepath);

            try {
                var fallbackPath = Path.Combine(AppContext.BaseDirectory, filename);
                File.WriteAllText(fallbackPath, fileContent);
                Logger.Warn($"Failed to write file to {targetFilepath}\nSaved a fallback file instead to {fallbackPath}\nCopy it manually to the target path: {targetFilepath}");
                return FileWriteStatus.Fallback;
            } catch (Exception) {
                Logger.Error($"Failed to write file to {targetFilepath}. Try playing around with folder permissions or re-running with sudo.");
                return FileWriteStatus.Fail;
            }
        }
    }
}
