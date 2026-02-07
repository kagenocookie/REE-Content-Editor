using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;
using ContentEditor.App.FileLoaders;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

public class FileUpgrader : BaseWindowHandler
{
    public override string HandlerName => "File Upgrader";

    private string sourceFolder = "";
    private string destinationFolder = "";

    private HashSet<string> sourcePaks = new();
    private HashSet<string> targetPaks = new();

    private List<string> upgradeableFileList = new();
    private List<string> notUpgradeableFileList = new();

    private Dictionary<REFileFormat, FileConverter?> sourceFormats = new();

    private static Type[] AvailableConverters = typeof(FileUpgrader).GetNestedTypes(System.Reflection.BindingFlags.NonPublic).Where(t => t.IsAssignableTo(typeof(FileConverter)) && !t.IsAbstract).ToArray();
    private static FileConverter[] StaticConverters = AvailableConverters.Select(c => (FileConverter)Activator.CreateInstance(c)!).ToArray();
    private readonly List<FileConverter> Converters = new();

    public override void OnIMGUI()
    {
        var sourceChanged = AppImguiHelpers.InputFolder("Source Folder", ref sourceFolder);
        ImguiHelpers.Tooltip("The folder containing the files you wish to upgrade.");

        AppImguiHelpers.InputFolder("Destination Folder", ref destinationFolder);
        ImguiHelpers.Tooltip("The folder into which to store the upgraded files.");

        // TODO non-RT to RT conversion option

        if (string.IsNullOrEmpty(sourceFolder)) {
            return;
        }

        if (sourceFolder == destinationFolder) {
            ImGui.TextColored(Colors.Warning, "The source and target folders should not be the same.");
            return;
        }

        if (sourcePaks.Count == 0) {
            sourcePaks = workspace.Env.PakReader.PakFilePriority.ToHashSet();
        }
        if (targetPaks.Count == 0) {
            targetPaks = workspace.Env.PakReader.PakFilePriority.ToHashSet();
        }

        if (sourceChanged || upgradeableFileList.Count == 0 && notUpgradeableFileList.Count == 0) {
            sourceFormats.Clear();
            Converters.Clear();
            notUpgradeableFileList.Clear();
            upgradeableFileList.Clear();
            if (!Directory.Exists(sourceFolder)) {
                ImGui.TextColored(Colors.Error, $"Folder {sourceFolder} not found");
                return;
            }

            foreach (var filepath in Directory.EnumerateFiles(sourceFolder, "*.*", SearchOption.AllDirectories)) {
                var file = Path.GetFileName(filepath.AsSpan());
                var relativePath = Path.GetRelativePath(sourceFolder, filepath);
                var format = PathUtils.ParseFileFormat(file);

                var converter = Converters.FirstOrDefault(c => c.CanUpgrade(filepath, format));
                if (converter == null) {
                    converter = StaticConverters.FirstOrDefault(c => c.CanUpgrade(filepath, format));
                    if (converter != null) {
                        Converters.Add(converter = (FileConverter)Activator.CreateInstance(converter.GetType())!);
                        converter.Init(workspace);
                    }
                }
                if (format.format != KnownFileFormats.Unknown) sourceFormats.TryAdd(format, converter);
                if (converter == null) {
                    notUpgradeableFileList.Add(relativePath);
                } else {
                    upgradeableFileList.Add(relativePath);
                }
            }
        }

        var avail = ImGui.GetContentRegionAvail() - new Vector2(0, 32 * UI.UIScale + ImGui.GetStyle().FramePadding.Y);
        if (!ImGui.BeginChild("FileList", avail)) {
            ImGui.EndChild();
            return;
        }

        if (ImGui.BeginTabBar("UpgradeTabs"u8)) {
            if (ImGui.BeginTabItem("PAK List"u8)) {
                foreach (var pak in workspace.Env.PakReader.PakFilePriority) {
                    ImGui.PushID(pak);
                    var source = sourcePaks.Contains(pak);
                    if (ImGui.Checkbox("Source"u8, ref source)) {
                        if (source) sourcePaks.Add(pak);
                        else sourcePaks.Remove(pak);
                    }

                    var target = targetPaks.Contains(pak);
                    ImGui.SameLine();
                    if (ImGui.Checkbox("Target"u8, ref target)) {
                        if (target) targetPaks.Add(pak);
                        else targetPaks.Remove(pak);
                    }

                    ImGui.SameLine();
                    ImGui.Text("|"u8);
                    ImGui.SameLine();
                    ImGui.Text(pak);
                    ImGui.PopID();
                }
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("File Formats"u8)) {
                foreach (var (format, converter) in sourceFormats.OrderBy(f => f.Key.format)) {
                    if (converter == null) {
                        ImGui.TextColored(Colors.Warning, $"{format.format}.{format.version} is not convertable");
                        continue;
                    }
                    ImGui.PushID((int)format.format);

                    ImGui.Checkbox(converter.Label ?? $"{format.format}.{format.version}", ref converter.enabled);
                    if (converter.enabled) {
                        var isReady = converter.ShowSettings();
                        ImGui.Spacing();
                    }

                    ImGui.PopID();
                }
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("File List"u8)) {
                ImGui.SetNextItemOpen(true, ImGuiCond.Appearing);
                if (ImGui.TreeNode($"Upgradeable Files ({upgradeableFileList.Count})")) {
                    foreach (var file in upgradeableFileList) {
                        ImGui.Text(file);
                        if (!file.StartsWith("natives")) {
                            ImGui.SameLine();
                            ImGui.Button($"{AppIcons.SI_GenericWarning}");
                            ImguiHelpers.TooltipColored("The file path is not contained in the natives/ path, the upgrader is unable to compare with original game files.\nRSZ data upgrade will be attempted but might not work as expected.", Colors.Warning);
                        }
                    }
                    ImGui.TreePop();
                }

                if (notUpgradeableFileList.Count > 0 && ImGui.TreeNode($"NOT Upgradeable Files ({notUpgradeableFileList.Count})")) {
                    foreach (var file in notUpgradeableFileList) {
                        ImGui.Text(file);
                    }
                    ImGui.TreePop();
                }
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.EndChild();
        if (upgradeableFileList.Count == 0) {
            return;
        }
        if (!Directory.Exists(destinationFolder)) {
            if (string.IsNullOrEmpty(destinationFolder) || !Path.IsPathFullyQualified(destinationFolder)) {
                return;
            }
            if (ImGui.Button("Create destination folder"u8)) {
                try {
                    Directory.CreateDirectory(destinationFolder);
                } catch (Exception e) {
                    Logger.Error("Failed to create destination folder: " + e.Message);
                }
            }
            return;
        }

        if (ImGui.Button("Upgrade"u8)) {
            AttemptUpgrade();
        }
        ImGui.SameLine();
        if (ImGui.Button("Find Vanilla Changes"u8)) {
            CompareChangedFiles();
        }
    }

    private void CompareChangedFiles()
    {
        using var context = PrepareWorkspace();

        foreach (var relativePath in upgradeableFileList) {
            try {
                var sourcePath = Path.Combine(sourceFolder, relativePath);
                var destinationPath = Path.Combine(destinationFolder, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                if (!relativePath.StartsWith("natives")) {
                    continue;
                }

                string nativePath = relativePath;
                var sourceFile = context.sourceEnv.ResourceManager.ReadOrGetFileResource(nativePath, nativePath);
                if (sourceFile == null) {
                    // read error or does not exist in source, ignore
                    continue;
                }

                var updatedFile = context.updatedEnv.ResourceManager.ReadOrGetFileResource(nativePath, nativePath);
                if (updatedFile == null) {
                    Logger.Warn($"Could not load updated file: {nativePath}");
                    continue;
                }

                if (updatedFile.DiffHandler == null) {
                    continue;
                }

                updatedFile.DiffHandler.LoadBase(context.updatedEnv, sourceFile);
                var diff = updatedFile.DiffHandler.FindDiff(updatedFile);

                if (diff == null) {
                    Logger.Info($"No changes detected for file {nativePath}");
                    continue;
                }

                var diffOutputFile = destinationPath + ".diff.json";
                Directory.CreateDirectory(Path.GetDirectoryName(diffOutputFile)!);
                using var fs = File.Create(diffOutputFile);
                JsonSerializer.Serialize(fs, diff, JsonConfig.configJsonOptions);
                Logger.Info($"File changes for file {nativePath} written to path: {diffOutputFile}");
            } catch (Exception e) {
                Logger.Error($"Failed to find changes for file {relativePath}: {e.Message}");
            }
        }
    }

    private void AttemptUpgrade()
    {
        using var context = PrepareWorkspace();

        foreach (var relativePath in upgradeableFileList) {
            try {
                var fmt = PathUtils.ParseFileFormat(relativePath);
                if (!sourceFormats.TryGetValue(fmt, out var converter) || converter?.enabled != true) {
                    continue;
                }

                var sourcePath = Path.Combine(sourceFolder, relativePath);
                var destinationPath = Path.Combine(destinationFolder, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                string? nativePath = null;
                if (relativePath.StartsWith("natives")) {
                    nativePath = relativePath;
                }

                var sourceFile = context.sourceEnv.ResourceManager.ReadOrGetFileResource(sourcePath, nativePath);
                if (sourceFile == null) {
                    Logger.Warn($"Could not load source file: {sourcePath} (native: {nativePath ?? "unknown"})");
                    continue;
                }

                if (!converter.Upgrade(sourceFile, destinationPath, context)) {
                    Logger.Warn($"Failed to upgrade file: {sourcePath} (native: {nativePath ?? "unknown"})");
                    continue;
                }
            } catch (Exception e) {
                Logger.Error($"Failed to handle upgrade for file {relativePath}: {e.Message}");
            }
        }
    }


    private UpgradeContext PrepareWorkspace()
    {
        var originalEnv = new Workspace(workspace.Env.Config.Clone(workspace.Env.Config.Resources.Clone())) { AllowUseLooseFiles = false };
        var updatedEnv = new Workspace(workspace.Env.Config.Clone(workspace.Env.Config.Resources.Clone())) { AllowUseLooseFiles = false };

        if (Converters.OfType<RSZConverter>().FirstOrDefault() is RSZConverter rsz) {
            var customSrcEnv = !string.IsNullOrEmpty(rsz.sourceRszJsonPath) && rsz.sourceRszJsonPath != AppConfig.Instance.GetGameRszJsonPath(workspace.Game);
            var customTargetEnv = !string.IsNullOrEmpty(rsz.targetRszJsonPath) && rsz.targetRszJsonPath != AppConfig.Instance.GetGameRszJsonPath(workspace.Game);
            if (customSrcEnv) {
                originalEnv.Config.Resources.LocalPaths.RszPatchFiles = [rsz.sourceRszJsonPath];
            }
            if (customTargetEnv) {
                updatedEnv.Config.Resources.LocalPaths.RszPatchFiles = [rsz.targetRszJsonPath];
            }
        }

        originalEnv.Config.PakFiles = workspace.Env.PakReader.PakFilePriority.Where(sourcePaks.Contains).ToArray();
        updatedEnv.Config.PakFiles = workspace.Env.PakReader.PakFilePriority.Where(targetPaks.Contains).ToArray();

        var originalWs = new ContentWorkspace(originalEnv, new PatchDataContainer("!"), null);
        var updatedWs = new ContentWorkspace(updatedEnv, new PatchDataContainer("!"), null);
        originalWs.ResourceManager.SetupFileLoaders(typeof(MeshLoader).Assembly);
        updatedWs.ResourceManager.SetupFileLoaders(typeof(MeshLoader).Assembly);

        return new UpgradeContext(originalWs, updatedWs);
    }

    private class UpgradeContext : IDisposable
    {
        public ContentWorkspace sourceEnv;
        public ContentWorkspace updatedEnv;

        public UpgradeContext(ContentWorkspace sourceEnv, ContentWorkspace targetEnv)
        {
            this.sourceEnv = sourceEnv;
            this.updatedEnv = targetEnv;
        }

        public void Dispose()
        {
            sourceEnv.Dispose();
            updatedEnv.Dispose();
        }
    }


    private class TexConverter : FileConverter
    {
        private string? exportFormat;

        public override bool CanUpgrade(string sourceFile, REFileFormat format) => format.format == KnownFileFormats.Texture;

        public override bool ShowSettings()
        {
            ImGui.SameLine();
            ImguiHelpers.ValueCombo("Export format", TexFile.AllVersionConfigsWithExtension, TexFile.AllVersionConfigs, ref exportFormat);
            return !string.IsNullOrEmpty(exportFormat);
        }

        public override bool Upgrade(FileHandle file, string destinationPath, UpgradeContext context)
        {
            var tex = file.GetFile<TexFile>();
            if (!string.IsNullOrEmpty(exportFormat)) {
                var pathVersion = TexFile.GetFileExtension(exportFormat);
                if (pathVersion != 0) {
                    destinationPath = PathUtils.ChangeFileVersion(destinationPath, (int)pathVersion);
                    tex.ChangeVersion(exportFormat);
                }
            }

            file.Save(context.updatedEnv, destinationPath);
            Logger.Info($"Saved file {file.Filepath} to {destinationPath}");
            return true;
        }
    }

    private class MeshConverter : FileConverter
    {
        private string? exportFormat;

        public override bool CanUpgrade(string sourceFile, REFileFormat format) => format.format == KnownFileFormats.Mesh;

        public override bool ShowSettings()
        {
            ImGui.SameLine();
            ImguiHelpers.ValueCombo("Export format", MeshFile.AllVersionConfigsWithExtension, MeshFile.AllVersionConfigs, ref exportFormat);
            return !string.IsNullOrEmpty(exportFormat);
        }

        public override bool Upgrade(FileHandle file, string destinationPath, UpgradeContext context)
        {
            var mesh = file.GetFile<MeshFile>();
            if (!string.IsNullOrEmpty(exportFormat)) {
                var pathVersion = MeshFile.GetFilePathVersion(exportFormat);
                if (pathVersion != 0) {
                    destinationPath = PathUtils.ChangeFileVersion(destinationPath, (int)pathVersion);
                    mesh.ChangeVersion(exportFormat);
                }
            }

            file.Save(context.updatedEnv, destinationPath);
            Logger.Info($"Saved file {file.Filepath} to {destinationPath}");
            return true;
        }
    }

    private class MsgConverter : FileConverter
    {
        public override bool CanUpgrade(string sourceFile, REFileFormat format) => format.format is KnownFileFormats.Message;
        private int updateVersion;

        public override bool ShowSettings()
        {
            ImGui.InputInt("Changed file version", ref updateVersion);
            ImGui.TextColored(Colors.Note, "Leave the value at 0 to keep the current version");
            return true;
        }

        public override bool Upgrade(FileHandle file, string destinationPath, UpgradeContext context)
        {
            if (updateVersion != 0) {
                destinationPath = PathUtils.ChangeFileVersion(destinationPath, updateVersion);
            }

            return DoDefaultDiffPatch(file, destinationPath, context, out var updatedFile);
        }

        protected override bool ChangeVersion(FileHandle file, int version, UpgradeContext context)
        {
            var msg = file.GetFile<MsgFile>();
            msg.Header.Data.version = (uint)updateVersion;
            return true;
        }
    }

    private class RSZConverter : FileConverter
    {
        public override string? Label => "RSZ Files";

        public string sourceRszJsonPath = "";
        public string targetRszJsonPath = "";

        public override bool CanUpgrade(string sourceFile, REFileFormat format) => format.format is KnownFileFormats.UserData or KnownFileFormats.Prefab or KnownFileFormats.Scene;

        public override void Init(ContentWorkspace workspace)
        {
            sourceRszJsonPath = targetRszJsonPath = AppConfig.Instance.GetGameRszJsonPath(workspace.Game) ?? "";
        }

        public override bool ShowSettings()
        {
            AppImguiHelpers.InputFilepath("Source Version RSZ JSON", ref sourceRszJsonPath, FileFilters.JsonFile);
            ImguiHelpers.Tooltip("The RSZ JSON of the source file game version.");

            AppImguiHelpers.InputFilepath("Target Version RSZ JSON", ref targetRszJsonPath, FileFilters.JsonFile);
            ImguiHelpers.Tooltip("The RSZ JSON of the upgraded game version.");

            ImGui.TextColored(Colors.Note, "Both RSZ JSON paths are optional. If unspecified, the currently active one will be used.");
            return true;
        }

        public override bool Upgrade(FileHandle file, string destinationPath, UpgradeContext context)
        {
            if (!DoDefaultDiffPatch(file, destinationPath, context, out var updatedFile)) {
                return false;
            }
            // note: at this point we could open the destination path and do additional upgrades on top of the base patch (eg. format changes, ...)
            return true;
        }
    }

    private abstract class FileConverter
    {
        public bool enabled = true;

        public virtual string? Label => null;
        public abstract bool CanUpgrade(string sourceFile, REFileFormat format);
        public virtual void Init(ContentWorkspace workspace) {}
        public abstract bool ShowSettings();
        public abstract bool Upgrade(FileHandle file, string destinationPath, UpgradeContext context);

        // NOTE: would it make sense to implement this as part of the file loader directly?
        protected virtual bool ChangeVersion(FileHandle file, int version, UpgradeContext context) => false;

        protected bool TryChangeFormatVersionIfNeeded(FileHandle file, REFileFormat destinationFormat, UpgradeContext context)
        {
            if (destinationFormat.version == file.Format.version) return true;

            if (ChangeVersion(file, destinationFormat.version, context)) {
                Logger.Info($"Changed file version: {file.Filepath} -> {destinationFormat.version} (native: {file.NativePath ?? "unknown"})");
                return true;
            }

            return false;
        }

        protected bool DoDefaultDiffPatch(FileHandle sourceFile, string destinationPath, UpgradeContext context, [MaybeNullWhen(false)] out FileHandle updatedFile)
        {
            var sourcePath = sourceFile.Filepath;
            var nativePath = sourceFile.NativePath;

            var destinationFormat = PathUtils.ParseFileFormat(destinationPath);

            if (sourceFile.DiffHandler == null) {
                if (destinationFormat.version != sourceFile.Format.version && TryChangeFormatVersionIfNeeded(sourceFile, destinationFormat, context)) {
                    updatedFile = sourceFile;
                    return true;
                }

                Logger.Warn($"File is not partially patchable: {sourcePath} (native: {nativePath ?? "unknown"})");
                File.Copy(sourcePath, destinationPath, true);
                updatedFile = null;
                return false;
            }

            var existsSourceNativesFile = !string.IsNullOrEmpty(nativePath) && context.updatedEnv.Env.PakReader.FileExists(nativePath);
            var diff = sourceFile.DiffHandler.FindDiff(sourceFile);
            updatedFile = nativePath == null ? null : context.updatedEnv.ResourceManager.ReadOrGetFileResource(nativePath, nativePath);

            // if there's no diff, there's 2 situations this could be:
            // - !existsSourceNativesFile: either the file is a fully custom file, in which case we might want to try and migrate any structural changes (versions, RSZ classes, ...)
            // - existsSourceNativesFile: or it's actually just functionally identical to the source file, in which case there's no reason to do anything
            if (existsSourceNativesFile && diff == null) {
                if (updatedFile != null) {
                    Logger.Info($"No changes detected for file {nativePath}, copying vanilla file from updated PAK list.");
                    using var ofs = File.Create(destinationPath);
                    updatedFile.Stream.CopyTo(ofs);
                } else {
                    Logger.Info($"No changes detected for file {nativePath}, copying with no changes.");
                    File.Copy(sourcePath, destinationPath, true);
                    updatedFile = context.updatedEnv.ResourceManager.ReadOrGetFileResource(destinationPath, nativePath);
                    if (updatedFile == null) {
                        Logger.Error($"Copied file could not be re-opened: {destinationPath}");
                        return false;
                    }
                }
                return true;
            }

            // we have a diff and a diffable file, let ApplyDiff handle the rest
            if (diff != null && updatedFile?.DiffHandler != null) {
                updatedFile.DiffHandler.ApplyDiff(diff);
                TryChangeFormatVersionIfNeeded(updatedFile, destinationFormat, context);
                if (updatedFile.Save(context.updatedEnv, destinationPath)) {
                    Logger.Info($"Successfully migrated changes for updated file {nativePath}");
                    return true;
                } else {
                    Logger.Error($"Failed to save updated file {nativePath}");
                    return false;
                }
            }

            Logger.Warn($"Copying file with no changes: {sourcePath} (native: {nativePath ?? "unknown"})");
            File.Copy(sourcePath, destinationPath, true);
            if (updatedFile == null) {
                updatedFile = context.updatedEnv.ResourceManager.ReadOrGetFileResource(destinationPath, nativePath);
                if (updatedFile == null) {
                    Logger.Error($"Copied file could not be re-opened: {destinationPath}");
                    return false;
                }
                if (destinationFormat.version != sourceFile.Format.version) {
                    if (TryChangeFormatVersionIfNeeded(updatedFile, destinationFormat, context)) {
                        updatedFile.Save(context.updatedEnv, PathUtils.ChangeFileVersion(destinationPath, destinationFormat.version));
                        return true;
                    } else {
                        Logger.Error($"Failed to upgrade file format for file: {destinationPath}");
                    }
                }
            }
            return true;
        }
    }
}
