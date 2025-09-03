using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
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

        if (file != null && ImGui.Button("Open in new window")) {
            // no point in showing any file info here, every editor should include it already
            EditorWindow.CurrentWindow?.AddFileEditor(file);
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
            shouldDisposeFile = true;
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
        return false;
    }

    public void Dispose()
    {
        if (shouldDisposeFile) {
            file?.Dispose();
        }
    }

    void IObjectUIHandler.OnIMGUI(UIContext context) => OnIMGUI();
}