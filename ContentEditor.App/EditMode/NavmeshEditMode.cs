using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Tooling.Navmesh;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Aimp;
using ReeLib.Il2cpp;

namespace ContentEditor.App;

[Flags]
public enum NavmeshContentType : int
{

    Points = 1 << 0,
    Triangles = 1 << 1,
    Polygons = 1 << 2,
    Boundaries = 1 << 3,
    AABBs = 1 << 4,
    Walls = 1 << 5,

    MainLinks = 1 << 8,
    SecondaryLinks = 1 << 9,
    All = Points|Triangles|Polygons|Boundaries|AABBs|Walls|MainLinks|SecondaryLinks,
}

public enum SceneMode
{
    Selection,
    SetAttribute,
    RemoveAttribute,
}

public class NavmeshEditMode : EditModeHandler
{
    public override string DisplayName => "Navigation";

    private UIContext? filePicker;
    private string filepath = "";
    protected UIContext? context;
    public NavmeshContentType displayedContentTypes = NavmeshContentType.Points|NavmeshContentType.Triangles|NavmeshContentType.Boundaries|NavmeshContentType.Walls|NavmeshContentType.AABBs|NavmeshContentType.MainLinks|NavmeshContentType.SecondaryLinks;

    private string? loadedFilepath;
    private FileHandle? loadedFile;

    public SceneMode Mode { get; private set; }
    public bool AttributeFill { get; private set; }
    public ulong SelectedAttribute { get; private set; }

    private bool firstTimeOnTarget;

    protected override void OnTargetChanged(Component? previous)
    {
        if (previous is AIMapComponentBase map) {
            map.visibleContentTypes = 0;
        }
        firstTimeOnTarget = true;
    }

    public override void DrawMainUI()
    {
        EnsureUIContext();

        AppImguiHelpers.WikiLinkButton("https://github.com/kagenocookie/REE-Content-Editor/wiki/AI-Navigation");
        context!.children[0].ShowUI();

        if (Target is AIMapComponentBase map) {
            var storedSuggestions = map.StoredResources.ToArray();
            if (storedSuggestions.Length > 0) {
                if (firstTimeOnTarget) {
                    if (storedSuggestions.Length == 1 && string.IsNullOrEmpty(filepath)) {
                        filepath = storedSuggestions[0];
                    }
                    firstTimeOnTarget = false;
                }
                if (ImguiHelpers.ValueCombo(Lang.EditMode.StoredFiles.String, storedSuggestions, storedSuggestions, ref filepath)) {
                    AppConfig.Settings.RecentNavmeshes.AddRecent(Scene.Workspace.Game, filepath);
                    filePicker?.ResetState();
                }
            }
            map.visibleContentTypes = displayedContentTypes;
        }

        var settings = AppConfig.Settings;
        if (settings.RecentNavmeshes.Count > 0) {
            if (AppImguiHelpers.ShowRecentFiles(settings.RecentNavmeshes, Scene.Workspace.Game, ref filepath)) {
                filePicker?.ResetState();
            }
        }
        context!.children[1].ShowUI();
        if (loadedFilepath != filepath) {
            if (loadedFile != null) {
                loadedFile.ModifiedChanged -= OnFileChanged;
                loadedFile = null;
            }

            if (Scene.Workspace.ResourceManager.TryResolveGameFile(filepath, out var file)) {
                loadedFilepath = filepath;
                loadedFile = file;
                loadedFile.ModifiedChanged += OnFileChanged;
            } else if (!string.IsNullOrEmpty(filepath)) {
                ImGui.TextColored(Colors.Warning, "File not found");
            }
        }

        if (loadedFile != null) {
            if (ImGui.Button(Lang.EditMode.Button_ResetGeometry)) {
                (Target as AIMapComponentBase)?.ResetPreviewGeometry();
            }
            ImGui.SameLine();
            if (ImGui.Button(Lang.EditMode.Button_BakeNavmesh)) {
                EditorWindow.CurrentWindow?.AddSubwindow(new NavmeshBakerUI(Scene, loadedFile, context));
            }
        }
    }

