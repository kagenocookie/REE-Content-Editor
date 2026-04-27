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

    private bool Chain2Available => Scene.Workspace.Env.GetFileExtensionsForFormat(KnownFileFormats.Chain2).Any();

    private UIContext? filePicker;
    private string filePath = "";
    private string clspPath = "";
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
            EditorWindow.CurrentWindow!.AddSubwindow(PrimaryEditor = new ChainEditor(Scene.Workspace, file, component));
        } else if (file.Format.format == KnownFileFormats.Chain2) {
            EditorWindow.CurrentWindow!.AddSubwindow(PrimaryEditor = new Chain2Editor(Scene.Workspace, file, component));
        } else if (file.Format.format == KnownFileFormats.CollisionShapePreset) {
            EditorWindow.CurrentWindow!.AddSubwindow(new ClspEditor(Scene.Workspace, file, component));
        } else {
            return;
        }
    }

    public override void DrawMainUI()
    {
        if (context == null) {
            context = UIContext.CreateRootContext("ChainEdit", this);
        }
        AppImguiHelpers.WikiLinkButton("https://github.com/kagenocookie/REE-Content-Editor/wiki/Chains");
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

        if (Scene.Workspace.Env.ComponentAvailable<CollisionShapePreset>()) {
            var clspPicker = context.GetChild("Clsp File") ?? context.AddChild(
                "Clsp File",
                this,
                new ResourcePathPicker(Scene.Workspace, KnownFileFormats.CollisionShapePreset) { Flags = ResourcePathPicker.PathPickerFlags.EditorOnly },
                (v) => v!.clspPath,
                (v, p) => v.clspPath = p ?? "");
            clspPicker.ShowUI();
            if (settings.RecentClsp.Count > 0) {
                var options = settings.RecentClsp.ToArray();
                if (AppImguiHelpers.ShowRecentFiles(settings.RecentClsp, Scene.Workspace.Game, ref clspPath, "Recent CLSP")) {
                    clspPicker?.ResetState();
                }
            }
        }

        if (ImGui.Button($"{AppIcons.SI_FileNew} New chain")) {
            filePath = Scene.Workspace.ResourceManager.CreateNewFile(KnownFileFormats.Chain)!.Filepath;
        }
        if (Chain2Available) {
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_FileNew} New chain2")) {
                filePath = Scene.Workspace.ResourceManager.CreateNewFile(KnownFileFormats.Chain2)!.Filepath;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_FileNew} New clsp")) {
            filePath = Scene.Workspace.ResourceManager.CreateNewFile(KnownFileFormats.CollisionShapePreset)!.Filepath;
        }

        if (Scene.Workspace.ResourceManager.TryGetOrLoadFile(filePath, out var file)) {
            if (ImGui.Button("Open Chain")) {
                OpenEditor(file);
            }
        } else if (!string.IsNullOrEmpty(filePath)) {
            ImGui.TextColored(Colors.Warning, "Chain file not found");
        }

        if (Scene.Workspace.ResourceManager.TryGetOrLoadFile(clspPath, out var file2)) {
            if (file != null) ImGui.SameLine();
            if (ImGui.Button("Open CLSP")) {
                OpenEditor(file2);
            }
        } else if (!string.IsNullOrEmpty(clspPath)) {
            ImGui.TextColored(Colors.Warning, "Chain file not found");
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

        if (Scene.Workspace.Env.ComponentAvailable<CollisionShapePreset>()) {
            if (!string.IsNullOrEmpty(clspPath)) {
                Target.GameObject.GetOrAddComponent<CollisionShapePreset>();
            }
            var clspComp = Target.GameObject.GetComponent<CollisionShapePreset>();

            if (!string.IsNullOrEmpty(clspPath)) {
                if (Scene.Workspace.ResourceManager.TryGetOrLoadFile(clspPath, out file)) {
                    if (clspComp?.CurrentOverrideFile != file.GetFile<ClspFile>()) {
                        AppConfig.Settings.RecentClsp.AddRecent(Scene.Workspace.Game, clspPath);
                    }
                    clspComp?.SetOverrideFile(file.GetFile<ClspFile>());
                } else {
                    ImGui.TextColored(Colors.Warning, "CLSP file not found");
                }
            } else {
                clspComp?.ClearOverrideFile();
            }
        }
    }
}
