using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Il2cpp;

namespace ContentEditor.App.ImguiHandling;

public class ResourcePathPicker : IObjectUIHandler
{
    public string? FileExtensionFilter { get; } = "All files|*.*";
    public KnownFileFormats[] FileFormats { get; }

    /// <summary>
    /// When true, path is expected to be an internal path (appsystem/stm/texture.tex).
    /// When false, path is expected to be a natives path (natives/stm/appsystem/stm/texture.tex.71567213).
    /// </summary>
    public bool SaveWithNativePath { get; init; }

    public ResourcePathPicker()
    {
        FileFormats = [];
    }

    public ResourcePathPicker(ContentWorkspace? ws, params KnownFileFormats[] allowedFormats)
    {
        var isKnownFormats = (allowedFormats.Length > 1 || allowedFormats.Length == 1 && allowedFormats[0] != KnownFileFormats.Unknown);
        FileFormats = isKnownFormats ? allowedFormats : [];
        if (ws != null && isKnownFormats) {
            FileExtensionFilter = string.Join(
                ",",
                allowedFormats.SelectMany(format => ws.Env
                    .GetFileExtensionsForFormat(format)
                    .Select(ext => $"{format} .{ext}|*.{ext}.*")));
        }
    }

    public ResourcePathPicker(ContentWorkspace? ws, RszField field)
        : this(ws, [TypeCache.GetResourceFormat(field.original_type)])
    {
    }

