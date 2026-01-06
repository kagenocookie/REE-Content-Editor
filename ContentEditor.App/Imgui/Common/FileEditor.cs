using System.Numerics;
using System.Text.Json;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;

namespace ContentEditor.App.ImguiHandling;

public abstract class FileEditor : IWindowHandler, IRectWindow, IDisposable, IFocusableFileHandleReferenceHolder, IUIContextEventHandler
{
    public virtual bool HasUnsavedChanges => context.Changed || Handle.Modified;
    protected virtual bool AllowJsonCopy => false;

    public virtual string HandlerName { get; } = "File";
    public FileHandle Handle { get; private set; }
    char IWindowHandler.Icon => AppIcons.GetIcon(this, Handle.Resource);

    protected virtual bool CanSave => true;

    public Vector2 Size { get; private set; }
    public Vector2 Position { get; private set; }

    protected bool failedToReadfile = false;
    protected ImGuiWindowFlags windowFlags;
    protected UIContext context = null!;
    internal UIContext Context => context;

    protected virtual bool IsRevertable => true;

    public bool CanClose => Handle.HandleType != FileHandleType.Embedded && context.parent == context.root;

    protected WindowData WindowData => context.Get<WindowData>();
    IRectWindow? IFileHandleReferenceHolder.Parent => context.Get<WindowData>().ParentWindow;

    public bool CanFocus => CanClose;

    protected FileEditor(FileHandle file)
    {
        Handle = file;
        ConnectFile();
    }

    public void Init(UIContext context)
    {
        this.context = context;
        context.SetChangedNoPropagate(Handle.Modified);
    }

    public void Focus()
    {
        var data = GetRootWindowData().context.Get<WindowData>();
        ImGui.SetWindowFocus($"{HandlerName}: {Handle.Filename}##{data.ID}");
    }

    public void Close()
    {
        var windowRoot = GetRootWindowData();
        EditorWindow.CurrentWindow?.CloseSubwindow(windowRoot);
    }

    protected FileEditor GetRootWindowData()
    {
        var rootFile = Handle;
        var windowRoot = this;
        while (rootFile != null && rootFile.HandleType == FileHandleType.Embedded) {
            var parent = context.FindValueInParentValues<WindowData>();
            var parentEditor = (parent?.Handler as FileEditor);
            if (parentEditor != null) {
                rootFile = parentEditor.Handle;
                windowRoot = parentEditor;
            }
        }

        return windowRoot;
    }

    public virtual void OnWindow()
    {
        var data = context.Get<WindowData>();
        var icon = AppIcons.GetIcon(this);
        var name = icon == '\0' ? $"{HandlerName}: {Handle.Filename}##{data.ID}" : $"{icon} {Handle.Filename}##{data.ID}";
        if (!ImguiHelpers.BeginWindow(data, name, windowFlags)) {
            EditorWindow.CurrentWindow?.CloseSubwindow(data);
            return;
        }
        Size = data.Size;
        Position = data.Position;

        ImGui.SameLine();
        OnIMGUI();
        ImGui.End();
    }

    public void OnIMGUI()
    {
        DrawFileControls(context.Get<WindowData>());
        ImGui.SameLine();
        ImGui.Text("|");
        ImGui.SameLine();
        ImGui.Button($"SRC"); // SILVER: Icon pending
        if (Handle.FileSource != null) {
            ImguiHelpers.TooltipColored($"File source: {Handle.HandleType} - {Handle.FileSource} ({Handle.NativePath})", Colors.Faded);
        } else if (!string.IsNullOrEmpty(Handle.NativePath)) {
            ImguiHelpers.TooltipColored($"File source: {Handle.HandleType} ({Handle.NativePath})", Colors.Faded);
        } else {
            ImguiHelpers.TooltipColored($"File source: {Handle.HandleType}", Colors.Faded);
        }
        if (ImGui.IsItemClicked()) {
            EditorWindow.CurrentWindow?.CopyToClipboard(Handle.NativePath ?? Handle.Filepath, "Path copied!");
        }
        DrawFileContents();
    }

