using System.Collections;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Il2cpp;

namespace ContentEditor.App.ImguiHandling;

public class ResourcePathPicker : IObjectUIHandler
{
    public FileFilter[]? FileExtensionFilter { get; } = FileFilters.AllFiles;
    public KnownFileFormats[] FileFormats { get; }

    /// <summary>
    /// When true, path is expected to be an internal path (appsystem/stm/texture.tex).
    /// When false, path is expected to be a natives path (natives/stm/appsystem/stm/texture.tex.71567213).
    /// </summary>
    public bool UseNativesPath { get; init; }
    public bool IsPathForIngame { get; init; } = true;
    public bool DisableWarnings { get; init; }

    private ContentWorkspace? workspace;

    public ResourcePathPicker()
    {
        FileFormats = [];
    }

    public ResourcePathPicker(ContentWorkspace? ws, params KnownFileFormats[] allowedFormats)
    {
        workspace = ws;
        var isKnownFormats = (allowedFormats.Length > 1 || allowedFormats.Length == 1 && allowedFormats[0] != KnownFileFormats.Unknown);
        FileFormats = isKnownFormats ? allowedFormats : [];
        if (ws != null && isKnownFormats) {
            FileExtensionFilter = allowedFormats.SelectMany(format => ws.Env
                .GetFileExtensionsForFormat(format)
                .Select(ext => new FileFilter(format.ToString(), [$"*.{ext}.*"])))
                .ToArray();
        }
    }

    public ResourcePathPicker(ContentWorkspace? ws, FileFilter[] additionalFilters, params KnownFileFormats[] allowedFormats)
    {
        workspace = ws;
        var isKnownFormats = (allowedFormats.Length > 1 || allowedFormats.Length == 1 && allowedFormats[0] != KnownFileFormats.Unknown);
        FileFormats = isKnownFormats ? allowedFormats : [];
        if (ws != null && isKnownFormats) {
            FileExtensionFilter = allowedFormats.SelectMany(format => ws.Env
                .GetFileExtensionsForFormat(format)
                .Select(ext => new FileFilter(format.ToString(), [$"{ext}.*"])))
                .Concat(additionalFilters)
                .ToArray();
        } else {
            FileExtensionFilter = additionalFilters;
        }
    }

    public ResourcePathPicker(ContentWorkspace? ws, RszField field)
        : this(ws, [TypeCache.GetResourceFormat(field.original_type)])
    {
    }

