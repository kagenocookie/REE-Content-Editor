using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;

namespace ContentEditor.App;

public class FilePreviewWindow : IWindowHandler, IObjectUIHandler, IDisposable
{
    public string HandlerName => "File Preview";
    public bool HasUnsavedChanges => false;

    private UIContext context = null!;
    private WindowData data = null!;
    private ContentWorkspace workspace = null!;

    private FileHandle? file;
    private IWindowHandler? innerWindow;
    private UIContext? fileContext;
    private WindowData? fileWindow;

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
        workspace = context.GetWorkspace() ?? throw new Exception("Workspace not found");
    }

    public void OnIMGUI()
    {
        if (innerWindow == null) {
            if (file == null) {
                ImGui.Text("No file open for preview");
            } else {
                ImGui.TextColored(Colors.Note, "File cannot be previewed");
            }
            return;
        }

        // SILVER: Force this button to the right side of the tab because it clips with some editor UIs.
        if (file != null) {
            float buttonW = ImGui.CalcTextSize($"{AppIcons.SI_WindowOpenNew}").X + ImGui.GetStyle().FramePadding.X * 2;

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - buttonW);
            if (ImGui.Button($"{AppIcons.SI_WindowOpenNew}")) {
                EditorWindow.CurrentWindow?.AddFileEditor(file);
            }
            ImguiHelpers.Tooltip("Open in New Window");
        }
        ImGui.Separator();
        ImGui.BeginGroup();
        innerWindow.OnIMGUI();
        ImGui.EndGroup();
    }
    private bool shouldDisposeFile;

    public void SetFile(string filepath)
    {
        if (workspace.ResourceManager.TryResolveFile(filepath, out var file)) {
            SetFile(file);
        }
    }

    public void SetFile(Stream stream, string filepath, string nameSpace)
    {
        var file = workspace.ResourceManager.CreateFileHandle(nameSpace + ":// " + filepath, null, stream, false);
        SetFile(file);
        shouldDisposeFile = true;
    }

    public void SetFile(FileHandle file)
    {
        shouldDisposeFile = false;
        this.file = file;
        (innerWindow as IDisposable)?.Dispose();
        innerWindow?.OnClosed();
        innerWindow = WindowHandlerFactory.CreateFileResourceHandler(workspace, file);
        fileContext?.ClearChildren();
        if (innerWindow != null) {
            if (innerWindow is IObjectUIHandler) {
                fileWindow = WindowData.CreateEmbeddedWindow(context, data.ParentWindow, innerWindow, "File");
                fileContext = fileWindow.Context;
            } else {
                fileWindow = new WindowData() {
                    ParentWindow = data.ParentWindow,
                };
                fileContext = context.AddChild("File", fileWindow);
                fileWindow.Context = fileContext;
                innerWindow.Init(fileContext);
            }
        }
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    public bool RequestClose()
    {
        if (innerWindow != null && innerWindow.RequestClose()) {
            if (!EditorWindow.CurrentWindow!.HasSubwindow<SaveFileConfirmation>(out _)) {
                EditorWindow.CurrentWindow!.AddSubwindow(new SaveFileConfirmation(
                    "Unsaved changes",
                    $"Some files have unsaved changes.\nAre you sure you wish to close the window?",
                    workspace.ResourceManager.GetModifiedResourceFiles(),
                    data.ParentWindow,
                    () => workspace.ResourceManager.CloseAllFiles()
                ));
            }
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        (innerWindow as IDisposable)?.Dispose();
        if (shouldDisposeFile) {
            file?.Dispose();
        }
    }

    void IObjectUIHandler.OnIMGUI(UIContext context) => OnIMGUI();
}