    public override void DrawToolbarUI()
    {
        if (loadedFile != null && loadedFile.GetFile<AimpFile>().mainContent?.contents.FirstOrDefault() is ContentGroupTriangle) {
            if (ImGui.BeginMenu(Lang.EditMode.Navmesh_PaintingToolbar)) {
                EnsureUIContext();
                context!.ShowChildrenUI(2);
                ImGui.EndMenu();
            }
        }
    }

    public static EnumDescriptor<ulong> CreateAIMapLayerEnum(AimpFile? file)
    {
        var desc = new EnumDescriptor<ulong>();
        int i = 0;
        if (file?.layers == null) {
            for (i = 0; i < 64; i++) {
                desc.AddValue<ulong>(1ul << i, i.ToString());
            }
            return desc;
        }
        foreach (var layer in file.layers) {
            desc.AddValue<ulong>(1ul << i++, layer.name ?? layer.nameHash.ToString());
        }
        return desc;
    }

    private void EnsureUIContext()
    {
        if (context == null) {
            context = UIContext.CreateRootContext("Navmesh", this);
            filePicker = context.AddChild<NavmeshEditMode, string>(
                "File",
                this,
                new ResourcePathPicker(Scene.Workspace, KnownFileFormats.AIMap) { Flags = ResourcePathPicker.PathPickerFlags.EditorOnly },
                (v) => v!.filepath,
                (v, p) => v.filepath = p ?? "");
            context.AddChild<NavmeshEditMode, NavmeshContentType>(
                "Content Types",
                this,
                new CsharpFlagsEnumFieldHandler<NavmeshContentType, int>() { HideNumberInput = true },
                getter: o => o!.displayedContentTypes,
                setter: (o, v) => o.displayedContentTypes = v,
                UIOptions.DisableUndoRedo
            );
            context.AddChild(
                Lang.EditMode.Navmesh_SceneMode,
                this,
                new CsharpEnumRadioHandler(typeof(SceneMode)),
                c => c!.Mode,
                (c, v) => c.Mode = v,
                UIOptions.DisableUndoRedo
            );
            context.AddChild(
                Lang.EditMode.Navmesh_AttrFill,
                this,
                new ConditionalUIHandler(BoolFieldHandler.Instance, c => ((NavmeshEditMode)c.target!).Mode != SceneMode.Selection),
                c => c!.AttributeFill,
                (c, v) => c.AttributeFill = v,
                UIOptions.DisableUndoRedo
            );
            context.AddChild(
                Lang.EditMode.Navmesh_Attribute,
                this,
                new FlagsEnumFieldHandler(CreateAIMapLayerEnum((Target as AIMapComponentBase)?.DisplayedFile)),
                c => c!.SelectedAttribute,
                (c, v) => {
                    c.SelectedAttribute = v;
                    (c.Target as AIMapComponentBase)?.AttributesFilter = (ulong)v;
                },
                UIOptions.DisableUndoRedo
            );
        }
    }

    private void RefreshEnums()
    {
        EnsureUIContext();
        context!.children[^1].uiHandler = new FlagsEnumFieldHandler(CreateAIMapLayerEnum((Target as AIMapComponentBase)?.DisplayedFile));
    }

    private void OnFileChanged(bool changed)
    {
        (Target as AIMapComponentBase)?.ResetPreviewGeometry();
        RefreshEnums();
    }

    public override void OnIMGUI()
    {
        if (!(Target is AIMapComponentBase comp)) return;

        if (loadedFile == null) {
            comp.SetOverrideFile(null);
        } else {
            var nvm = loadedFile.GetFile<AimpFile>();
            if (comp.DisplayedFile != nvm) {
                comp.SetOverrideFile(nvm);
                AppConfig.Settings.RecentNavmeshes.AddRecent(Scene.Workspace.Game, filepath);
                RefreshEnums();
            }
            comp.visibleContentTypes = displayedContentTypes;
        }
    }
}
