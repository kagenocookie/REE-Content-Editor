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
    public bool SaveWithNativePath { get; init; }

    public ResourcePathPicker()
    {
        FileFormats = [];
    }

    public ResourcePathPicker(ContentWorkspace? ws, params KnownFileFormats[] allowedFormats)
    {
        FileFormats = allowedFormats;
        if (ws != null) {
            FileExtensionFilter = string.Join(
                ",",
                allowedFormats.SelectMany(format => ws.Env
                    .GetFileExtensionsForFormat(format)
                    .Select(ext => $"{format} .{ext}|*.{ext}*")));
        }
    }

    public ResourcePathPicker(ContentWorkspace? ws, RszField field)
        : this(ws, [TypeCache.GetResourceFormat(field.original_type)])
    {
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

        // validate the filepath
        if (SaveWithNativePath) {
            // native path
            if (context.state != null && (Path.IsPathFullyQualified(context.state) || !context.state.StartsWith("natives/") || PathUtils.ParseFileFormat(context.state).version == -1)) {
                ImGui.TextColored(Colors.Warning, "The given file path may not resolve properly ingame.\nEnsure it's a native path (including the natives/stm/ part and with file extension version)");
            }
        } else {
            // internal path
            if (context.state != null && (Path.IsPathFullyQualified(context.state) || PathUtils.ParseFileFormat(context.state).version != -1 || context.state.Contains("natives/"))) {
                ImGui.TextColored(Colors.Warning, "The given file path may not resolve properly ingame.\nEnsure it's an internal path (without the natives/stm/ part and no file extension version)");
            }
        }

        if (FileFormats.Length != 0 && !string.IsNullOrEmpty(context.state)) {
            var parsed = PathUtils.ParseFileFormat(context.state);
            if (!FileFormats.Contains(parsed.format)) {
                ImGui.TextColored(Colors.Warning, "The file may be an incorrect type. Expected file types: " + FileExtensionFilter);
            }
        }

        // TODO expandable resource preview
    }

    private void ApplyPathChange(UIContext context, string newPath)
    {
        newPath = PathUtils.GetNativeFromFullFilepath(newPath) ?? newPath;
        if (SaveWithNativePath) {
            newPath = PathUtils.GetInternalFromNativePath(newPath);
        } else {
            newPath = PathUtils.RemoveNativesFolder(newPath);
            var format = PathUtils.ParseFileFormat(newPath);
            if (format.version != -1) {
                newPath = PathUtils.GetFilepathWithoutSuffixes(newPath).ToString();
            }
        }
        context.Set(newPath);
    }
}
