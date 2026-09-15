using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;

namespace ContentEditor.App.ImguiHandling;

public class EmbeddedFileHandle : IObjectUIHandler
{
    private FileHandle? file;
    private bool isError;
    private string? lastPath;

    public void OnIMGUI(UIContext context)
    {
        var path = context.Get<string>();
        var workspace = context.GetWorkspace();
        if (string.IsNullOrEmpty(path) || workspace == null) {
            ImGui.TextColored(Colors.Error, $"{context.label} is empty or missing a valid workspace");
            if (file != null) {
                context.ClearChildren();
                file = null;
            }
            return;
        }

        if (!path.Equals(lastPath, StringComparison.InvariantCultureIgnoreCase)) {
            isError = false;
        }

        if (isError) {
            ImGui.TextColored(Colors.Warning, "Could not resolve file " + path);
            return;
        }

        if (file == null || file.ResourcePath == null || !path.Equals(lastPath, StringComparison.InvariantCultureIgnoreCase)) {
            lastPath = path;
            if (file != null) {
                context.ClearChildren();
                file = null;
            }

            if (!workspace.ResourceManager.TryResolveGameFile(path, out file)) {
                isError = true;
                return;
            }
        }

        if (context.children.Count == 0) {
            var editor = WindowHandlerFactory.CreateFileResourceHandler(workspace, file);
            if (editor == null) {
                context.AddChild(context.label, null, ReadOnlyLabelHandler.Instance);
            } else {
                WindowData.CreateEmbeddedWindow(context, context.GetWindow()!, editor, context._label);
            }
        }

        if (context.label.UTF8.Length > 1) {
            ImGui.Text(context.label);
        }
        if (context.children[0].uiHandler is FixedLabelHandler) {
            context.ShowChildrenUI();
            return;
        }
        ImguiHelpers.BeginRect();
        ImGui.Spacing();
        ImGui.Indent(4);
        ImGui.BeginChild(context.label);
        context.ShowChildrenUI();
        ImGui.EndChild();
        ImGui.Unindent(4);
        ImGui.Spacing();
        ImguiHelpers.EndRect();
        ImGui.Spacing();
    }
}
