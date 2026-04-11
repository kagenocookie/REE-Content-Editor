using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using System.Numerics;

namespace ContentEditor.App;

public class SceneView : IWindowHandler, IKeepEnabledWhileSaving
{
    public bool HasUnsavedChanges => false;

    public string HandlerName => $"Scene";

    int IWindowHandler.FixedID => -100;

    public ContentWorkspace Workspace { get; }
    public Scene Scene { get; }

    private WindowData data = null!;
    protected UIContext context = null!;

    public SceneView(ContentWorkspace workspace, Scene scene)
    {
        Workspace = workspace;
        Scene = scene;
    }

    public void Focus()
    {
        var data = context.Get<WindowData>();
        ImGui.SetWindowFocus(data.Name ?? $"{data.Handler}##{data.ID}");
    }

    public void Close()
    {
        var data = context.Get<WindowData>();
        EditorWindow.CurrentWindow?.CloseSubwindow(data);
    }

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow()
    {
        var dragging = Scene.Mouse.IsDragging;
        if (dragging) ImGui.EndDisabled();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0));
        if (!ImguiHelpers.BeginWindow(data, flags: ImGuiWindowFlags.MenuBar)) {
            WindowManager.Instance.CloseWindow(data);
            ImGui.PopStyleVar();
            if (dragging) ImGui.BeginDisabled();
            return;
        }
        ImGui.PopStyleVar();
        ImGui.BeginGroup();
        OnIMGUI();
        ImGui.EndGroup();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0));
        ImGui.End();
        ImGui.PopStyleVar();
        if (dragging) ImGui.BeginDisabled();
    }

    public void OnIMGUI()
    {
        if (!Scene.IsActive) {
            using var _ = ImguiHelpers.OverrideStyleCol(ImGuiCol.Text, Colors.Warning);
            ImguiHelpers.TextCentered("No active scene. Activate one from the Scenes menu.");
            return;
        }

        ShowMenu();
        var expectedSize = ImGui.GetWindowSize() - ImGui.GetCursorPos();
        expectedSize.X = Math.Max(expectedSize.X, 4);
        expectedSize.Y = Math.Max(expectedSize.Y, 4);
        Scene.RenderContext.SetRenderToTexture(expectedSize);

        if (Scene.RenderContext.RenderTargetTextureHandle == 0) {
            if (Scene.HasRenderables) {
                // the texture handle gets assigned immediately before the first render
                // that means it won't be available until all required resources load in
                ImGui.Text($"Loading scene {Scene.InternalPath ?? Scene.Name} ...");
            } else {
                using var _ = ImguiHelpers.OverrideStyleCol(ImGuiCol.Text, Colors.Warning);
                ImguiHelpers.TextCentered($"Scene {Scene.InternalPath ?? Scene.Name} has no 3D content");
            }
            return;
        }

        var c = ImGui.GetCursorPos();
        var cc = ImGui.GetCursorScreenPos();
        Scene.OwnRenderContext.ViewportOffset = cc;
        AppImguiHelpers.Image(Scene.RenderContext.RenderTargetTextureHandle, expectedSize, new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));
        Scene.RenderUI();
        ImGui.SetCursorPos(c);
        ImGui.InvisibleButton("##image", expectedSize, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        var meshClick = ImGui.IsItemClicked(ImGuiMouseButton.Right) || ImGui.IsItemClicked(ImGuiMouseButton.Left);
        var hoveredMesh = ImGui.IsItemHovered();

        if (!Scene.HasRenderables) {
            var text = $"Scene {Scene.InternalPath ?? Scene.Name} has no 3D content";
            var textSize = ImGui.CalcTextSize(text);
            ImGui.GetWindowDrawList().AddText(cc + new Vector2(expectedSize.X / 2 - textSize.X / 2, 0), ImGui.ColorConvertFloat4ToU32(Colors.Warning), $"Scene {Scene.InternalPath ?? Scene.Name} has no 3D content");
        }

        if (meshClick) {
            if (!isDragging) {
                isDragging = true;
            }
        } else if (isDragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseDown(ImGuiMouseButton.Right)) {
            isDragging = false;
        }

        if (isDragging || hoveredMesh || Scene.Mouse.IsViewportHovered && !hoveredMesh) {
            AppImguiHelpers.RedirectMouseInputToScene(Scene, hoveredMesh);
        }

        if (data.IsFocused) {
            if (AppConfig.Instance.Key_Scene_Hide.Get().IsPressed()) {
                var editors = EditorWindow.CurrentWindow!.GetSceneEditorWindows(Scene).ToList();
                foreach (var ed in editors) {
                    if (ed.Handler is not IInspectorController inspectorRoot) continue;

                    foreach (var obj in inspectorRoot.Inspector.Inspectors) {
                        if (obj.Target is IVisibilityTarget vis) {
                            vis.ShouldDrawSelf = false;
                        }
                    }
                    inspectorRoot.Inspector.Reset();
                }
            }
            if (AppConfig.Instance.Key_Scene_Focus3D.Get().IsPressed()) {
                var editors = EditorWindow.CurrentWindow!.GetSceneEditorWindows(Scene).ToList();
                foreach (var ed in editors) {
                    if (ed.Handler is not IInspectorController inspector || inspector.Inspector.PrimaryTarget is not IVisibilityTarget vis) continue;

                    if (vis is Folder f) vis.Scene?.ActiveCamera.LookAt(f, false);
                    else if (vis is GameObject go) vis.Scene?.ActiveCamera.LookAt(go, false);
                }
            }
            if (AppConfig.Instance.Key_Scene_FocusUI.Get().IsPressed()) {
                var editors = EditorWindow.CurrentWindow!.GetSceneEditorWindows(Scene).ToList();
                foreach (var ed in editors) {
                    if (ed.Handler is not ISceneEditor sceneEditor) continue;

                    var treeEditor = (sceneEditor as SceneEditor)?.Tree ?? (sceneEditor as PrefabEditor)?.Tree;
                    if (treeEditor == null) continue;

                    var primary = (ed.Handler as IInspectorController)?.Inspector.PrimaryTarget;
                    if (primary is not IVisibilityTarget vis) continue;

                    treeEditor.ScrollTo(vis);
                }
            }
            if (AppConfig.Instance.Key_Scene_Delete.Get().IsPressed()) {
                var editors = EditorWindow.CurrentWindow!.GetSceneEditorWindows(Scene).ToList();
                foreach (var ed in editors) {
                    if (ed.Handler is not IInspectorController inspectorRoot) continue;

                    foreach (var obj in inspectorRoot.Inspector.Inspectors) {
                        if (obj.Target is GameObject go) {
                            UndoRedo.RecordRemoveChild(null, go);
                        } else if (obj.Target is Folder ff) {
                            UndoRedo.RecordRemoveChild(null, ff);
                        }
                    }
                    inspectorRoot.Inspector.Reset();
                }
            }
            if (AppConfig.Instance.Key_Scene_UnhideAll.Get().IsPressed()) {
                foreach (var obj in Scene.RootFolder.GetAllFolders(true)) {
                    obj.ShouldDrawSelf = true;
                }
                foreach (var obj in Scene.RootFolder.GetAllGameObjects(true)) {
                    obj.ShouldDrawSelf = true;
                }
            }
        }
    }

    private bool isDragging;

    private void ShowMenu()
    {
        if (ImGui.BeginMenuBar()) {
            ShowEditModesMenu();
            ImGui.EndMenuBar();
        }
    }

    private void ShowEditModesMenu()
    {
        var canReopenScene = !string.IsNullOrEmpty(Scene.InternalPath) || File.Exists(Scene.Name);
        using (var _ = ImguiHelpers.Disabled(!canReopenScene)) {
            if (ImGui.MenuItem($"{AppIcons.SI_SceneParentGameObject}")) {
                EditorWindow.CurrentWindow?.OpenSceneFileEditor(Scene);
            }
            ImguiHelpers.Tooltip("Re-Open Scene Editor"u8);
        }
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(Scene.Root.GetAvailableEditModes().Any() == false)) {
            var activeEditMode = Scene.Root.ActiveEditMode;
            if (ImGui.BeginMenu(activeEditMode == null ? "Editing: --" : "Editing: " + activeEditMode.DisplayName)) {
                    if (activeEditMode != null) {
                        activeEditMode.DrawMainUI();
                        if (activeEditMode.Target is Component cc) {
                            ImGui.Spacing();
                            if (ImGui.Button($"{AppIcons.SI_ResetCamera}")) {
                                Scene.ActiveCamera.LookAt(cc.GameObject, true);
                            }
                            ImguiHelpers.Tooltip("Focus on target object"u8);
                            ImGui.SameLine();
                            ImGui.Text(activeEditMode.Target.ToString());
                        }
                        ImGui.Separator();
                    }
                    foreach (var mode in Scene.Root.GetAvailableEditModes()) {
                        var showComponents = false;
                        if (mode == activeEditMode) {
                            ImGui.PushStyleColor(ImGuiCol.Text, Colors.TextActive);
                            showComponents = ImGui.BeginMenu(mode.DisplayName);
                            ImGui.PopStyleColor();
                        } else {
                            showComponents = ImGui.BeginMenu(mode.DisplayName);
                        }

                        if (showComponents) {
                            int i = 0;
                            foreach (var editable in Scene.Root.GetEditableComponents(mode)) {
                                var comp = (Component)editable;
                                ImGui.PushID(i++);
                                if (mode == activeEditMode && editable == activeEditMode.Target) {
                                    ImGui.PushStyleColor(ImGuiCol.Text, Colors.TextActive);
                                    if (ImGui.MenuItem(comp.GameObject.Path + " | " + comp.Scene?.InternalPath)) {
                                        Scene.Root.DisableEditMode();
                                    }
                                    ImGui.PopStyleColor();
                                } else {
                                    if (ImGui.MenuItem(comp.GameObject.Path + " | " + comp.Scene?.InternalPath)) {
                                        Scene.Root.SetEditMode(editable);
                                    }
                                }
                                ImGui.PopID();
                            }
                            ImGui.EndMenu();
                        }
                    }

                    ImGui.EndMenu();
                }
        }
        if (ImGui.MenuItem($"{AppIcons.SI_GenericCamera} Controls")) ImGui.OpenPopup("CameraSettings");
        if (Scene != null && ImGui.BeginPopup("CameraSettings")) {
            Scene.Controller.ShowCameraControls();
            if (Scene.ActiveCamera.ProjectionMode != AppConfig.Settings.SceneView.DefaultProjection) {
                AppConfig.Settings.SceneView.DefaultProjection = Scene.ActiveCamera.ProjectionMode;
                AppConfig.Settings.Save();
            }
            if (Math.Abs(Scene.Controller.MoveSpeed - AppConfig.Settings.SceneView.MoveSpeed) > 0.001f) {
                AppConfig.Settings.SceneView.MoveSpeed = Scene.Controller.MoveSpeed;
                AppConfig.Settings.Save();
            }
            ImGui.EndPopup();
        }
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();
    }

    public bool RequestClose()
    {
        return false;
    }
}
