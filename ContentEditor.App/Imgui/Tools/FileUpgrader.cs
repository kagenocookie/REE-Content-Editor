using System.Numerics;
using System.Text.Json;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

public class FileUpgrader : BaseWindowHandler
{
    public override string HandlerName => "File Upgrader";

    private string sourceRszJsonPath = "";
    private string targetRszJsonPath = "";

    private string sourceFolder = "";
    private string destinationFolder = "";

    private HashSet<string> sourcePaks = new();
    private HashSet<string> targetPaks = new();

    private List<string> upgradeableFileList = new();
    private List<string> notUpgradeableFileList = new();

    public override void Init(UIContext context)
    {
        base.Init(context);
        sourceRszJsonPath = targetRszJsonPath = AppConfig.Instance.GetGameRszJsonPath(workspace.Game) ?? "";
    }

    public override void OnIMGUI()
    {
        AppImguiHelpers.InputFilepath("Source Version RSZ JSON", ref sourceRszJsonPath);
        ImguiHelpers.Tooltip("The RSZ JSON of the source file game version. If unspecified, the currently active one will be used.");

        AppImguiHelpers.InputFilepath("Target Version RSZ JSON", ref targetRszJsonPath);
        ImguiHelpers.Tooltip("The RSZ JSON of the upgraded game version. If unspecified, the currently active one will be used.");

        var sourceChanged = AppImguiHelpers.InputFolder("Source Folder", ref sourceFolder);
        ImguiHelpers.Tooltip("The folder containing the files you wish to upgrade.");

        AppImguiHelpers.InputFolder("Destination Folder", ref destinationFolder);
        ImguiHelpers.Tooltip("The folder into which to store the upgraded files.");

        if (string.IsNullOrEmpty(sourceFolder)) {
            return;
        }

        if (sourcePaks.Count == 0) {
            sourcePaks = workspace.Env.PakReader.PakFilePriority.ToHashSet();
        }
        if (targetPaks.Count == 0) {
            targetPaks = workspace.Env.PakReader.PakFilePriority.ToHashSet();
        }

        if (sourceChanged || upgradeableFileList.Count == 0 && notUpgradeableFileList.Count == 0) {
            foreach (var filepath in Directory.EnumerateFiles(sourceFolder, "*.*", SearchOption.AllDirectories)) {
                var file = Path.GetFileName(filepath.AsSpan());
                var relativePath = Path.GetRelativePath(sourceFolder, filepath);
                var format = PathUtils.ParseFileFormat(file);
                if (format.format is KnownFileFormats.Message or KnownFileFormats.UserData or KnownFileFormats.Scene or KnownFileFormats.Prefab) {
                    upgradeableFileList.Add(relativePath);
                } else {
                    notUpgradeableFileList.Add(relativePath);
                }
            }
        }

        var avail = ImGui.GetContentRegionAvail() - new Vector2(0, 32 * UI.UIScale + ImGui.GetStyle().FramePadding.Y);
        if (!ImGui.BeginChild("FileList", avail)) {
            ImGui.EndChild();
            return;
        }

        if (ImGui.BeginTabBar("UpgradeTabs"u8)) {
            if (ImGui.BeginTabItem("PAK list"u8)) {
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
            if (ImGui.BeginTabItem("File list"u8)) {
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
        if (upgradeableFileList.Count == 0 || !Directory.Exists(destinationFolder)) {
            return;
        }

        if (ImGui.Button("Upgrade"u8)) {
            AttemptUpgrade();
        }
        ImGui.SameLine();
        if (ImGui.Button("Find Vanilla Changes"u8)) {
            CompareChangedFile();
        }
    }

    private void CompareChangedFile()
    {
        var customSrcEnv = !string.IsNullOrEmpty(sourceRszJsonPath) && sourceRszJsonPath != AppConfig.Instance.GetGameRszJsonPath(workspace.Game);
        using var originalEnv = new Workspace(workspace.Env.Config.Clone()) { AllowUseLooseFiles = false };
        using var updatedEnv = new Workspace(workspace.Env.Config.Clone()) { AllowUseLooseFiles = false };

        originalEnv.Config.PakFiles = workspace.Env.PakReader.PakFilePriority.Where(sourcePaks.Contains).ToArray();
        updatedEnv.Config.PakFiles = workspace.Env.PakReader.PakFilePriority.Where(targetPaks.Contains).ToArray();

        using var originalWs = new ContentWorkspace(originalEnv, new PatchDataContainer("!"), null);
        using var updatedWs = new ContentWorkspace(updatedEnv, new PatchDataContainer("!"), null);

        foreach (var relativePath in upgradeableFileList) {
            try {
                var sourcePath = Path.Combine(sourceFolder, relativePath);
                var destinationPath = Path.Combine(destinationFolder, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                if (!relativePath.StartsWith("natives")) {
                    continue;
                }

                string nativePath = relativePath;
                var sourceFile = originalWs.ResourceManager.ReadOrGetFileResource(nativePath, nativePath);
                if (sourceFile == null) {
                    Logger.Warn($"Could not load source file: {sourcePath}");
                    continue;
                }

                var updatedFile = updatedWs.ResourceManager.ReadOrGetFileResource(nativePath, nativePath);
                if (updatedFile == null) {
                    Logger.Warn($"Could not load updated file: {nativePath}");
                    continue;
                }

                if (updatedFile.DiffHandler == null) {
                    continue;
                }

                updatedFile.DiffHandler.LoadBase(updatedWs, sourceFile);
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
        var customSrcEnv = !string.IsNullOrEmpty(sourceRszJsonPath) && sourceRszJsonPath != AppConfig.Instance.GetGameRszJsonPath(workspace.Game);
        using var originalEnv = new Workspace(workspace.Env.Config.Clone()) { AllowUseLooseFiles = false };
        using var updatedEnv = new Workspace(workspace.Env.Config.Clone()) { AllowUseLooseFiles = false };

        originalEnv.Config.PakFiles = workspace.Env.PakReader.PakFilePriority.Where(sourcePaks.Contains).ToArray();
        updatedEnv.Config.PakFiles = workspace.Env.PakReader.PakFilePriority.Where(targetPaks.Contains).ToArray();

        using var originalWs = new ContentWorkspace(originalEnv, new PatchDataContainer("!"), null);
        using var updatedWs = new ContentWorkspace(updatedEnv, new PatchDataContainer("!"), null);

        foreach (var relativePath in upgradeableFileList) {
            try {
                var sourcePath = Path.Combine(sourceFolder, relativePath);
                var destinationPath = Path.Combine(destinationFolder, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                string? nativePath = null;
                if (relativePath.StartsWith("natives")) {
                    nativePath = relativePath;
                }

                var sourceFile = originalWs.ResourceManager.ReadOrGetFileResource(sourcePath, nativePath);
                if (sourceFile == null) {
                    Logger.Warn($"Could not load source file: {sourcePath} (native: {nativePath ?? "unknown"})");
                    continue;
                }

                if (sourceFile.DiffHandler == null) {
                    // note: we could still support format version upgrades in this case (note, also need to handle correct new path if the format version changed)
                    Logger.Warn($"File is not partially patchable: {sourcePath} (native: {nativePath ?? "unknown"})");
                    File.Copy(sourcePath, destinationPath, true);
                    continue;
                }

                var existsSourceNativesFile = !string.IsNullOrEmpty(nativePath) && originalEnv.PakReader.FileExists(nativePath);
                var diff = sourceFile.DiffHandler.FindDiff(sourceFile);
                var updatedFile = nativePath == null ? null : updatedWs.ResourceManager.ReadOrGetFileResource(nativePath, nativePath);

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
                    }
                    continue;
                }

                // we have a diff and a diffable file, let ApplyDiff handle the rest
                if (diff != null && updatedFile?.DiffHandler != null) {
                    updatedFile.DiffHandler.ApplyDiff(diff);
                    if (updatedFile.Save(updatedWs, destinationPath)) {
                        Logger.Info($"Successfully migrated changes for updated file {nativePath}");
                    } else {
                        Logger.Error($"Failed to save updated file {nativePath}");
                    }
                    continue;
                }

                // try and resolve other format specific edge cases?
                // if (sourceFile.Format.format is KnownFileFormats.UserData or KnownFileFormats.Prefab or KnownFileFormats.Scene) {
                //     if (updatedFile == null) {
                //         here we could only try and manually migrate field structure changes to RSZ classes - full JSON re-serialization?
                //     }
                // }

                Logger.Warn($"Copying file with no changes: {sourcePath} (native: {nativePath ?? "unknown"})");
                File.Copy(sourcePath, destinationPath, true);
            } catch (Exception e) {
                Logger.Error($"Failed to handle upgrade for file {relativePath}: {e.Message}");
            }
        }
    }
}