    public void OnIMGUI(UIContext context)
    {
        var currentPath = context.Get<string>();
        var newPath = context.state ?? currentPath;
        if (AppImguiHelpers.InputFilepath(context.label, ref newPath, FileExtensionFilter)) {
            context.Changed = true;
            context.state = newPath;
        }

        if (ImGui.BeginPopupContextItem()) {
            var ws = context.GetWorkspace();
            if (!string.IsNullOrWhiteSpace(newPath ?? currentPath) && ImGui.Button("Open file")) {
                ImGui.CloseCurrentPopup();
                if (ws != null) {
                    var resolvedPath = ws.Env.ResolveFilepath(currentPath);
                    if (resolvedPath == null) {
                        Logger.Error("File not found: " + currentPath);
                    } else if (!ws.ResourceManager.CanLoadFile(resolvedPath)) {
                        Logger.Error("Unable to load file: " + resolvedPath);
                    } else if (!ws.ResourceManager.TryGetOrLoadFile(resolvedPath, out var file)) {
                        Logger.Error("Failed to load file: " + resolvedPath);
                    } else {
                        EditorWindow.CurrentWindow?.AddFileEditor(file);
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(newPath ?? currentPath) && ImGui.Button("Extract file ...")) {
                ImGui.CloseCurrentPopup();
                if (ws != null) {
                    var resolvedPath = ws.Env.ResolveFilepath(currentPath);
                    if (resolvedPath == null) {
                        Logger.Error("File not found: " + currentPath);
                    } else {
                        PlatformUtils.ShowSaveFileDialog(
                            initialFile: Path.GetFileName(resolvedPath),
                            filter: PathUtils.ParseFileFormat(currentPath).format + "|" + PathUtils.GetFilenameExtensionWithSuffixes(resolvedPath).ToString(),
                            callback: (outpath) => {
                                using var file = ws.Env.FindSingleFile(resolvedPath);
                                using var outfs = File.Create(outpath);
                                file?.CopyTo(outfs);
                            });
                    }
                }
            }
            if (ws?.CurrentBundle != null && ImGui.Button("Save to bundle ...")) {
                if (ws.ResourceManager.TryResolveFile(currentPath, out var file)) {
                    var wnd = EditorWindow.CurrentWindow!;
                    SaveFileToBundle(ws, file, (bundle, savePath, localPath, nativePath) => {
                        file.Save(ws, savePath);
                        bundle.AddResource(localPath, nativePath);
                        Logger.Info("File saved to " + savePath);
                        wnd.InvokeFromUIThread(() => {
                            ApplyPathChange(context, nativePath, wnd);
                        });
                    });
                } else {
                    Logger.Error("File could not be found!");
                }
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Button("Find files ...")) {
                var exts = FileExtensionFilter?.Split('|').Where((x, i) => i % 2 == 1).Select(x => x.Replace("*.", "").Replace(".*", "")).ToArray() ?? [];
                var extRegex = string.Join("|", exts);
                if (exts.Length == 0 || exts.Length == 1 && exts[0] == "*") {
                    EditorWindow.CurrentWindow!.AddSubwindow(new PakBrowser(ws!.Env, null));
                } else {
                    if (exts.Length > 1) {
                        extRegex = "(?:" + extRegex + ")";
                    }
                    var pattern = ws!.Env.BasePath + "**\\." + extRegex + "\\.**";
                    EditorWindow.CurrentWindow!.AddSubwindow(new PakBrowser(ws.Env, null) { CurrentDir = pattern });
                }
            }
            ImGui.EndPopup();
        }

        if (newPath != currentPath) {
            if (ImGui.Button("Update path")) {
                ApplyPathChange(context, newPath);
                context.state = null;
            }
            if (ImguiHelpers.SameLine() && ImGui.Button("Cancel change")) {
                context.Changed = false;
                context.state = null;
            }
        }

        // validate the filepath
        if (SaveWithNativePath) {
            // native path
            if (context.state != null && (Path.IsPathFullyQualified(context.state) || !context.state.StartsWith("natives/") || PathUtils.ParseFileFormat(context.state).version == -1)) {
                ImGui.TextColored(Colors.Warning, "The given file path may not resolve properly ingame.\nEnsure it's a native path (including the natives/stm/ part and with file extension version)");
            }
        } else {
            // internal path
            if (context.state != null && (Path.IsPathFullyQualified(context.state) || PathUtils.ParseFileFormat(context.state).version != -1 || context.state.Contains("natives/"))) {
                ImGui.TextColored(Colors.Warning, "The given file path may not resolve properly ingame.\nEnsure it's an internal path (without the natives/stm/ part and no file extension version)");
            }
        }

        if (FileFormats.Length != 0 && !string.IsNullOrEmpty(context.state) && !string.IsNullOrEmpty(FileExtensionFilter)) {
            var parsed = PathUtils.ParseFileFormat(context.state);
            if (!FileFormats.Contains(parsed.format)) {
                ImGui.TextColored(Colors.Warning, "The file may be an incorrect type. Expected file types: " + FileExtensionFilter);
            }
        }

        // TODO expandable resource preview
    }

    private void ApplyPathChange(UIContext context, string newPath, WindowBase? window = null)
    {
        newPath = PathUtils.GetNativeFromFullFilepath(newPath) ?? newPath;
        if (SaveWithNativePath) {
            newPath = PathUtils.GetInternalFromNativePath(newPath);
        } else {
            newPath = PathUtils.RemoveNativesFolder(newPath);
            var format = PathUtils.ParseFileFormat(newPath);
            if (format.version != -1) {
                newPath = PathUtils.GetFilepathWithoutSuffixes(newPath).ToString();
            }
        }
        UndoRedo.RecordSet(context, newPath, window);
        context.state = newPath;
    }

    public delegate void BundleSaveFileCallback(Bundle bundle, string savePath, string localPath, string nativePath);

    public static void SaveFileToBundle(ContentWorkspace workspace, FileHandle file, BundleSaveFileCallback saveCallback)
    {
        var bundle = workspace.CurrentBundle;
        if (bundle == null) {
            Logger.Error("Can't save file - no active bundle");
            return;
        }

        var fn = file.Filename.ToString();
        var bundleFolder = workspace.BundleManager.GetBundleFolder(bundle);
        var nativeFilepath = file.NativePath ?? PathUtils.GetNativeFromFullFilepath(file.Filepath) ?? fn;
        var localFilepath = fn;
        var suggestedSavePath = Path.Combine(bundleFolder, localFilepath);
        if (File.Exists(suggestedSavePath)) {
            localFilepath = PathUtils.RemoveNativesFolder(nativeFilepath);
            suggestedSavePath = Path.Combine(bundleFolder, localFilepath);
            Logger.Warn($"The file {suggestedSavePath} already exists, defaulting to the full file path instead.");
        }
        var orgFilename = Path.GetFileName(suggestedSavePath);
        var saveFolder = Path.GetDirectoryName(suggestedSavePath)!;
        if (!Directory.Exists(saveFolder)) {
            Directory.CreateDirectory(saveFolder);
        }
        PlatformUtils.ShowSaveFileDialog((path) => {
            path = path.NormalizeFilepath();
            if (!path.StartsWith(bundleFolder)) {
                Logger.Error("Chosen filepath is not inside the bundle folder! Use Save As instead if it was intentional.");
                return;
            }
            localFilepath = path.Replace(bundleFolder, "").TrimStart('/');
            var newFilename = Path.GetFileName(path);
            if (!newFilename.Equals(orgFilename, StringComparison.InvariantCultureIgnoreCase)) {
                Logger.Warn($"The filename has been automatically renamed from {orgFilename} to {newFilename} for the native path. If this was not intentional, modify the resource_listing entry in the bundle.json.");
                nativeFilepath = nativeFilepath.Replace(orgFilename, newFilename);
            }

            saveCallback.Invoke(bundle, path, localFilepath, nativeFilepath);
            workspace.BundleManager.SaveBundle(bundle);
        }, suggestedSavePath);
    }
}
