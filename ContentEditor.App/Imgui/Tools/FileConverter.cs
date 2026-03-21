using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.BackgroundTasks;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.DDS;

namespace ContentEditor.App;

public class FileConverter : BaseWindowHandler
{
    public override string HandlerName => "File Conversion";

    private string sourceFolder = "";
    private string destinationFolder = "";

    private HashSet<string> sourcePaks = new();
    private HashSet<string> targetPaks = new();

    private List<string> upgradeableFileList = new();
    private List<string> notUpgradeableFileList = new();

    private enum ConversionMode
    {
        Select,
        Conversion,
        Upgrade,
    }

    private ConversionMode mode = ConversionMode.Select;

    private Dictionary<REFileFormatFull, FileConversionHandler?> sourceFormats = new();

    private static Type[] AvailableConverters = typeof(FileConverter).GetNestedTypes(System.Reflection.BindingFlags.NonPublic).Where(t => t.IsAssignableTo(typeof(FileConversionHandler)) && !t.IsAbstract).ToArray();
    private static FileConversionHandler[] StaticConverters = AvailableConverters.Select(c => (FileConversionHandler)Activator.CreateInstance(c)!).ToArray();
    private readonly List<FileConversionHandler> Converters = new();

    private bool allConverterSettingsReady = false;
    private bool sourceChanged = true;
    public override void OnIMGUI()
    {
        sourceChanged |= AppImguiHelpers.InputFolder("Source Folder", ref sourceFolder);
        ImguiHelpers.Tooltip("The folder containing the files you wish to upgrade.");

        AppImguiHelpers.InputFolder("Destination Folder", ref destinationFolder);
        ImguiHelpers.Tooltip("The folder into which to store the upgraded files.");

        // TODO non-RT to RT conversion option

        if (string.IsNullOrEmpty(sourceFolder)) {
            return;
        }

        if (ImguiHelpers.CSharpEnumCombo("Conversion mode", ref mode)) {
            sourceChanged = true;
        }

        if (sourceFolder == destinationFolder) {
            ImGui.TextColored(Colors.Warning, "The source and target folders should not be the same."u8);
            return;
        }

        if (mode == ConversionMode.Select) return;
        if (mode == ConversionMode.Upgrade) {
            ImGui.TextColored(Colors.Note, "Upgrade mode is intended for upgrading files from one version of a game to another (e.g. updating from a previous patch)."u8);
        }
        if (mode == ConversionMode.Conversion) {
            ImGui.TextColored(Colors.Note, "Conversion mode is used to batch convert several files to a different file format."u8);
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
                var format = PathUtils.ParseFileFormatFull(file);

                var converter = Converters.FirstOrDefault(c => mode == ConversionMode.Conversion ? c.CanConvert(filepath, format) : c.CanUpgrade(filepath, format));
                if (converter == null) {
                    converter = StaticConverters.FirstOrDefault(c => mode == ConversionMode.Conversion ? c.CanConvert(filepath, format) : c.CanUpgrade(filepath, format));
                    if (converter != null) {
                        Converters.Add(converter = (FileConversionHandler)Activator.CreateInstance(converter.GetType())!);
                        converter.Init(workspace);
                    }
                }
                sourceFormats.TryAdd(format, converter);
                if (converter == null) {
                    notUpgradeableFileList.Add(relativePath);
                } else {
                    upgradeableFileList.Add(relativePath);
                    converter.AddFormat(format);
                }
            }
            sourceChanged = false;
        }

        var avail = ImGui.GetContentRegionAvail() - new Vector2(0, 32 * UI.UIScale + ImGui.GetStyle().FramePadding.Y);
        if (!ImGui.BeginChild("FileList", avail)) {
            ImGui.EndChild();
            return;
        }

