using System.Numerics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public abstract class FileEditor : IWindowHandler, IRectWindow, IDisposable, IFocusableFileHandleReferenceHolder, IUIContextEventHandler
{
    public virtual bool HasUnsavedChanges => context.Changed;

    public virtual string HandlerName { get; } = "File";
    public FileHandle Handle { get; private set; }

    public bool IsEmbedded { get; set; }

    public Vector2 Size { get; private set; }
    public Vector2 Position { get; private set; }

    protected bool failedToReadfile = false;
    protected ImGuiWindowFlags windowFlags;
    protected UIContext context = null!;

    protected virtual bool IsRevertable => true;

    public bool CanClose => Handle.HandleType != FileHandleType.Embedded && context.parent == context.root;

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
            var parent = context.FindClassValueInParentValues<WindowData>();
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
        if (!ImguiHelpers.BeginWindow(data, $"{HandlerName}: {Handle.Filename}##{data.ID}", windowFlags)) {
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
        DrawFileContents();
    }

    protected void DrawFileControls(WindowData data)
    {
        if (failedToReadfile) return;

        if (ImGui.Button("Save")) {
            Save();
        }
        var workspace = data.Context.GetWorkspace()!;
        ImGui.SameLine();
        if (ImGui.Button("Save as ...")) {
            PlatformUtils.ShowSaveFileDialog((path) => SaveTo(path, true), Handle.Filepath);
        }
        ImGui.SameLine();
        if (ImGui.Button("Save copy to ...")) {
            PlatformUtils.ShowSaveFileDialog((path) => SaveTo(path, false), Handle.Filepath);
        }
        if (workspace.CurrentBundle != null && !Handle.IsInBundle(workspace, workspace.CurrentBundle)) {
            ImGui.SameLine();
            if (ImGui.Button("Save to bundle")) {
                var bundle = workspace.CurrentBundle;
                var fn = Handle.Filename.ToString();
                var bundleFolder = workspace.BundleManager.GetBundleFolder(bundle);
                var nativeFilepath = Handle.NativePath ?? PathUtils.GetNativeFromFullFilepath(Handle.Filepath) ?? fn;
                var localFilepath = fn;
                var savePath = Path.Combine(bundleFolder, localFilepath);
                if (File.Exists(savePath)) {
                    localFilepath = PathUtils.RemoveNativesFolder(nativeFilepath);
                    savePath = Path.Combine(bundleFolder, localFilepath);
                }
                SaveTo(savePath, true, () => bundle.AddResource(localFilepath, nativeFilepath));
                workspace.BundleManager.SaveBundle(bundle);
            }
        }
        if (Handle.HandleType is FileHandleType.Disk or FileHandleType.Bundle && System.IO.File.Exists(Handle.Filepath)) {
            ImGui.SameLine();
            if (ImGui.Button("Show in file explorer")) {
                FileSystemUtils.ShowFileInExplorer(Handle.Filepath);
            }
            if (ImGui.IsItemHovered()) {
                ImGui.SetItemTooltip("Filepath: " + Handle.Filepath);
            }
        }
        if (HasUnsavedChanges && IsRevertable) {
            ImGui.SameLine();
            if (ImGui.Button("Revert")) {
                Handle.Revert(data.Context.GetWorkspace()!);
            }
        }

        if (Handle.FileSource != null) {
            ImGui.TextColored(Colors.Faded, "File source: " + Handle.HandleType + " - " + Handle.FileSource);
        } else {
            ImGui.TextColored(Colors.Faded, "File source: " + Handle.HandleType);
        }
    }

    private void Save()
    {
        Handle.Save(context.GetWorkspace()!);
    }

    private void SaveTo(string savePath, bool replaceFileHandle, Action? beforeReplaceAction = null)
    {
        var workspace = context.GetWorkspace()!;
        if (!Handle.Save(workspace, savePath)) return;

        Logger.Info("File saved to " + savePath);
        if (replaceFileHandle) {
            beforeReplaceAction?.Invoke();
            DisconnectFile();
            Handle = workspace.ResourceManager.GetFileHandle(savePath);
            ConnectFile();
            OnFileChanged();
        }
    }

    protected virtual void OnFileChanged() { }

    protected virtual void OnFileReverted()
    {
        context.SetChangedNoPropagate(false);
    }

    protected virtual void OnFileSaved()
    {
        context.Save();
    }

    private void OnModifiedChanged(bool changed)
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
            EditorWindow.CurrentWindow!.AddSubwindow(new ConfirmationDialog(
                "Unsaved changes", "You have unsaved changes in this file, do you wish to save the file first?",
                this,
                () => Save(), () => {})
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

        return false;
    }
}