    protected virtual void DrawFileControls(WindowData data)
    {
        if (failedToReadfile) return;

        if (CanSave) {
            if (ImGui.Button($"{AppIcons.SI_Save}")) {
                Save();
            }
            ImguiHelpers.Tooltip("Save");
            var workspace = data.Context.GetWorkspace()!;
            ImGui.SameLine();
            if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_SaveAs, new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary, Colors.IconSecondary, Colors.IconPrimary })) {
                PlatformUtils.ShowSaveFileDialog((path) => SaveTo(path, true), Handle.Filepath);
            }
            ImguiHelpers.Tooltip("Save As...");
            ImGui.SameLine();
            if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_SaveCopy, new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary })) {
                PlatformUtils.ShowSaveFileDialog((path) => SaveTo(path, false), Handle.Filepath);
            }
            ImguiHelpers.Tooltip("Save Copy to...");
            if (Handle.DiffHandler != null && ImguiHelpers.SameLine() && Handle.HandleType is not FileHandleType.Memory && ImGui.Button("See changes")) {
                var diff = Handle.DiffHandler.FindDiff(Handle);
                if (diff == null) {
                    EditorWindow.CurrentWindow?.Overlays.ShowTooltip("No changes detected compared to the base file", 3f);
                } else {
                    EditorWindow.CurrentWindow?.AddSubwindow(new JsonViewer(diff, Handle.Filepath, Handle));
                }
            }
            if (workspace.CurrentBundle != null) {
                if (!Handle.IsInBundle(workspace, workspace.CurrentBundle)) {
                    ImGui.SameLine();
                    if (ImGui.Button("Save to bundle")) {
                        ResourcePathPicker.SaveFileToBundle(workspace, Handle, (bundle, savePath, localPath, nativePath) => {
                            SaveTo(savePath, true, () => bundle.AddResource(localPath, nativePath, Handle.Format.format.IsDefaultReplacedBundleResource()));
                        });
                    }
                } else if (workspace.CurrentBundle.ResourceListing == null || !workspace.CurrentBundle.TryFindResourceListing(Handle.NativePath ?? "", out var resourceListing)) {
                    if (Handle.NativePath != null) {
                        if (ImGui.Button("Store in bundle")) {
                            workspace.CurrentBundle.ResourceListing ??= new();
                            var localPath = Path.GetRelativePath(workspace.BundleManager.GetBundleFolder(workspace.CurrentBundle), Handle.Filepath);
                            workspace.CurrentBundle.AddResource(localPath, Handle.NativePath, Handle.Format.format.IsDefaultReplacedBundleResource());
                        }
                        ImguiHelpers.Tooltip("File is located in the bundle folder but is not marked as part of the bundle. This will store it into the bundle json.");
                    }
                } else if (Handle.DiffHandler != null) {
                    ImGui.SameLine();
                    var replace = resourceListing.Replace;
                    if (ImGui.Checkbox("Replace File", ref replace)) {
                        resourceListing.Replace = replace;
                        Handle.Modified = true;
                    }
                    ImguiHelpers.Tooltip("When true, the file is treated as a full replacement and not partially patched.\nThis is required if you need to remove anything from the base file.\n\nWhen false, the file will be partially patched.\nThis is useful to allow multiple mods to affect a small part of a shared file, but may cause issues in specific cases like removed or reordered items.");
                }
            }
        }
        if (Handle.HandleType is FileHandleType.Disk or FileHandleType.Bundle && System.IO.File.Exists(Handle.Filepath)) {
            if (CanSave) ImGui.SameLine();
            if (ImGui.Button("Show in file explorer")) {
                FileSystemUtils.ShowFileInExplorer(Handle.Filepath);
            }
            ImguiHelpers.Tooltip("Filepath: " + Handle.Filepath);
        }
        if (HasUnsavedChanges && IsRevertable) {
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_Reset}")) {
                Handle.Revert(data.Context.GetWorkspace()!);
            }
            ImguiHelpers.Tooltip("Revert");
        }
    }

    protected void ShowFileJsonCopyPasteButtons<T>(JsonSerializerOptions? jsonOptions) where T : BaseFile
    {
        if (ImGui.Button("Copy as JSON")) {
            var data = Handle.GetFile<T>();
            var json = JsonSerializer.Serialize(data, jsonOptions ?? JsonConfig.jsonOptionsIncludeFields);
            EditorWindow.CurrentWindow?.CopyToClipboard(json, $"Copied file to JSON!");
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Paste from JSON")) {
            try {
                var wnd = EditorWindow.CurrentWindow;
                var data = wnd?.GetClipboard();
                if (string.IsNullOrEmpty(data)) return;

                var val = JsonSerializer.Deserialize<T>(data, jsonOptions ?? JsonConfig.jsonOptionsIncludeFields);
                if (val == null) {
                    Logger.Error($"Failed to deserialize {typeof(T).Name}.");
                    return;
                }

                var targetFile = Handle.GetFile<T>();
                var clone = targetFile.DeepCloneGeneric();
                UndoRedo.RecordCallback(context, () => DeepCloneUtil<T>.ReplaceFields(val, targetFile), () => DeepCloneUtil<T>.ReplaceFields(clone, targetFile));
                UndoRedo.AttachCallbackToLastAction(UndoRedo.CallbackType.Both, Reset, wnd);
            } catch (Exception e) {
                Logger.Error($"Failed to deserialize {typeof(T).Name}: " + e.Message);
            }

            ImGui.CloseCurrentPopup();
        }
    }

    private void Save()
    {
        Handle.Save(context.GetWorkspace()!);
    }

    private void SaveTo(string savePath, bool replaceFileHandle, Action? beforeReplaceAction = null, string? nativePath = null)
    {
        var workspace = context.GetWorkspace()!;
        if (!Handle.Save(workspace, savePath)) return;

        Logger.Info("File saved to " + savePath);
        if (replaceFileHandle) {
            beforeReplaceAction?.Invoke();
            DisconnectFile();
            if (Handle.References.Count == 0) {
                workspace.ResourceManager.CloseFile(Handle);
            }
            var newHandle = workspace.ResourceManager.GetFileHandle(savePath, nativePath);
            if (newHandle == null) {
                Logger.Error("Could not re-load newly saved file from path " + savePath);
            } else {
                Handle = newHandle;
                ConnectFile();
                OnFileChanged();
            }
        }
    }

    protected virtual void OnFileChanged()
    {
        context.ClearChildren();
        context.Changed = false;
    }

    protected virtual void OnFileReverted()
    {
        Reset();
        context.SetChangedNoPropagate(false);
    }

    protected virtual void Reset()
    {
        // default to doing a simple clear and not ClearChildren() so we don't dispose the file stream
        context.children.Clear();
        failedToReadfile = false;
    }

    protected virtual void OnFileSaved()
    {
        context.Save();
        if (this is IInspectorController inspector) {
            inspector.EmitSave();
        }
    }

    protected virtual void OnModifiedChanged(bool changed)
    {
        context.SetChangedNoPropagate(changed);
    }

    protected bool TryRead(BaseFile file, bool ignorePreviousFailure = false)
    {
        if (failedToReadfile && !ignorePreviousFailure) {
            ImGui.TextColored(Colors.Error, "Failed to read file");
            return false;
        }

        try {
            failedToReadfile = !file.Read();
            return !failedToReadfile;
        } catch (Exception e) {
            failedToReadfile = true;
            EditorWindow.CurrentWindow?.AddSubwindow(new ErrorModal("Read error", $"Failed to read file {Handle.Filepath}:\n\n{e.Message}", this));
            return false;
        }
    }

    protected abstract void DrawFileContents();

    public bool RequestClose()
    {
        if (Handle.Modified || HasUnsavedChanges) {
            var ownerWindow = EditorWindow.CurrentWindow!;
            ownerWindow.AddSubwindow(new SaveFileConfirmation(
                "Unsaved changes", "You have unsaved changes in this file, do you wish to save the file first?",
                [Handle],
                this,
                () => {
                    var file = Handle;
                    ownerWindow.CloseSubwindow(this);
                    ownerWindow.InvokeFromUIThread(() => {
                        if (file.References.Count == 0) {
                            ownerWindow.Workspace?.ResourceManager.CloseFile(file);
                        }
                    });
                })
            );
            return true;
        }
        return false;
    }

    protected void ConnectFile()
    {
        Handle.Reverted += OnFileReverted;
        Handle.Saved += OnFileSaved;
        Handle.ModifiedChanged += OnModifiedChanged;
        Handle.References.Add(this);
    }

    protected void DisconnectFile()
    {
        Handle.Reverted -= OnFileReverted;
        Handle.Saved -= OnFileSaved;
        Handle.ModifiedChanged -= OnModifiedChanged;
        Handle.References.Remove(this);
    }

    public void Dispose()
    {
        DisconnectFile();
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    public virtual bool HandleEvent(UIContext context, EditorUIEvent eventData)
    {
        if (eventData.type == UIContextEvent.Changed) {
            Handle.Modified = true;
        }
        if (eventData.type == UIContextEvent.Reverting) {
            if (eventData.origin.IsChildOf(context)) {
                return false;
            }

            if (eventData.origin == context) {
                var ws = context.GetWorkspace();
                if (ws != null) {
                    ws.ResourceManager.MarkFileResourceModified(Handle.Filepath, false);
                }
            }
        }

        if (eventData.type == UIContextEvent.Saved && eventData.origin == context) {
            return true;
        }

        return Handle.HandleType == FileHandleType.Embedded;
    }
}
