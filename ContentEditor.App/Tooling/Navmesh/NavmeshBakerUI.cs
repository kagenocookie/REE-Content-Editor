using System.Numerics;
using System.Runtime.InteropServices;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using DotRecast.Recast.Geom;
using ReeLib;

namespace ContentEditor.App.Tooling.Navmesh;

public class NavmeshBakerUI(Scene baseScene, FileHandle mapFileHandle, UIContext parentContext) : FileToolWindow(parentContext, mapFileHandle)
{
    public override string HandlerName => "Navmesh Baker";

    public Scene BaseScene { get; } = baseScene;
    public AimpFile MapFile { get; } = mapFileHandle.GetFile<AimpFile>();
    private NavmeshBuildParams config = new NavmeshBuildParams();

    private readonly List<Scene> sceneList = new();
    private string[] sceneLabels = [];
    private readonly List<RszInstance> sceneColliders = new();
    private string[] sceneColliderNames = [];
    private readonly HashSet<RszInstance> selectedColliders = new();
    private readonly HashSet<string> selectedCfils = new();
    private readonly HashSet<string> cfils = new();
    private string[] cfilsArray = [];
    private Scene? referenceScene = baseScene;
    private string sceneFilter = "";
    private string cfilFilter = "";
    private string colliderFilter = "";
    private bool forceReset = true;

