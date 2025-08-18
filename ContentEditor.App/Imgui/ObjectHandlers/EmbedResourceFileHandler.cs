using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ContentPatcher.DD2;
using ImGuiNET;
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
        if (entity == null) {
            ImGui.TextColored(Colors.Error, context.label + ": Entity not found");
            return;
        }
        var path = field.GetPath(entity);
        if (string.IsNullOrEmpty(path)) {
            ImGui.Text(context.label + ": No resource");
            if (file != null) {
                context.ClearChildren();
                file = null;
            }
            return;
        }

        var workspace = context.GetWorkspace();
        if (entity == null || workspace == null) {
            ImGui.TextColored(Colors.Error, $"{field.label} field requires a valid item entity and workspace");
            return;
        }
        if (file == null || file.NativePath == null || !PathUtils.GetInternalFromNativePath(file.NativePath).Equals(path, StringComparison.InvariantCultureIgnoreCase)) {
            if (file != null) {
                context.ClearChildren();
                file = null;
            }
            if (path.IsNativePath()) {
                ImGui.TextColored(Colors.Error, "The path is specified as a native path - this is not supported!\nPath should not be prefixed with natives/ and containly only the base file extension without versions.");
                return;
            }
            if (!workspace.ResourceManager.TryResolveFile(path, out file)) {
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