    public void OnIMGUI(UIContext context)
    {
        var currentPath = context.Get<string>();
        var newPath = context.InitFilterDefault(currentPath);
        if (AppImguiHelpers.InputFilepath(context.label, ref newPath, FileExtensionFilter)) {
            context.Changed = true;
            context.Filter = newPath;
        }

        if (ImGui.BeginPopupContextItem()) {
            var ws = workspace ??= context.GetWorkspace();
            if (!string.IsNullOrWhiteSpace(newPath) && ImGui.Button("Open file")) {
                ImGui.CloseCurrentPopup();
                if (ws != null) {
                    if (ws.ResourceManager.TryGetOrLoadFile(currentPath, out var newFileHandle)) {
                        EditorWindow.CurrentWindow?.AddFileEditor(newFileHandle);
                    } else {
                        Logger.Error("Failed to load file: " + currentPath);
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(newPath) && ImGui.Button("Extract file ...")) {
                ImGui.CloseCurrentPopup();
                if (ws != null) {
                    var resolvedPath = ws.Env.ResolveFilepath(currentPath);
                    if (resolvedPath == null) {
                        Logger.Error("File not found: " + currentPath);
                    } else {
                        PlatformUtils.ShowSaveFileDialog(
                            initialFile: Path.GetFileName(resolvedPath),
                            filter: [new FileFilter(PathUtils.ParseFileFormat(currentPath).format, currentPath)],
                            callback: (outpath) => {
                                using var file = ws.Env.FindSingleFile(resolvedPath);
                                using var outfs = File.Create(outpath);
                                file?.CopyTo(outfs);
                            });
                    }
                }
            }
            if (ws?.CurrentBundle != null && ImGui.Button("Save to bundle ...")) {
                if (ws.ResourceManager.TryResolveGameFile(currentPath, out var file)) {
                    var wnd = EditorWindow.CurrentWindow!;
                    SaveFileToBundle(ws, file, (savePath, localPath, nativePath) => {
                        if (!file.Save(ws, savePath)) return false;

                        Logger.Info("File saved to " + savePath);
                        wnd.InvokeFromUIThread(() => {
                            ApplyPathChange(context, nativePath, wnd);
                        });
                        return true;
                    });
                } else {
                    Logger.Error("File could not be found!");
                }
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Button("Find files ...")) {
                var exts = FileExtensionFilter ?? [];
                var extRegex = string.Join("|", exts.SelectMany(e => e.extensions)).Replace(".*", "");
                if (exts.Length == 0) {
                    EditorWindow.CurrentWindow!.AddSubwindow(new PakBrowser(ws!, null));
                } else {
                    if (exts.Length > 1) {
                        extRegex = "(?:" + extRegex + ")";
                    }
                    var pattern = ws!.Env.BasePath + "**\\." + extRegex + "\\.**";
                    EditorWindow.CurrentWindow!.AddSubwindow(new PakBrowser(ws, null) { CurrentDir = pattern });
                }
            }
            ImGui.EndPopup();
        }

        if (newPath != currentPath) {
            if (ImGui.Button("Update path")) {
                ApplyPathChange(context, newPath);
            }
            if (ImguiHelpers.SameLine() && ImGui.Button("Cancel change")) {
                context.Changed = false;
                context.Filter = currentPath;
            }
        }

        // validate the filepath
        if (IsPathForIngame && UseNativesPath) {
            // native path
            if ((Path.IsPathFullyQualified(context.Filter) || !context.Filter.StartsWith("natives/") || PathUtils.ParseFileFormat(context.Filter).version == -1)) {
                ImGui.TextColored(Colors.Warning, "The given file path may not resolve properly ingame.\nEnsure it's a native path (including the natives/stm/ part and with file extension version)");
            }
        } else if (IsPathForIngame) {
            // internal path
            if ((Path.IsPathFullyQualified(context.Filter) || PathUtils.ParseFileFormat(context.Filter).version != -1 || context.Filter.Contains("natives/"))) {
                ImGui.TextColored(Colors.Warning, "The given file path may not resolve properly ingame.\nEnsure it's an internal path (without the natives/stm/ part and no file extension version)");
            }
        }

        if (!DisableWarnings && FileFormats.Length != 0 && !string.IsNullOrEmpty(context.Filter) && FileExtensionFilter?.Length > 0) {
            var parsed = PathUtils.ParseFileFormat(context.Filter);
            if (!FileFormats.Contains(parsed.format)) {
                ImGui.TextColored(Colors.Warning, "The file may be an incorrect type. Expected file types: " + FileExtensionFilter);
            }
        }

        // TODO expandable resource preview
    }

    private void ApplyPathChange(UIContext context, string newPath, WindowBase? window = null)
    {
        if (!IsPathForIngame) {
            UndoRedo.RecordSet(context, newPath, window);
            context.Filter = newPath;
            return;
        }

        newPath = PathUtils.GetNativeFromFullFilepath(newPath) ?? newPath;
        if (UseNativesPath) {
            newPath = PathUtils.GetInternalFromNativePath(newPath);
        } else {
            newPath = PathUtils.RemoveNativesFolder(newPath);
            var format = PathUtils.ParseFileFormat(newPath);
            if (format.version != -1) {
                newPath = PathUtils.GetFilepathWithoutSuffixes(newPath).ToString();
            }
        }
        UndoRedo.RecordSet(context, newPath, window);
        context.Filter = newPath;
    }

    public delegate bool BundleSaveFileCallback(string savePath, string localPath, string nativePath);

    public static void ShowSaveToBundle(IFileLoader loader, IResourceFile resource, ContentWorkspace workspace, string initialFilename, string? nativePath = null, Action? callback = null)
    {
        if (!string.IsNullOrEmpty(nativePath) && !Path.IsPathFullyQualified(initialFilename) && !initialFilename.IsNativePath()) nativePath = Path.Combine(Path.GetDirectoryName(nativePath)!, initialFilename).Replace('\\', '/').ToLowerInvariant();

        var tempHandle = FileHandle.CreateEmbedded(loader, resource, initialFilename, nativePath);

        ResourcePathPicker.SaveFileToBundle(workspace, tempHandle, (savePath, localPath, nativePath) => {
            var saveSuccess = tempHandle.Save(workspace, savePath);
            callback?.Invoke();
            return saveSuccess;
        });
    }

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
        string suggestedSavePath;
        if (AppConfig.Instance.BundleDefaultSaveFullPath) {
            localFilepath = PathUtils.RemoveNativesFolder(nativeFilepath);
            suggestedSavePath = Path.Combine(bundleFolder, localFilepath);
        } else {
            suggestedSavePath = Path.Combine(bundleFolder, localFilepath);
            if (File.Exists(suggestedSavePath)) {
                localFilepath = PathUtils.RemoveNativesFolder(nativeFilepath);
                suggestedSavePath = Path.Combine(bundleFolder, localFilepath);
                Logger.Warn($"The file {suggestedSavePath} already exists, defaulting to the full file path instead.");
            }
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

            if (File.Exists(path)) {
                Logger.Info($"Target bundle file {path} already exists, replacing it.");
            }

            if (bundle.TryFindResourceByLocalPath(localFilepath, out var previousListing) && previousListing.Target != nativeFilepath) {
                Logger.Info($"File is already stored in the bundle.json. Re-using existing file's native filepath.");
                nativeFilepath = previousListing.Target;
            }

            if (!saveCallback.Invoke(path, localFilepath, nativeFilepath)) {
                Logger.Error("Failed to save file to bundle in path " + localFilepath);
                return;
            }

            if (!bundle.TryFindResourceByNativePath(nativeFilepath, out _)) {
                bundle.AddResource(localFilepath, nativeFilepath, file.Format.format.IsDefaultReplacedBundleResource());
            }

            workspace.BundleManager.SaveBundle(bundle);
        }, suggestedSavePath);
    }
}

public class ResourceListPathPicker : ListHandlerTyped<string>
{
    private ContentWorkspace? ws;
    private KnownFileFormats[] allowedFormats;

    public ResourceListPathPicker(ContentWorkspace? ws, params KnownFileFormats[] allowedFormats)
    {
        this.ws = ws;
        this.allowedFormats = allowedFormats;
    }

    protected override UIContext CreateElementContext(UIContext context, IList list, int elementIndex)
    {
        var ctx = WindowHandlerFactory.CreateListElementContext(context, elementIndex);
        ctx.uiHandler = new ResourcePathPicker(ws, allowedFormats);
        return ctx;
    }
}
