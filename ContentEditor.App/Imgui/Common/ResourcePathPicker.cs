using System.Collections;
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
    public bool UseNativesPath { get; init; }
    public bool IsPathForIngame { get; init; } = true;

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
            FileExtensionFilter = string.Join(
                "|",
                allowedFormats.SelectMany(format => ws.Env
                    .GetFileExtensionsForFormat(format)
                    .Select(ext => $"{format} .{ext}|*.{ext}.*")));
        }
    }

    public ResourcePathPicker(ContentWorkspace? ws, string additionalFilter, params KnownFileFormats[] allowedFormats)
    {
        workspace = ws;
        var isKnownFormats = (allowedFormats.Length > 1 || allowedFormats.Length == 1 && allowedFormats[0] != KnownFileFormats.Unknown);
        FileFormats = isKnownFormats ? allowedFormats : [];
        if (ws != null && isKnownFormats) {
            FileExtensionFilter = string.Join(
                "|",
                allowedFormats.SelectMany(format => ws.Env
                    .GetFileExtensionsForFormat(format)
                    .Select(ext => $"{format} .{ext}|*.{ext}.*"))) + "|" + additionalFilter;
        } else {
            FileExtensionFilter = additionalFilter;
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
            if (!string.IsNullOrWhiteSpace(newPath) && ImGui.Button("Extract file ...")) {
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
                if (ws.ResourceManager.TryResolveGameFile(currentPath, out var file)) {
                    var wnd = EditorWindow.CurrentWindow!;
                    SaveFileToBundle(ws, file, (bundle, savePath, localPath, nativePath) => {
                        file.Save(ws, savePath);
                        bundle.AddResource(localPath, nativePath, file.Format.format.IsDefaultReplacedBundleResource());
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
                context.Filter = "";
            }
            if (ImguiHelpers.SameLine() && ImGui.Button("Cancel change")) {
                context.Changed = false;
                context.Filter = "";
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

        if (FileFormats.Length != 0 && !string.IsNullOrEmpty(context.Filter) && !string.IsNullOrEmpty(FileExtensionFilter)) {
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

    public delegate void BundleSaveFileCallback(Bundle bundle, string savePath, string localPath, string nativePath);

    public static void ShowSaveToBundle(IFileLoader loader, IResourceFile resource, ContentWorkspace workspace, string initialFilename, string? nativePath = null, Action? callback = null)
    {
        if (!string.IsNullOrEmpty(nativePath)) nativePath = Path.Combine(Path.GetDirectoryName(nativePath)!, initialFilename).Replace('\\', '/').ToLowerInvariant();

        var tempHandle = FileHandle.CreateEmbedded(loader, resource, initialFilename, nativePath);

        ResourcePathPicker.SaveFileToBundle(workspace, tempHandle, (bundle, savePath, localPath, nativePath) => {
            if (tempHandle.Save(workspace, savePath)) {
                if (!bundle.TryFindResourceByNativePath(nativePath, out _)) {
                    bundle.AddResource(localPath, nativePath, tempHandle.Format.format.IsDefaultReplacedBundleResource());
                }
            }
            callback?.Invoke();
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