        if (ImGui.BeginTabBar("UpgradeTabs"u8)) {
            var unconvertable = new List<REFileFormatFull>();
            if (ImGui.BeginTabItem("File Formats"u8)) {
                var allReady = true;
                var atLeastOneReady = false;
                var shownConverters = new HashSet<FileConversionHandler>();
                foreach (var (format, converter) in sourceFormats.OrderBy(f => f.Key.format)) {
                    if (converter == null) {
                        unconvertable.Add(format);
                        continue;
                    }
                    if (!shownConverters.Add(converter)) continue;
                    ImGui.PushID(format.GetHashCode());

                    var firstFmt = converter.SourceFormats.FirstOrDefault();
                    var label = string.IsNullOrEmpty(converter.Label) ? $"{firstFmt}" : converter.Label;
                    ImGui.Checkbox(label, ref converter.enabled);
                    if (converter.SourceFormats.Count >= 2 && ImGui.BeginItemTooltip()) {
                        ImGui.SeparatorText("Format List"u8);
                        foreach (var fmt in converter.SourceFormats) {
                            ImGui.BulletText(fmt.ToString());
                        }
                        ImGui.EndTooltip();
                    }
                    if (converter.enabled) {
                        var isReady = converter.ShowSettings(mode);
                        if (!isReady) {
                            allReady = false;
                        } else {
                            atLeastOneReady = true;
                        }
                        ImGui.Spacing();
                    }

                    ImGui.PopID();
                }

                allConverterSettingsReady = allReady && atLeastOneReady;

                if (unconvertable.Count > 0 && ImGui.TreeNode("Not convertable file formats")) {
                    foreach (var format in unconvertable) {
                        ImGui.Text(format.ToString());
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                            EditorWindow.CurrentWindow?.CopyToClipboard(format.ToString());
                        }
                    }
                    ImGui.TreePop();
                }
                ImGui.EndTabItem();
            }
            if (mode == ConversionMode.Upgrade && ImGui.BeginTabItem("PAK List"u8)) {
                ImGui.TextColored(Colors.Note, """
                    The PAK file list is used to compare the files with their original counterparts and only apply the minimal necessary changes to make the file equivalent.
                    This may or may not work correctly for all files depending on what kind of changes have been made both on the mod side and on the original file side.
                    """u8);
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
            if (ImGui.BeginTabItem("File List"u8)) {
                ImGui.SetNextItemOpen(true, ImGuiCond.Appearing);
                if (ImGui.TreeNode($"Convertable Files ({upgradeableFileList.Count})")) {
                    foreach (var file in upgradeableFileList) {
                        ImGui.Text(file);
                        if (mode == ConversionMode.Upgrade && !file.StartsWith("natives")) {
                            ImGui.SameLine();
                            ImGui.PushID(file);
                            ImGui.Button($"{AppIcons.SI_GenericWarning}");
                            ImguiHelpers.TooltipColored("The file path is not contained in the natives/ path, the upgrader is unable to compare with original game files.\nRSZ data upgrade will be attempted but might not work as expected.", Colors.Warning);
                            ImGui.PopID();
                        }
                    }
                    ImGui.TreePop();
                }

                if (notUpgradeableFileList.Count > 0 && ImGui.TreeNode($"NOT Convertable Files ({notUpgradeableFileList.Count})")) {
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

        using var _ = ImguiHelpers.Disabled(!allConverterSettingsReady);
        if (ImGui.Button("Start conversion"u8)) {
            AttemptUpgrade();
        }
        if (mode == ConversionMode.Upgrade) {
            ImGui.SameLine();
            if (ImGui.Button("Find Vanilla Changes"u8)) {
                CompareChangedFiles();
            }
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
                var fmt = PathUtils.ParseFileFormatFull(relativePath);
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

        return new UpgradeContext(originalWs, updatedWs, mode);
    }

    private class UpgradeContext : IDisposable
    {
        public ContentWorkspace sourceEnv;
        public ContentWorkspace updatedEnv;
        public ConversionMode mode;

        public UpgradeContext(ContentWorkspace sourceEnv, ContentWorkspace targetEnv, ConversionMode mode)
        {
            this.sourceEnv = sourceEnv;
            this.updatedEnv = targetEnv;
            this.mode = mode;
        }

        public void Dispose()
        {
            sourceEnv.Dispose();
            updatedEnv.Dispose();
        }
    }


    private class TexConverter : FileConversionHandler
    {
        private string? exportFormat;
        private bool compressTextures = true;
        private bool generateMips = true;

        private static readonly string[] ImageExtensions = { ".PNG", ".TGA", ".DDS" };
        private static readonly string[] ImageExtensions2 = { "png", "tga" };

        public override bool CanConvert(string sourceFile, REFileFormatFull format) => format.format == KnownFileFormats.Texture || ImageExtensions.Contains(Path.GetExtension(sourceFile).ToUpperInvariant());

        private static string[] AllVersionsInclExportsExt { get => field ??= ImageExtensions.Concat(TexFile.AllVersionConfigsWithExtension).ToArray(); }
        private static string[] AllVersionsInclExports { get => field ??= new string[] { "png", "tga", "dds" }.Concat(TexFile.AllVersionConfigs).ToArray(); }

        public override bool ShowSettings(ConversionMode mode)
        {
            ImGui.SameLine();
            var allext = mode == ConversionMode.Conversion ? AllVersionsInclExportsExt : TexFile.AllVersionConfigsWithExtension;
            var all = mode == ConversionMode.Conversion ? AllVersionsInclExports : TexFile.AllVersionConfigs;
            ImGui.SetNextItemWidth(ImGui.CalcItemWidth() / 2);
            ImguiHelpers.ValueCombo("Target format", allext, all, ref exportFormat);
            if (mode == ConversionMode.Conversion && SourceFormats.Any(f => f.extension.Equals("png", StringComparison.OrdinalIgnoreCase) || f.extension.Equals("tga", StringComparison.OrdinalIgnoreCase))) {
                ImGui.SameLine();
                ImGui.Checkbox("Compress (BC7)", ref compressTextures);
                ImguiHelpers.Tooltip("Only used for PNG/TGA source files");
                ImGui.SameLine();
                ImGui.Checkbox("Generate MipMaps", ref generateMips);
                ImguiHelpers.Tooltip("Only used for PNG/TGA source files");
            }
            return !string.IsNullOrEmpty(exportFormat);
        }

        public override bool Upgrade(FileHandle file, string destinationPath, UpgradeContext context)
        {
            if (exportFormat == null) return false;

            if (file.Format.format == KnownFileFormats.Texture) {
                var tex = file.GetFile<TexFile>();
                if (string.IsNullOrEmpty(exportFormat)) {
                    file.Save(context.updatedEnv, destinationPath);
                    Logger.Info($"Saved file {file.Filepath} to {destinationPath}");
                    return true;
                }

                if (exportFormat == "png" || exportFormat == "tga" || exportFormat == "dds") {
                    var dd = PathUtils.GetFilepathWithoutExtensionOrVersion(destinationPath);
                    // var updatedDest = Path.ChangeExtension(destinationPath, $".{exportFormat}");
                    destinationPath = dd.ToString() + "." + exportFormat;
                    var texture = new Texture().LoadFromTex(tex);
                    texture.SaveAs(destinationPath);
                    Logger.Info($"Saved file {file.Filepath} as {destinationPath}");
                } else {
                    var pathVersion = TexFile.GetFileExtension(exportFormat);
                    if (pathVersion != 0) {
                        destinationPath = PathUtils.ChangeFileVersion(destinationPath, (int)pathVersion);
                        tex.ChangeVersion(exportFormat);
                    }

                    file.Save(context.updatedEnv, destinationPath);
                    Logger.Info($"Saved file {file.Filepath} to {destinationPath}");
                }
            } else {

                if (exportFormat == "png" || exportFormat == "tga") {
                    using var texture = new Texture().LoadFromFile(file);
                    destinationPath = Path.ChangeExtension(destinationPath, $".{exportFormat}");
                    texture.SaveAs(destinationPath);
                    Logger.Info($"Saved file {file.Filepath} as {destinationPath}");
                } else {
                    if (exportFormat == "dds") {
                        destinationPath = Path.ChangeExtension(destinationPath, $".{exportFormat}");
                    } else {
                        var pathVersion = TexFile.GetFileExtension(exportFormat);
                        if (pathVersion == 0 || pathVersion == -1) {
                            context.updatedEnv.Env.TryGetFileExtensionVersion("tex", out pathVersion);
                        }

                        destinationPath = Path.ChangeExtension(destinationPath, ".tex");
                        destinationPath = PathUtils.ChangeFileVersion(destinationPath, (int)pathVersion);
                    }

                    void SaveDDS(DDSFile newDds)
                    {
                        if (exportFormat == "dds") {
                            newDds.SaveAs(destinationPath);
                        } else {
                            var tex = new TexFile(new FileHandler());
                            tex.ChangeVersion(exportFormat);
                            tex.LoadDataFromDDS(newDds);
                            TextureLoader.SaveTo(tex, destinationPath);
                            tex.Dispose();
                        }
                        newDds.Dispose();
                        Logger.Info($"Saved file {file.Filepath} to {destinationPath}");
                    }

                    if (Path.GetExtension(file.Filepath) == ".dds") {
                        // no extra conversion needed
                        var ddsF = new DDSFile(new FileHandler(file.Stream));
                        ddsF.Read();
                        SaveDDS(ddsF);
                        return true;
                    }

                    using var texture = new Texture().LoadFromFile(file);
                    var ops = new List<TextureConversionTask.TextureOperation>();
                    if (compressTextures) {
                        var fmt = texture.Format.IsSRGB() ? DxgiFormat.BC7_UNORM_SRGB : DxgiFormat.BC7_UNORM;
                        ops.Add(new TextureConversionTask.ChangeFormat(fmt));
                    }
                    if (generateMips) {
                        ops.Add(new TextureConversionTask.GenerateMipMaps());
                    }

                    var dds = texture.GetAsDDS(maxMipLevel: 1);

                    if (ops.Count == 0) {
                        // save directly
                        SaveDDS(dds);
                    } else {
                        // do async conversion
                        MainLoop.Instance.BackgroundTasks.Queue(new TextureConversionTask(dds, SaveDDS, ops.ToArray()));
                    }
                }
            }

            return true;
        }
    }

    private class MeshConverter : FileConversionHandler
    {
        private string? exportFormat;

        public override bool CanConvert(string sourceFile, REFileFormatFull format) => format.format == KnownFileFormats.Mesh;

        public override bool ShowSettings(ConversionMode mode)
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

    private class MsgConverter : FileConversionHandler
    {
        public override bool CanUpgrade(string sourceFile, REFileFormatFull format) => format.format is KnownFileFormats.Message;
        public override bool CanConvert(string sourceFile, REFileFormatFull format) => format.format is KnownFileFormats.Message;

        private int updateVersion;

        public override bool ShowSettings(ConversionMode mode)
        {
            ImGui.InputInt("New file version", ref updateVersion);
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

    private class RSZConverter : FileConversionHandler
    {
        public override string? Label => "RSZ Files";

        public string sourceRszJsonPath = "";
        public string targetRszJsonPath = "";

        public override bool CanUpgrade(string sourceFile, REFileFormatFull format) => format.format is KnownFileFormats.UserData or KnownFileFormats.Prefab or KnownFileFormats.Scene;

        public override void Init(ContentWorkspace workspace)
        {
            sourceRszJsonPath = targetRszJsonPath = AppConfig.Instance.GetGameRszJsonPath(workspace.Game) ?? "";
        }

        public override bool ShowSettings(ConversionMode mode)
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

    private abstract class FileConversionHandler
    {
        public bool enabled = true;

        public virtual string? Label => null;

        public readonly List<REFileFormatFull> SourceFormats = new();

        public virtual bool CanUpgrade(string sourceFile, REFileFormatFull format) => false;
        public virtual bool CanConvert(string sourceFile, REFileFormatFull format) => false;
        public virtual void Init(ContentWorkspace workspace) {}
        public abstract bool ShowSettings(ConversionMode mode);
        public abstract bool Upgrade(FileHandle file, string destinationPath, UpgradeContext context);

        // NOTE: would it make sense to implement this as part of the file loader directly?
        protected virtual bool ChangeVersion(FileHandle file, int version, UpgradeContext context) => false;

        public void AddFormat(REFileFormatFull format)
        {
            if (!SourceFormats.Contains(format)) {
                SourceFormats.Add(format);
            }
        }

        protected bool TryChangeFormatVersionIfNeeded(FileHandle file, REFileFormatFull destinationFormat, UpgradeContext context)
        {
            if (destinationFormat.version == file.Format.version) return true;

            if (ChangeVersion(file, destinationFormat.version, context)) {
                Logger.Info($"Changed file version: {file.Filepath} -> {destinationFormat} (native: {file.NativePath ?? "unknown"})");
                return true;
            }

            return false;
        }

        protected bool DoDefaultDiffPatch(FileHandle sourceFile, string destinationPath, UpgradeContext context, [MaybeNullWhen(false)] out FileHandle updatedFile)
        {
            var sourcePath = sourceFile.Filepath;
            var nativePath = sourceFile.NativePath;

            var destinationFormat = PathUtils.ParseFileFormatFull(destinationPath);

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
