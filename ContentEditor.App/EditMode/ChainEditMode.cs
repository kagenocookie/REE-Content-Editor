using ContentEditor.App.ImguiHandling;
using ContentEditor.App.ImguiHandling.Chain;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

public class ChainEditMode : EditModeHandler
{
    public override string DisplayName => $"{AppIcons.SI_FileType_CHAIN} Chain";

    public ChainEditorBase? PrimaryEditor { get; private set; }

    private UIContext? filePicker;
    private string filePath = "";
    protected UIContext? context;

    protected override void OnTargetChanged(Component? previous)
    {
        if (previous is RequestSetColliderComponent prst) {
            prst.activeGroup = null;
            prst.SetOverrideFile(null);
        }
    }

    public void OpenEditor(string filepath)
    {
        if (!(Target is RequestSetColliderComponent component)) {
            return;
        }
        if (Scene.Workspace.ResourceManager.TryGetOrLoadFile(filepath, out var file)) {
            OpenEditor(file);
        }
    }

    public void OpenEditor(FileHandle file)
    {
        if (!(Target is Chain component)) {
            return;
        }
        if (file.Format.format == KnownFileFormats.Chain) {
            EditorWindow.CurrentWindow!.AddSubwindow(PrimaryEditor = new ChainEditor(Scene.Workspace, file));
        } else if (file.Format.format == KnownFileFormats.Chain2) {
            EditorWindow.CurrentWindow!.AddSubwindow(PrimaryEditor = new Chain2Editor(Scene.Workspace, file));
        } else {
            return;
        }
    }

    public override void DrawMainUI()
    {
        if (context == null) {
            context = UIContext.CreateRootContext("ChainEdit", this);
        }
        if (filePicker == null) {
            filePicker = context.AddChild<ChainEditMode, string>(
                "Edited Chain File",
                this,
                new ResourcePathPicker(Scene.Workspace, KnownFileFormats.Chain, KnownFileFormats.Chain2) { Flags = ResourcePathPicker.PathPickerFlags.EditorOnly },
                (v) => v!.filePath,
                (v, p) => v.filePath = p ?? "");
        }
        filePicker.ShowUI();

        if (Target is Chain component && !string.IsNullOrEmpty(component.ChainAsset)) {
            if (ImGui.Button("Use Stored Chain File") || string.IsNullOrEmpty(filePath)) {
                AppConfig.Settings.RecentChains.AddRecent(Scene.Workspace.Game, filePath);
                filePath = component.ChainAsset;
            }
        }

        var settings = AppConfig.Settings;
        if (settings.RecentChains.Count > 0) {
            var options = settings.RecentChains.ToArray();
            if (AppImguiHelpers.ShowRecentFiles(settings.RecentChains, Scene.Workspace.Game, ref filePath)) {
                filePicker?.ResetState();
            }
        }

        if (Scene.Workspace.ResourceManager.TryGetOrLoadFile(filePath, out var file)) {
            if (ImGui.Button("Open Editor")) {
                OpenEditor(file);
            }
        } else if (!string.IsNullOrEmpty(filePath)) {
            ImGui.TextColored(Colors.Warning, "File not found");
        }
    }

    public override void OnIMGUI()
    {
        if (!(Target is Chain comp)) return;

        if (Scene.Workspace.ResourceManager.TryGetOrLoadFile(filePath, out var file)) {
            if (file.Format.format == KnownFileFormats.Chain) {
                if (comp.CurrentFile != file.GetFile<ChainFile>()) {
                    comp.SetOverrideFile(file.GetFile<ChainFile>());
                    AppConfig.Settings.RecentChains.AddRecent(Scene.Workspace.Game, filePath);
                }
            } else if (file.Format.format == KnownFileFormats.Chain2) {
                if (comp.CurrentFile != file.GetFile<Chain2File>()) {
                    comp.SetOverrideFile(file.GetFile<Chain2File>());
                    AppConfig.Settings.RecentChains.AddRecent(Scene.Workspace.Game, filePath);
                }
            }
        } else {
            comp.ClearOverrideFile();
        }
    }
}
