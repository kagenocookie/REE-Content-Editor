using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Il2cpp;

namespace ContentEditor.App.ImguiHandling;

public class ResourcePathPicker : IObjectUIHandler
{
    public string? FileExtensionFilter { get; }
    public KnownFileFormats FileFormat { get; }

    public ResourcePathPicker(string? fileExtensionFilter = null)
    {
        FileExtensionFilter = fileExtensionFilter;
    }

    public ResourcePathPicker(ContentWorkspace? ws, RszField field)
    {
        FileFormat = TypeCache.GetResourceFormat(field.original_type);
        if (ws != null) {
            FileExtensionFilter = string.Join(",", ws.Env.GetFileExtensionsForFormat(FileFormat).Select(fm => $"{fm} {FileFormat}|*.{fm}*"));
        }
    }

    public void OnIMGUI(UIContext context)
    {
        var currentPath = context.Get<string>();
        context.state ??= currentPath;
        if (AppImguiHelpers.InputFilepath(context.label, ref context.state, FileExtensionFilter)) {
            context.Changed = true;
        }

        if (context.state != currentPath) {
            if (ImGui.Button("Update path")) {
                ApplyPathChange(context, context.state);
                context.state = null;
            }
            if (ImguiHelpers.SameLine() && ImGui.Button("Cancel change")) {
                context.Changed = false;
                context.state = null;
            }
        }

        // TODO expandable resource preview
        if (context.state != null && (Path.IsPathFullyQualified(context.state) || PathUtils.ParseFileFormat(context.state).version != -1 || context.state.StartsWith("natives/"))) {
            ImGui.TextColored(Colors.Warning, "The given file path may not resolve properly ingame.\nEnsure it's a relative path without the natives/stm/ part and no file version extension");
        }
    }

    private void ApplyPathChange(UIContext context, string toPath)
    {
        context.Set(toPath);
        // TODO resolve relative path properly
        //
    }
}
