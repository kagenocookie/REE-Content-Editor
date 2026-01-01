using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Tooling.Navmesh;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

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

public class NavmeshEditor : EditModeHandler
{
    public override string DisplayName => "Navmesh";

    private UIContext? filePicker;
    private string filepath = "";
    protected UIContext? context;
    public NavmeshContentType displayedContentTypes = NavmeshContentType.Points|NavmeshContentType.Triangles|NavmeshContentType.AABBs|NavmeshContentType.Walls|NavmeshContentType.MainLinks|NavmeshContentType.SecondaryLinks;

    private string? loadedFilepath;
    private FileHandle? loadedFile;

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
        if (context == null) {
            context = UIContext.CreateRootContext("Navmesh", this);
            filePicker = context.AddChild<NavmeshEditor, string>(
                "File",
                this,
                new ResourcePathPicker(Scene.Workspace, KnownFileFormats.AIMap) { UseNativesPath = true, IsPathForIngame = false },
                (v) => v!.filepath,
                (v, p) => v.filepath = p ?? "");
            context.AddChild<NavmeshEditor, NavmeshContentType>(
                "Content Types",
                this,
                new CsharpFlagsEnumFieldHandler<NavmeshContentType, int>() { HideNumberInput = true },
                getter: o => o!.displayedContentTypes,
                setter: (o, v) => o.displayedContentTypes = v
            );
        }

        context.children[0].ShowUI();

        if (Target is AIMapComponentBase map) {
            var storedSuggestions = map.StoredResources.ToArray();
            if (storedSuggestions.Length > 0) {
                if (firstTimeOnTarget) {
                    if (storedSuggestions.Length == 1 && string.IsNullOrEmpty(filepath)) {
                        filepath = storedSuggestions[0];
                    }
                    firstTimeOnTarget = false;
                }
                if (ImguiHelpers.ValueCombo("Stored Files", storedSuggestions, storedSuggestions, ref filepath)) {
                    AppConfig.Instance.AddRecentNavmesh(filepath);
                }
            }
            map.visibleContentTypes = displayedContentTypes;
        }

        var settings = AppConfig.Settings;
        if (settings.RecentNavmeshes.Count > 0) {
            var options = settings.RecentNavmeshes.ToArray();
            if (ImguiHelpers.ValueCombo("Recent", options, options, ref filepath)) {
                AppConfig.Instance.AddRecentNavmesh(filepath);
            }
        }
        context.children[1].ShowUI();
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
            if (ImGui.Button("Reset preview geometry")) {
                (Target as AIMapComponentBase)?.ResetPreviewGeometry();
            }
            ImGui.SameLine();
            if (ImGui.Button("Bake navmesh ...")) {
                EditorWindow.CurrentWindow?.AddSubwindow(new NavmeshBakerUI(Scene, loadedFile, context));
            }
        }
    }

    private void OnFileChanged(bool changed)
    {
        (Target as AIMapComponentBase)?.ResetPreviewGeometry();
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
                AppConfig.Instance.AddRecentNavmesh(filepath);
            }
            comp.visibleContentTypes = displayedContentTypes;
        }
    }
}
