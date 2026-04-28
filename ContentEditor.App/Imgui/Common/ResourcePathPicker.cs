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

    public PathPickerFlags Flags { get; init; } = PathPickerFlags.IngameDefault;

    private ContentWorkspace? workspace;

    [Flags]
    public enum PathPickerFlags
    {
        None = 0,
        /// <summary>
        /// Skips the confirmation buttons when updating the file path, immediately updating the value when a change is made.
        /// </summary>
        NoConfirmation = 1,
        /// <summary>
        /// If set, will show additional warnings in case of potential issues with ingame behavior.
        /// </summary>
        IsPathForIngame = 2,
        /// <summary>
        /// When set, path is expected to be a natives path (natives/stm/appsystem/stm/texture.tex.71567213).
        /// Otherwise, path is expected to be an internal path (appsystem/stm/texture.tex).
        /// </summary>
        UseNativesPath = 4,
        /// <summary>
        /// Disable warnings for potentially invalid file formats.
        /// </summary>
        DisableFormatWarning = 8,

        IngameDefault = IsPathForIngame,
        IngameDefaultNoConfirm = IsPathForIngame|PathPickerFlags.NoConfirmation,
        EditorOnly = UseNativesPath|NoConfirmation|DisableFormatWarning,
        EditorOnlyConfirmed = UseNativesPath|DisableFormatWarning,
    }

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
            var reFormats = allowedFormats.SelectMany(format => ws.Env
                .GetFileExtensionsForFormat(format)
                .Select(ext => new FileFilter(format.ToString(), [$"{ext}.*"])))
                .ToArray();

            var combinedFilter = reFormats.SelectMany(x => x.extensions)
                .Distinct()
                .ToArray();

            if (combinedFilter.Length > 1) {
                FileExtensionFilter = reFormats
                    .Prepend(new FileFilter("Any supported file", combinedFilter))
                    .ToArray();
            } else {
                FileExtensionFilter = reFormats;
            }
        }
    }

    public ResourcePathPicker(ContentWorkspace? ws, FileFilter[] additionalFilters, params KnownFileFormats[] allowedFormats)
    {
        workspace = ws;
        var isKnownFormats = (allowedFormats.Length > 1 || allowedFormats.Length == 1 && allowedFormats[0] != KnownFileFormats.Unknown);
        FileFormats = isKnownFormats ? allowedFormats : [];
        if (ws != null && isKnownFormats) {
            var reFormats = allowedFormats.SelectMany(format => ws.Env
                .GetFileExtensionsForFormat(format)
                .Select(ext => new FileFilter(format.ToString(), [$"{ext}.*"])))
                .ToArray();

            var combinedFilter = reFormats.SelectMany(x => x.extensions)
                .Concat(additionalFilters.SelectMany(x => x.extensions))
                .Distinct()
                .ToArray();

            if (combinedFilter.Length > 1 && !additionalFilters.Any(f => f.extensions.Order().SequenceEqual(combinedFilter.Order()))) {
                FileExtensionFilter = reFormats
                    .Prepend(new FileFilter("Any supported file", combinedFilter))
                    .Concat(additionalFilters)
                    .ToArray();
            } else {
                FileExtensionFilter = reFormats
                    .Concat(additionalFilters)
                    .ToArray();
            }

        } else {
            FileExtensionFilter = additionalFilters;
        }
    }

    public ResourcePathPicker(ContentWorkspace? ws, RszField field)
        : this(ws, Workspace.GetFormatIncludingSubtypes(TypeCache.GetResourceFormat(field.original_type)))
    {
    }

    public void OnIMGUI(UIContext context)
    {
        var currentPath = context.Get<string>();
        var newPath = context.InitFilterDefault(currentPath);
        var ws = workspace ??= context.GetWorkspace();
        var wnd = context.GetNativeWindow();
        var changed = Show(context.label, ref currentPath, ref newPath, ws!, FileFormats, FileExtensionFilter ?? [], Flags, (nativePath) => {
            wnd?.InvokeFromUIThread(() => {
                ApplyPathChange(context, nativePath, wnd);
            });
        });
        if (changed) {
            context.Changed = false;
            context.Filter = currentPath ?? "";
            ApplyPathChange(context, currentPath ?? "");
        } else {
            context.Filter = newPath;
        }
    }

    public static bool Show(string label, ref string currentPath, ref string pendingPath, ContentWorkspace workspace, KnownFileFormats[] formats, FileFilter[] fileFilters, PathPickerFlags flags, Action<string>? delayedSaveCallback = null)
    {
        ImGui.PushID(label);
        if (flags.HasFlag(PathPickerFlags.NoConfirmation)) {
            pendingPath = currentPath;
        } else {
            pendingPath ??= currentPath;
        }
        var changed = false;
        if (AppImguiHelpers.InputFilepath(label, ref pendingPath, fileFilters)) {
            if (flags.HasFlag(PathPickerFlags.NoConfirmation)) {
                changed = true;
                currentPath = pendingPath;
            }
        }

        if (ImGui.BeginPopupContextItem()) {
            if (!string.IsNullOrWhiteSpace(pendingPath) && ImGui.Button("Open file")) {
                ImGui.CloseCurrentPopup();
                if (workspace.ResourceManager.TryGetOrLoadFile(currentPath, out var newFileHandle)) {
                    EditorWindow.CurrentWindow?.AddFileEditor(newFileHandle);
                } else {
                    Logger.Error("Failed to load file: " + currentPath);
                }
            }
            if (!string.IsNullOrWhiteSpace(pendingPath) && ImGui.Button("Extract file ...")) {
                ImGui.CloseCurrentPopup();
                var resolvedPath = workspace.Env.ResolveFilepath(currentPath);
                if (resolvedPath == null) {
                    Logger.Error("File not found: " + currentPath);
                } else {
                    PlatformUtils.ShowSaveFileDialog(
                        initialFile: Path.GetFileName(resolvedPath),
                        filters: [new FileFilter(PathUtils.ParseFileFormat(currentPath).format, currentPath)],
                        callback: (outpath) => {
                            using var file = workspace.Env.FindSingleFile(resolvedPath);
                            using var outfs = File.Create(outpath);
                            file?.CopyTo(outfs);
                        });
                }
            }
            if (workspace.CurrentBundle != null && ImGui.Button("Save to bundle ...")) {
                if (workspace.ResourceManager.TryResolveGameFile(currentPath, out var file)) {
                    var wnd = EditorWindow.CurrentWindow!;
                    SaveFileToBundle(workspace, file, (savePath, localPath, nativePath) => {
                        if (!file.Save(workspace, savePath)) return false;

                        Logger.Info("File saved to " + savePath);
                        if (delayedSaveCallback != null) {

                        }
                        delayedSaveCallback?.Invoke(nativePath);
                        return true;
                    });
                } else {
                    Logger.Error("File could not be found!");
                }
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Button("Find files ...")) {
                var exts = fileFilters ?? [];
                var extRegex = string.Join("|", exts.SelectMany(e => e.extensions).Distinct()).Replace(".*", "");
                if (exts.Length == 0) {
                    EditorWindow.CurrentWindow!.AddSubwindow(new PakBrowser(workspace, null));
                } else {
                    if (exts.Length > 1) {
                        extRegex = "(?:" + extRegex + ")";
                    }
                    var pattern = workspace.Env.BasePath + "**\\." + extRegex + "\\.**";
                    EditorWindow.CurrentWindow!.AddSubwindow(new PakBrowser(workspace, null) { CurrentDir = pattern });
                }
            }
            ImGui.EndPopup();
        }

        if (!flags.HasFlag(PathPickerFlags.NoConfirmation) && pendingPath != currentPath && (!string.IsNullOrEmpty(pendingPath) || !string.IsNullOrEmpty(currentPath))) {
            if (ImGui.Button("Update path")) {
                currentPath = pendingPath;
                changed = true;
            }
            if (ImguiHelpers.SameLine() && ImGui.Button("Cancel change")) {
                pendingPath = currentPath ?? "";
                changed = true;
            }
        }

        // validate the filepath
        if (flags.HasFlag(PathPickerFlags.IsPathForIngame)) {
            if (flags.HasFlag(PathPickerFlags.UseNativesPath)) {
                // native path
                if ((Path.IsPathFullyQualified(pendingPath) || !pendingPath.StartsWith("natives/") || PathUtils.ParseFileFormat(pendingPath).version == -1)) {
                    ImGui.TextColored(Colors.Warning, "The given file path may not resolve properly ingame.\nEnsure it's a native path (including the natives/stm/ part and with file extension version)");
                }
            } else {
                // internal path
                if ((Path.IsPathFullyQualified(pendingPath) || PathUtils.ParseFileFormat(pendingPath).version != -1 || pendingPath.Contains("natives/"))) {
                    ImGui.TextColored(Colors.Warning, "The given file path may not resolve properly ingame.\nEnsure it's an internal path (without the natives/stm/ part and no file extension version)");
                }
            }
        }

        if (!flags.HasFlag(PathPickerFlags.DisableFormatWarning) && formats.Length > 0 && fileFilters?.Length > 0 && !string.IsNullOrEmpty(currentPath)) {
            var parsed = PathUtils.ParseFileFormat(pendingPath);
            if (!formats.Contains(parsed.format)) {
                ImGui.TextColored(Colors.Warning, "The file may be an incorrect type. Expected file types: " + string.Join(", ", fileFilters.SelectMany(x => x.extensions)).Replace(".*", ""));
            }
        }

        // TODO expandable resource preview
        ImGui.PopID();
        return changed;
    }

    private void ApplyPathChange(UIContext context, string newPath, WindowBase? window = null)
    {
        if (!Flags.HasFlag(PathPickerFlags.IsPathForIngame)) {
            UndoRedo.RecordSet(context, newPath, window);
            context.Filter = newPath;
            return;
        }

        newPath = PathUtils.GetNativeFromFullFilepath(newPath) ?? newPath;
        if (Flags.HasFlag(PathPickerFlags.UseNativesPath)) {
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

    public static void SaveFileToBundle(ContentWorkspace workspace, FileHandle file, BundleSaveFileCallback saveCallback, bool closeFile = true)
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

            bundle.Save();
            if (closeFile && file.References.Count == 0) {
                workspace.ResourceManager.CloseFile(file);
            }
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