    public override void OnIMGUI()
    {
        ImGui.Text("Base scene: " + BaseScene.InternalPath);
        ImGui.Text("Map file: " + File.Filepath);
        if (ImGui.Button("Open Map File")) {
            EditorWindow.CurrentWindow?.AddFileEditor(File);
        }
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_GenericInfo} Help")) {
            ImGui.OpenPopup("NavmeshBakeHelp");
        }
        if (ImGui.BeginPopup("NavmeshBakeHelp")) {
            ImGui.Text("The Collision Filters list can be used to adjust which colliders get autoselected on their collision filter setting.");
            ImGui.Text("\"Reset selection\" will reset the selected colliders list based on the allowed collision filters");
            if (ImGui.Button("Parameters Online Reference")) {
                FileSystemUtils.OpenURL("https://deepwiki.com/recastnavigation/recastnavigation/2-recast:-navigation-mesh-generation#configuration-parameters");
            }
            ImGui.EndPopup();
        }
        if (Context.children.Count == 0) {
            Context.AddChild<NavmeshBakerUI, NavmeshBuildParams>("Bake Parameters", this, getter: ui => ui!.config, setter: (ui, v) => ui.config = v ?? new()).AddDefaultHandler();
        }
        ImGui.SameLine();
        if (ImGui.Button("Refresh Scene List")) {
            sceneList.Clear();
        }
        if (sceneList.Count == 0) {
            sceneList.AddRange(BaseScene.RootScene.NestedScenes);
            sceneLabels = sceneList.Select(sc => sc.InternalPath).ToArray();
        }

        ImGui.SameLine();
        var sceneSpan = CollectionsMarshal.AsSpan(sceneList);
        if (ImguiHelpers.FilterableCombo("Reference Scene", sceneLabels, sceneSpan, ref referenceScene, ref sceneFilter)) {
            sceneColliders.Clear();
        }
        ImguiHelpers.Tooltip("The root scene from which to scan for valid colliders");

        if (referenceScene == null) {
            return;
        }

        if (sceneColliders.Count == 0) {
            cfils.Clear();
            var names = new List<string>();
            foreach (var scene in referenceScene.NestedScenes) {
                foreach (var go in scene.GetAllGameObjects()) {
                    var colliders = go.GetComponent<Colliders>();
                    if (colliders == null) continue;

                    foreach (var collider in colliders.EnumerableColliders) {
                        // showing only mesh shapes for now until we add support for other collider types
                        if (RszFieldCache.Collider.Shape.Get(collider).RszClass.name != "via.physics.MeshShape") continue;

                        var cfil = RszFieldCache.Collider.CollisionFilter.Get(collider);
                        if (!string.IsNullOrEmpty(cfil)) cfils.Add(cfil);
                        sceneColliders.Add(collider);
                        names.Add($"{go.Path} | {WindowHandlerFactory.GetString(collider)}");
                        if (cfil.Contains("TerrainDefault", StringComparison.InvariantCultureIgnoreCase)) {
                            // TODO maybe add a pre-configured set of CFIL's per game?
                            selectedCfils.Add(cfil);
                        }
                    }
                }
            }
            sceneColliderNames = names.ToArray();
            cfilsArray = cfils.ToArray();
            selectedColliders.Clear();
        }

        ImGui.Separator();
        ImGui.SetNextItemOpen(true, ImGuiCond.Appearing);
        Context.ShowChildrenUI();

        var childSize = ImGui.GetContentRegionAvail() - new System.Numerics.Vector2(0, UI.FontSize + ImGui.GetStyle().FramePadding.Y * 6);
        childSize.Y = Math.Clamp(childSize.Y, 50, 400);
        ImGui.BeginChild("Colliders", new Vector2(childSize.X * 0.6f, childSize.Y), ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.ResizeX);
        ImguiCheckboxes("Source colliders", sceneColliderNames, CollectionsMarshal.AsSpan(sceneColliders), selectedColliders, ref colliderFilter,
            (c) => selectedCfils.Contains(c.Get(RszFieldCache.Collider.CollisionFilter)));
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("CFILs", new Vector2(childSize.X * 0.4f, childSize.Y), ImGuiChildFlags.AutoResizeX | ImGuiChildFlags.ResizeX);
        ImguiCheckboxes("Collision Filters", cfilsArray, cfilsArray, selectedCfils, ref cfilFilter, null);
        ImGui.EndChild();

        ImGui.Separator();

        if (ImGui.Button("Reset selection") || forceReset) {
            forceReset = false;
            selectedColliders.Clear();
            foreach (var collider in sceneColliders) {
                var cfil = RszFieldCache.Collider.CollisionFilter.Get(collider);
                if (selectedCfils.Count == 0 || selectedCfils.Contains(cfil)) {
                    selectedColliders.Add(collider);
                }
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Bake")) {
            List<IInputGeomProvider> geoList = new();
            // TODO might need more IInputGeomProviders that can handle other resources (.mesh, RSZ collider shapes, .coco, .hf, .chf, ...)
            foreach (var coll in selectedColliders) {
                var shape = RszFieldCache.Collider.Shape.Get(coll);
                switch (shape.RszClass.name) {
                    case "via.physics.MeshShape": {
                            var mcolPath = RszFieldCache.MeshShape.Mesh.Get(shape);
                            if (!string.IsNullOrEmpty(mcolPath)) {
                                if (referenceScene.Workspace.ResourceManager.TryResolveGameFile(mcolPath, out var mcol)) {
                                    var mcfile = mcol.GetFile<McolFile>();
                                    if (mcfile.bvh?.triangles.Count > 0)
                                        geoList.Add(new McolInputGeomProvider(mcfile));
                                } else {
                                    Logger.Error("Failed to load mcol file " + mcolPath);
                                }
                            }
                            break;
                        }
                    default:
                        Logger.Warn("Unsupported collider shape type for navmesh bake: " + shape.RszClass.name);
                        break;
                }
            }

            if (geoList.Count == 0) {
                Logger.Error("Cannot bake navmesh: No valid collisions were selected.");
                return;
            }
            try {
                var geo = new InputGeomCombiner(geoList);
                NavmeshGenerator.RebuildNavmeshFromMesh(MapFile, config, geo);
            } catch (Exception e) {
                Logger.Error(e, "Failed to rebuild navmesh");
            } finally {
                File.EmitChange();
            }
        }
    }

    private static bool ImguiCheckboxes<T>(string label, string[] labels, ReadOnlySpan<T> values, HashSet<T> selectedItems, ref string filter, Func<T, bool>? extraFilter)
    {
        var changed = false;
        ImGui.SeparatorText(label);
        ImGui.InputText("Filter##" + label, ref filter, 120);
        var toggleVisible = false;
        if (ImGui.Button("Toggle filtered##" + label)) {
            toggleVisible = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Clear selection")) {
            selectedItems.Clear();
        }
        var toggleTargetState = (bool?)null;
        for (int i = 0; i < labels.Length; ++i) {
            var val = values[i];
            if (!string.IsNullOrEmpty(filter) && !labels[i].Contains(filter, StringComparison.InvariantCultureIgnoreCase) || extraFilter?.Invoke(val) == false) {
                continue;
            }

            var isSelected = selectedItems.Contains(val);
            if (ImGui.Checkbox(labels[i] + "##" + i, ref isSelected) || toggleVisible) {
                if (toggleTargetState == null) toggleTargetState = !isSelected;
                if (toggleVisible) {
                    isSelected = toggleTargetState.Value;
                }

                if (isSelected) selectedItems.Add(val);
                else selectedItems.Remove(val);
                changed = true;
            }
        }

        return changed;
    }
}
