using ContentEditor.App.ImguiHandling;
using ContentEditor.App.ImguiHandling.Chain;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

public class GpucEditMode : EditModeHandler
{
    public override string DisplayName => $"{AppIcons.SI_FileType_GPUC} GPU Cloth";

    public GpucEditor? PrimaryEditor { get; private set; }

    private UIContext? filePicker;
    private string filePath = "";
    protected UIContext? context;

    protected override void OnTargetChanged(Component? previous)
    {
        if (previous is GpuCloth comp) {
            comp.activeGroup = null;
            comp.SetOverrideFile(null);
        }
    }

    public void OpenEditor(string filepath)
    {
        if (!(Target is GpuCloth component)) {
            return;
        }
        if (Scene.Workspace.ResourceManager.TryGetOrLoadFile(filepath, out var file)) {
            OpenEditor(file);
        }
    }

    public void OpenEditor(FileHandle file)
    {
        if (!(Target is GpuCloth component)) {
            return;
        }
        if (file.Format.format != KnownFileFormats.GpuCloth) {
            return;
        }

        EditorWindow.CurrentWindow!.AddSubwindow(PrimaryEditor = new GpucEditor(Scene.Workspace, file, component));
    }

    public override void DrawMainUI()
    {
        if (context == null) {
            context = UIContext.CreateRootContext("ChainEdit", this);
        }
        // AppImguiHelpers.WikiLinkButton("https://github.com/kagenocookie/REE-Content-Editor/wiki/GPU-Cloth");
        if (filePicker == null) {
            filePicker = context.AddChild<GpucEditMode, string>(
                "Edited Cloth File",
                this,
                new ResourcePathPicker(Scene.Workspace, KnownFileFormats.GpuCloth) { Flags = ResourcePathPicker.PathPickerFlags.EditorOnly },
                (v) => v!.filePath,
                (v, p) => v.filePath = p ?? "");
        }
        filePicker.ShowUI();

        if (Target is GpuCloth component && !string.IsNullOrEmpty(component.Resource)) {
            if (ImGui.Button("Use Stored Chain File") || string.IsNullOrEmpty(filePath)) {
                AppConfig.Settings.RecentCloth.AddRecent(Scene.Workspace.Game, filePath);
                filePath = component.Resource;
            }
        }

        var settings = AppConfig.Settings;
        if (settings.RecentCloth.Count > 0) {
            var options = settings.RecentCloth.ToArray();
            if (AppImguiHelpers.ShowRecentFiles(settings.RecentCloth, Scene.Workspace.Game, ref filePath)) {
                filePicker?.ResetState();
            }
        }

        if (Scene.Workspace.ResourceManager.TryGetOrLoadFile(filePath, out var file)) {
            if (ImGui.Button("Open File")) {
                OpenEditor(file);
            }
        } else if (!string.IsNullOrEmpty(filePath)) {
            ImGui.TextColored(Colors.Warning, "File not found");
        }
    }

    public override void OnIMGUI()
    {
        if (!(Target is GpuCloth comp)) return;

        if (Scene.Workspace.ResourceManager.TryGetOrLoadFile(filePath, out var file)) {
            if (comp.CurrentFile != file.GetFile<GpucFile>()) {
                comp.SetOverrideFile(file.GetFile<GpucFile>());
                AppConfig.Settings.RecentCloth.AddRecent(Scene.Workspace.Game, filePath);
            }
        } else {
            comp.ClearOverrideFile();
        }
    }
}
