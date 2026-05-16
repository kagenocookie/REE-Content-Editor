using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.EntityResources;

[CustomFieldHandler(typeof(ResourceCustomField))]
public class EmbeddedResourceFileHandler(ResourceCustomField field) : IObjectUIHandler, IObjectUIInstantiator
{
    public static Func<CustomField, IObjectUIHandler> GetFactory() => (field) => new EmbeddedResourceFileHandler((ResourceCustomField)field);

    private FileHandle? file;

    public void OnIMGUI(UIContext context)
    {
        var entity = context.GetOwnerEntity();
        var workspace = context.GetWorkspace();
        if (entity == null || workspace == null) {
            ImGui.TextColored(Colors.Error, $"{field.label} field requires a valid item entity and workspace");
            return;
        }

        var path = workspace.Env.GetResourcePath(field.GetPath(entity)).ToString();
        if (string.IsNullOrEmpty(path)) {
            ImGui.Text(context.label + ": No resource");
            if (file != null) {
                context.ClearChildren();
                file = null;
            }
            return;
        }

        if (entity == null || workspace == null) {
            ImGui.TextColored(Colors.Error, $"{field.label} field requires a valid item entity and workspace");
            return;
        }
        if (file == null || file.ResourcePath == null || !file.ResourcePath.Equals(path, StringComparison.InvariantCultureIgnoreCase)) {
            if (file != null) {
                context.ClearChildren();
                file = null;
            }

            if (!workspace.ResourceManager.TryResolveGameFile(path, out file)) {
                ImGui.TextColored(Colors.Warning, "Could not resolve file " + path);
                return;
            }
        }

        if (context.children.Count == 0) {
            var editor = WindowHandlerFactory.CreateFileResourceHandler(workspace, file);
            if (editor == null) {
                context.AddChild(context.label, null, ReadOnlyLabelHandler.Instance);
            } else {
                WindowData.CreateEmbeddedWindow(context, context.GetWindow()!, editor, context.label);
            }
        }

        ImGui.Text(field.label);
        ImGui.SameLine();
        if (ImGui.Button("Open in new window")) {
            EditorWindow.CurrentWindow?.AddFileEditor(file);
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
