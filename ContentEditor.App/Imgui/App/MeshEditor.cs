using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ReeLib.Mesh;
using ReeLib.via;
using Silk.NET.Input;

namespace ContentEditor.App;

/// <summary>
/// File dedicated to the mesh editor part of the mesh viewer
/// </summary>
internal sealed class MeshEditor(MeshViewer viewer) : IDisposable
{
    public const float SplitterWidth = 8.0f;

    public MeshViewer Viewer { get; } = viewer;
    public bool IsEnabled { get; private set; }
    public bool WasEverEnabled { get; private set; }
    public bool HasSidePanel => IsEnabled && interactionMode == EditorInteractionMode.Object;
    public MeshDisplayMode DisplayMode { get; private set; }

    private EditorInteractionMode interactionMode = EditorInteractionMode.Object;
    private enum EditorInteractionMode
    {
        Object,
        Edit,
    }

    private Scene? subscribedScene;
    private readonly HashSet<SubmeshReference> selectedSubmeshes = [];
    private readonly HashSet<MeshViewerContext> selectedArmatures = [];
    private readonly HashSet<SubmeshReference> hiddenSubmeshes = [];
    private readonly HashSet<MeshViewerContext> hiddenArmatures = [];
    private readonly Dictionary<MeshViewerContext, Vector2[]> armatureScreenPositions = [];
    private SubmeshReference? submeshSelectionAnchor;
    private SubmeshReference? scrollToSubmesh;
    private readonly HashSet<VertexReference> selectedVertices = [];
    private readonly HashSet<BoneElementReference> selectedBoneElements = [];
    private Dictionary<VertexReference, Vector3>? moveOriginalPositions;
    private Dictionary<BoneReference, BoneTransformState>? moveOriginalBoneTransforms;
    private Dictionary<VertexReference, Vector3>? moveVertexDeltaSigns;
    private Dictionary<BoneReference, Vector3>? moveBoneDeltaSigns;
    private Vector3 moveAnchorWorld;
    private Vector3 moveStartWorld;
    private Vector2 moveStartScreen;
    private MoveConstraint moveConstraint;
    private bool extendVertexSelection;
    private bool toggleVertexSelection;
    private Vector2 boxSelectStart;
    private Vector2 boxSelectEnd;
    private bool boxSelecting;
    private bool shiftSubmeshSelection;
    private bool ctrlSubmeshSelection;
    private bool suppressNextSceneClick;
    private MeshEditorOptionsWindow? optionsWindow;
    private WindowData? optionsWindowData;
    private float vertexPointSize = AppConfig.Settings.MeshViewer.EditorVertexSize;
    private float vertexSelectionRadius = AppConfig.Settings.MeshViewer.EditorVertexSelectionRadius;
    private bool mirrorX = AppConfig.Settings.MeshViewer.EditorMirrorX;
    private bool mirrorY = AppConfig.Settings.MeshViewer.EditorMirrorY;
    private bool mirrorZ = AppConfig.Settings.MeshViewer.EditorMirrorZ;
    private float mirrorRadius = AppConfig.Settings.MeshViewer.EditorMirrorRadius;
    private bool optionsStayOnTop = AppConfig.Settings.MeshViewer.EditorOptionsStayOnTop;
    private float panelWidth;
    private bool panelWidthInitialized;
    private bool panelWidthUserResized;

    private readonly record struct SubmeshReference(MeshViewerContext Context, int Index);
    private readonly record struct VertexReference(MeshViewerContext Context, MeshBuffer Buffer, int Index);
    private readonly record struct BoneReference(MeshViewerContext Context, MeshBone Bone);
    private readonly record struct BoneElementReference(BoneReference Bone, BoneElement Element);
    private readonly record struct BoneHit(BoneElementReference Reference, float DistanceSquared);
    private readonly record struct BoneTransformState(Matrix4x4 Local, Matrix4x4 Global, Matrix4x4 InverseGlobal);
    private readonly record struct MirrorGridKey(int X, int Y, int Z);

    private enum BoneElement
    {
        Head,
        Body,
        Tail,
    }

    private bool IsMoving => moveOriginalPositions != null || moveOriginalBoneTransforms != null;

    private enum MoveConstraint
    {
        None,
        X,
        Y,
        Z,
        ExceptX,
        ExceptY,
        ExceptZ,
    }

    public void ShowButton(MeshViewerContext context)
    {
        EnsureSceneSubscription();
        ApplyRenderState();
        if (ImGui.MenuItem(Lang.MeshViewer.Title_Editor, "", IsEnabled)) {
            SetEnabled(!IsEnabled);
        }
    }

    public void ShowDisplayModeControls()
    {
        if (ImGui.RadioButton(Lang.MeshViewer.Display_Default, DisplayMode == MeshDisplayMode.Default)) SetDisplayMode(MeshDisplayMode.Default);
        ImGui.SameLine();
        if (ImGui.RadioButton(Lang.MeshViewer.Display_Solid, DisplayMode == MeshDisplayMode.Solid)) SetDisplayMode(MeshDisplayMode.Solid);
        ImGui.SameLine();
        if (ImGui.RadioButton(Lang.MeshViewer.Display_Wireframe, DisplayMode == MeshDisplayMode.Wireframe)) SetDisplayMode(MeshDisplayMode.Wireframe);
    }

    public bool ShowViewportModeControls(Vector2 viewportPosition, Vector2 viewportSize)
    {
        if (!IsEnabled) return false;

        var viewportHovered = ImGui.IsMouseHoveringRect(viewportPosition, viewportPosition + viewportSize);
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || viewportHovered) {
            //var keyboard = EditorWindow.CurrentWindow?.LastKeyboard;
            var selectAllPressed = AppConfig.Instance.Key_MeshViewer_SelectAll.Get().IsPressed();
            if (interactionMode == EditorInteractionMode.Object && selectAllPressed) ToggleAllObjects();
            if (interactionMode == EditorInteractionMode.Edit) {
                if (!IsMoving && selectAllPressed) ToggleAllEditableElements();
                if (!IsMoving && (selectedVertices.Count > 0 || selectedBoneElements.Count > 0) && AppConfig.Instance.Key_MeshViewer_MoveGeometry.Get().IsPressed()) BeginMove();
                if (IsMoving) {
                    UpdateMoveConstraint();
                    if (ImGui.IsKeyPressed(ImGuiKey.Escape)) CancelMove();
                    else if (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter)) CommitMove();
                    else UpdateMovePreview();
                }
            }
        }

        if (interactionMode == EditorInteractionMode.Object) {
            DrawArmatures(viewportPosition, viewportSize);
        } else {
            if (selectedArmatures.Count > 0) DrawArmatures(viewportPosition, viewportSize);
            DrawSelectedVertices(viewportPosition, viewportSize);
        }
        DrawBoxSelection(viewportPosition);

        var controlsStart = ImGui.GetCursorPos();
        var hovered = false;
        if (ShowModeButton(Lang.MeshViewer.Editor_ModeObject.String, interactionMode == EditorInteractionMode.Object)) SetInteractionMode(EditorInteractionMode.Object);
        hovered |= ImGui.IsItemHovered();
        ImGui.SameLine();
        if (ShowModeButton(Lang.MeshViewer.Editor_ModeEdit.String, interactionMode == EditorInteractionMode.Edit)) SetInteractionMode(EditorInteractionMode.Edit);
        hovered |= ImGui.IsItemHovered();
        if (interactionMode == EditorInteractionMode.Edit) {
            ImGui.SetCursorPos(new Vector2(controlsStart.X, controlsStart.Y + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y));
            if (ImGui.Button(Lang.MeshViewer.Editor_Options)) OpenOptions();
            hovered |= ImGui.IsItemHovered();
            if (IsMoving) {
                ImGui.SameLine();
                ImGui.TextUnformatted(GetMoveStatus());
            }
        }

        if (IsMoving && !hovered && ImGui.IsMouseHoveringRect(viewportPosition, viewportPosition + viewportSize)) {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                CommitMove();
                suppressNextSceneClick = true;
            } else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) {
                CancelMove();
                suppressNextSceneClick = true;
            }
        }
        return hovered;
    }

    private static bool ShowModeButton(string label, bool selected)
    {
        if (selected) {
            ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
        }
        var clicked = ImGui.Button(label);
        if (selected) ImGui.PopStyleColor(2);
        return clicked;
    }

    public float GetPanelWidth(float availableWidth)
    {
        var maxWidth = Math.Max(availableWidth * 0.75f, 4.0f);
        var minWidth = Math.Min(Math.Max(180.0f * UI.UIScale, ImGui.GetFontSize() * 7.0f), maxWidth);
        if (!panelWidthInitialized) {
            panelWidthInitialized = true;
            panelWidth = Math.Clamp(GetContentWidth(), minWidth, maxWidth);
        } else {
            panelWidth = Math.Clamp(panelWidth, minWidth, maxWidth);
            if (!panelWidthUserResized) panelWidth = Math.Max(panelWidth, Math.Min(GetContentWidth(), maxWidth));
        }
        return panelWidth;
    }

    public void ShowSplitter(float height, float availableWidth)
    {
        ImGui.InvisibleButton("##MeshEditorSplitter", new Vector2(SplitterWidth, height));
        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();
        if (active) {
            var maxWidth = Math.Max(availableWidth * 0.75f, 4.0f);
            var minWidth = Math.Min(Math.Max(180.0f * UI.UIScale, ImGui.GetFontSize() * 7.0f), maxWidth);
            panelWidth = Math.Clamp(panelWidth - ImGui.GetIO().MouseDelta.X, minWidth, maxWidth);
            panelWidthUserResized = true;
        }
        if (hovered || active) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var x = min.X + SplitterWidth * 0.5f;
        var color = ImGui.GetColorU32(active ? ImGuiCol.SeparatorActive : hovered ? ImGuiCol.SeparatorHovered : ImGuiCol.Separator);
        drawList.AddLine(new Vector2(x, min.Y), new Vector2(x, min.Y + height), color, 2.0f);
    }

    public void ShowPanel(Vector2 size)
    {
        if (!HasSidePanel) return;

        ImGui.BeginChild("##MeshEditorPanel", size, ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysUseWindowPadding, ImGuiWindowFlags.HorizontalScrollbar);
        ImGui.Text(Lang.MeshViewer.Editor_Objects);
        ImGui.Separator();

        var contexts = Viewer.MeshContexts;
        var hasArmatures = contexts.Any(context => context.Mesh?.Bones?.Bones.Count > 0);
        if (hasArmatures) {
            ImGui.SeparatorText(Lang.MeshViewer.Armature);
            for (var contextIndex = 0; contextIndex < contexts.Count; contextIndex++) {
                var context = contexts[contextIndex];
                if (context.Mesh?.Bones?.Bones.Count is not > 0) continue;
                if (ShowVisibilityButton($"armature_visibility_{contextIndex}", !hiddenArmatures.Contains(context))) {
                    ToggleArmatureVisibility(context);
                }
                ImGui.SameLine();
                var label = contexts.Count > 1 ? $"{AppIcons.SI_FileType_FBXSKEL} {context.ShortName} {Lang.MeshViewer.Armature}" : $"{AppIcons.SI_FileType_FBXSKEL} {Lang.MeshViewer.Armature}";
                if (ImGui.Selectable($"{label}##armature_{contextIndex}", selectedArmatures.Contains(context), ImGuiSelectableFlags.None, new Vector2(Math.Max(ImGui.CalcTextSize(label).X, ImGui.GetContentRegionAvail().X), 0))) {
                    SelectArmature(context, ImGui.IsKeyDown(ImGuiKey.ModShift), ImGui.IsKeyDown(ImGuiKey.ModCtrl));
                }
            }
            ImGui.Spacing();
        }

        ImGui.SeparatorText(Lang.MeshViewer.Editor_Submeshes);
        var hasSubmeshes = false;
        for (int contextIndex = 0; contextIndex < contexts.Count; contextIndex++) {
            var context = contexts[contextIndex];
            var meshes = context.Component.MeshHandle?.Meshes;
            if (meshes == null) continue;

            if (contexts.Count > 1) ImGui.SeparatorText(context.ShortName);
            var submeshIndex = 0;
            foreach (var mesh in meshes) {
                hasSubmeshes = true;
                var submesh = new SubmeshReference(context, submeshIndex);
                var selected = selectedSubmeshes.Contains(submesh);
                var label = GetSubmeshLabel(context, submeshIndex, mesh.MeshGroup);
                var labelWidth = ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2.0f;
                if (ShowVisibilityButton($"submesh_visibility_{contextIndex}_{submeshIndex}", !hiddenSubmeshes.Contains(submesh))) {
                    ToggleSubmeshVisibility(submesh);
                }
                ImGui.SameLine();
                var selectableWidth = Math.Max(labelWidth, ImGui.GetContentRegionAvail().X);
                if (ImGui.Selectable($"{label}##submesh_{contextIndex}_{submeshIndex}", selected, ImGuiSelectableFlags.None, new Vector2(selectableWidth, 0))) {
                    SelectSubmesh(submesh, ImGui.IsKeyDown(ImGuiKey.ModShift), ImGui.IsKeyDown(ImGuiKey.ModCtrl), false);
                }
                if (labelWidth > ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X * 2.0f) {
                    ImguiHelpers.Tooltip(label);
                }
                if (scrollToSubmesh == submesh) {
                    ImGui.SetScrollHereY();
                    scrollToSubmesh = null;
                }
                submeshIndex++;
            }

        }

        if (!hasSubmeshes) ImGui.TextDisabled(Lang.MeshViewer.Editor_NoSubmeshes);
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered()) {
            ClearAllSelection();
        }
        ImGui.EndChild();
    }

    private static bool ShowVisibilityButton(string id, bool visible)
    {
        ImGui.PushID(id);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.Text) with { W = visible ? 1.0f : 0.45f });
        var clicked = ImGui.SmallButton($"{(visible ? AppIcons.Eye : AppIcons.EyeBlocked)}");
        ImGui.PopStyleColor(2);
        ImGui.PopID();
        ImguiHelpers.Tooltip(visible ? "Hide"u8 : "Show"u8);
        return clicked;
    }

    private void ToggleSubmeshVisibility(SubmeshReference submesh)
    {
        if (!hiddenSubmeshes.Add(submesh)) {
            hiddenSubmeshes.Remove(submesh);
        } else {
            selectedSubmeshes.Remove(submesh);
            selectedVertices.RemoveWhere(vertex => vertex.Context == submesh.Context);
        }
        ApplyRenderState();
    }

    private void ToggleArmatureVisibility(MeshViewerContext context)
    {
        if (!hiddenArmatures.Add(context)) {
            hiddenArmatures.Remove(context);
        } else {
            selectedArmatures.Remove(context);
            selectedBoneElements.RemoveWhere(element => element.Bone.Context == context);
        }
    }

    private void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled) return;
        if (!enabled) {
            CancelMove();
            CloseOptions();
        }
        IsEnabled = enabled;
        WasEverEnabled |= enabled;
        ClearAllSelection();
        if (enabled) {
            panelWidthInitialized = false;
            panelWidthUserResized = false;
        }
        EnsureSceneSubscription();
        ApplyRenderState();
    }

    private void SetDisplayMode(MeshDisplayMode mode)
    {
        DisplayMode = mode;
        ApplyRenderState();
    }

    private void SetInteractionMode(EditorInteractionMode mode)
    {
        if (interactionMode == mode) return;
        CancelMove();
        if (mode != EditorInteractionMode.Edit) CloseOptions();
        interactionMode = mode;
        if (mode == EditorInteractionMode.Edit) {
            foreach (var context in selectedArmatures) context.Animator?.Stop();
        }
        boxSelecting = false;
        extendVertexSelection = false;
        toggleVertexSelection = false;
        shiftSubmeshSelection = false;
        ctrlSubmeshSelection = false;
        suppressNextSceneClick = false;
        selectedVertices.Clear();
        selectedBoneElements.Clear();
        subscribedScene?.Mouse.ResetClickSequence();
        ApplyRenderState();
    }

    private void EnsureSceneSubscription()
    {
        var targetScene = IsEnabled ? Viewer.Scene : null;
        if (subscribedScene == targetScene) return;

        if (subscribedScene != null) {
            subscribedScene.Mouse.Pressed -= OnScenePressed;
            subscribedScene.Mouse.Clicked -= OnSceneClicked;
            subscribedScene.Mouse.DoubleClicked -= OnSceneDoubleClicked;
            subscribedScene.Mouse.Dragging -= OnSceneDragging;
            subscribedScene.Mouse.StopDragging -= OnSceneStopDragging;
        }
        subscribedScene = targetScene;
        if (subscribedScene != null) {
            subscribedScene.Mouse.Pressed += OnScenePressed;
            subscribedScene.Mouse.Clicked += OnSceneClicked;
            subscribedScene.Mouse.DoubleClicked += OnSceneDoubleClicked;
            subscribedScene.Mouse.Dragging += OnSceneDragging;
            subscribedScene.Mouse.StopDragging += OnSceneStopDragging;
        }
    }

    private void OnScenePressed(ImGuiMouseButton button, Vector2 viewportPosition)
    {
        if (button != ImGuiMouseButton.Left) return;
        if (interactionMode == EditorInteractionMode.Edit && !IsMoving) {
            extendVertexSelection = ImGui.IsKeyDown(ImGuiKey.ModShift)
                || EditorWindow.CurrentWindow?.LastKeyboard.IsKeyPressed(Key.ShiftLeft) == true
                || EditorWindow.CurrentWindow?.LastKeyboard.IsKeyPressed(Key.ShiftRight) == true;
            toggleVertexSelection = ImGui.IsKeyDown(ImGuiKey.ModCtrl)
                || EditorWindow.CurrentWindow?.LastKeyboard.IsKeyPressed(Key.ControlLeft) == true
                || EditorWindow.CurrentWindow?.LastKeyboard.IsKeyPressed(Key.ControlRight) == true;
            boxSelectStart = viewportPosition;
            boxSelectEnd = viewportPosition;
            boxSelecting = false;
        } else if (interactionMode == EditorInteractionMode.Object) {
            shiftSubmeshSelection = ImGui.IsKeyDown(ImGuiKey.ModShift)
                || EditorWindow.CurrentWindow?.LastKeyboard.IsKeyPressed(Key.ShiftLeft) == true
                || EditorWindow.CurrentWindow?.LastKeyboard.IsKeyPressed(Key.ShiftRight) == true;
            ctrlSubmeshSelection = ImGui.IsKeyDown(ImGuiKey.ModCtrl)
                || EditorWindow.CurrentWindow?.LastKeyboard.IsKeyPressed(Key.ControlLeft) == true
                || EditorWindow.CurrentWindow?.LastKeyboard.IsKeyPressed(Key.ControlRight) == true;
            boxSelectStart = viewportPosition;
            boxSelectEnd = viewportPosition;
            boxSelecting = false;
        }
    }

    private void OnSceneDragging(Vector2 viewportPosition)
    {
        if (IsMoving || interactionMode is not (EditorInteractionMode.Object or EditorInteractionMode.Edit)
            || subscribedScene?.Mouse.IsLeftDown != true) return;
        boxSelectEnd = viewportPosition;
        boxSelecting |= Vector2.DistanceSquared(boxSelectStart, boxSelectEnd) >= 16.0f;
    }

    private void OnSceneStopDragging(Vector2 viewportPosition)
    {
        if (!boxSelecting) return;
        boxSelectEnd = viewportPosition;
        if (interactionMode == EditorInteractionMode.Edit) {
            SelectElementsInBox(boxSelectStart, boxSelectEnd, extendVertexSelection, toggleVertexSelection);
        } else if (interactionMode == EditorInteractionMode.Object) {
            SelectObjectsInBox(boxSelectStart, boxSelectEnd, shiftSubmeshSelection);
        }
        boxSelecting = false;
        extendVertexSelection = false;
        toggleVertexSelection = false;
        shiftSubmeshSelection = false;
        ctrlSubmeshSelection = false;
    }

    private void OnSceneClicked(ImGuiMouseButton button, Vector2 viewportPosition)
    {
        if (!IsEnabled || subscribedScene == null) return;

        if (suppressNextSceneClick) {
            suppressNextSceneClick = false;
            return;
        }

        if (IsMoving) {
            if (button == ImGuiMouseButton.Left) CommitMove();
            else if (button == ImGuiMouseButton.Right) CancelMove();
            return;
        }

        if (button != ImGuiMouseButton.Left) return;
        if (interactionMode == EditorInteractionMode.Edit) {
            SelectEditElement(viewportPosition, extendVertexSelection, toggleVertexSelection);
            extendVertexSelection = false;
            toggleVertexSelection = false;
            return;
        }

        var armatureHit = FindClosestBone(viewportPosition, Viewer.MeshContexts.Where(context => !hiddenArmatures.Contains(context)));
        if (armatureHit is { } armature) {
            SelectViewportArmature(armature.Reference.Bone.Context, shiftSubmeshSelection);
            shiftSubmeshSelection = false;
            ctrlSubmeshSelection = false;
            return;
        }

        var ray = subscribedScene.ActiveCamera.ViewportToRay(viewportPosition);
        MeshViewerContext? closestContext = null;
        var closestSubmesh = -1;
        var closestDistance = float.MaxValue;

        foreach (var context in Viewer.MeshContexts) {
            var handle = context.Component.MeshHandle;
            if (handle == null || !context.GameObject.ShouldDraw) continue;

            var hit = handle.Handle.GetIntersection(ray, context.GameObject.Transform.WorldTransform, context.Component.HiddenPreviewSubmeshIndices);
            if (!hit.IsHit || hit.distanceSquared >= closestDistance) continue;
            closestContext = context;
            closestSubmesh = hit.meshIndex;
            closestDistance = hit.distanceSquared;
        }

        if (closestContext == null) {
            if (!shiftSubmeshSelection) ClearAllSelection();
        } else {
            SelectViewportSubmesh(new SubmeshReference(closestContext, closestSubmesh), shiftSubmeshSelection);
        }
        shiftSubmeshSelection = false;
        ctrlSubmeshSelection = false;
    }

    private void OnSceneDoubleClicked(ImGuiMouseButton button, Vector2 viewportPosition)
    {
        if (interactionMode == EditorInteractionMode.Object) OnSceneClicked(button, viewportPosition);
    }

    private void SelectViewportSubmesh(SubmeshReference submesh, bool extendSelection)
    {
        if (!extendSelection) {
            selectedSubmeshes.Clear();
            selectedArmatures.Clear();
            selectedBoneElements.Clear();
        }
        selectedSubmeshes.Add(submesh);
        submeshSelectionAnchor = submesh;
        scrollToSubmesh = submesh;
        selectedVertices.Clear();
        ApplyRenderState();
    }

    private void SelectViewportArmature(MeshViewerContext context, bool extendSelection)
    {
        if (!extendSelection) {
            selectedSubmeshes.Clear();
            selectedArmatures.Clear();
            submeshSelectionAnchor = null;
            scrollToSubmesh = null;
            selectedVertices.Clear();
        }
        selectedArmatures.Add(context);
        selectedBoneElements.Clear();
        ApplyRenderState();
    }

    private void SelectSubmesh(SubmeshReference submesh, bool shift, bool ctrl, bool scrollToSelected)
    {
        var ordered = GetSelectableSubmeshes();
        if (!shift && !ctrl) {
            selectedArmatures.Clear();
            selectedBoneElements.Clear();
        }
        if (shift && submeshSelectionAnchor is { } anchor) {
            var anchorIndex = ordered.IndexOf(anchor);
            var clickedIndex = ordered.IndexOf(submesh);
            if (anchorIndex >= 0 && clickedIndex >= 0) {
                for (var index = Math.Min(anchorIndex, clickedIndex); index <= Math.Max(anchorIndex, clickedIndex); index++) {
                    selectedSubmeshes.Add(ordered[index]);
                }
            }
        } else if (ctrl) {
            if (!selectedSubmeshes.Add(submesh)) selectedSubmeshes.Remove(submesh);
            submeshSelectionAnchor = submesh;
        } else {
            selectedSubmeshes.Clear();
            selectedSubmeshes.Add(submesh);
            submeshSelectionAnchor = submesh;
        }
        if (scrollToSelected) scrollToSubmesh = submesh;
        selectedVertices.Clear();
        ApplyRenderState();
    }

    private void SelectArmature(MeshViewerContext context, bool shift, bool ctrl)
    {
        if (!shift && !ctrl) {
            selectedSubmeshes.Clear();
            submeshSelectionAnchor = null;
            scrollToSubmesh = null;
            selectedVertices.Clear();
        }
        if (ctrl) {
            if (!selectedArmatures.Add(context)) selectedArmatures.Remove(context);
        } else if (shift) {
            selectedArmatures.Add(context);
        } else {
            selectedArmatures.Clear();
            selectedArmatures.Add(context);
        }
        selectedBoneElements.Clear();
        ApplyRenderState();
    }

    private List<SubmeshReference> GetSelectableSubmeshes()
    {
        var submeshes = new List<SubmeshReference>();
        foreach (var context in Viewer.MeshContexts) {
            var count = context.Component.MeshHandle?.Meshes.Count() ?? 0;
            for (var index = 0; index < count; index++) {
                var submesh = new SubmeshReference(context, index);
                if (!hiddenSubmeshes.Contains(submesh)) submeshes.Add(submesh);
            }
        }
        return submeshes;
    }

    private void ToggleAllObjects()
    {
        var submeshes = GetSelectableSubmeshes();
        var armatures = Viewer.MeshContexts
            .Where(context => context.Mesh?.Bones?.Bones.Count > 0 && !hiddenArmatures.Contains(context))
            .ToArray();
        if ((submeshes.Count > 0 || armatures.Length > 0)
            && submeshes.All(selectedSubmeshes.Contains)
            && armatures.All(selectedArmatures.Contains)) {
            ClearAllSelection();
            return;
        }

        selectedSubmeshes.Clear();
        selectedSubmeshes.UnionWith(submeshes);
        selectedArmatures.Clear();
        selectedArmatures.UnionWith(armatures);
        submeshSelectionAnchor = submeshes.Count > 0 ? submeshes[0] : null;
        selectedVertices.Clear();
        selectedBoneElements.Clear();
        ApplyRenderState();
    }

    private void ToggleAllEditableElements()
    {
        var vertices = EnumerateEditableVertices().ToArray();
        var boneElements = EnumerateEditableBoneElements().ToArray();
        if ((vertices.Length > 0 || boneElements.Length > 0)
            && vertices.All(selectedVertices.Contains)
            && boneElements.All(selectedBoneElements.Contains)) {
            selectedVertices.Clear();
            selectedBoneElements.Clear();
        } else {
            selectedVertices.Clear();
            selectedVertices.UnionWith(vertices);
            selectedBoneElements.Clear();
            selectedBoneElements.UnionWith(boneElements);
        }
    }

    private void ClearSubmeshSelection()
    {
        selectedSubmeshes.Clear();
        submeshSelectionAnchor = null;
        scrollToSubmesh = null;
        selectedVertices.Clear();
        ApplyRenderState();
    }

    private void ClearAllSelection()
    {
        ClearSubmeshSelection();
        selectedArmatures.Clear();
        selectedBoneElements.Clear();
    }

    private void SelectEditElement(Vector2 viewportPosition, bool extendSelection, bool toggleSelection)
    {
        var boneHit = FindClosestBone(viewportPosition, selectedArmatures);
        if (boneHit is { } hit) {
            if (!extendSelection && !toggleSelection) {
                selectedVertices.Clear();
                selectedBoneElements.Clear();
            }
            if (toggleSelection) {
                if (!selectedBoneElements.Add(hit.Reference)) selectedBoneElements.Remove(hit.Reference);
            } else {
                selectedBoneElements.Add(hit.Reference);
            }
            return;
        }

        if (!extendSelection && !toggleSelection) selectedBoneElements.Clear();
        SelectVertex(viewportPosition, extendSelection, toggleSelection);
    }

    private void SelectElementsInBox(Vector2 start, Vector2 end, bool extendSelection, bool toggleSelection)
    {
        if (!extendSelection && !toggleSelection) {
            selectedVertices.Clear();
            selectedBoneElements.Clear();
        }
        SelectVerticesInBox(start, end, true, toggleSelection);
        SelectBonesInBox(start, end, toggleSelection);
    }

    private void SelectObjectsInBox(Vector2 start, Vector2 end, bool extendSelection)
    {
        if (subscribedScene == null) return;
        var min = Vector2.Min(start, end);
        var max = Vector2.Max(start, end);
        var camera = subscribedScene.ActiveCamera;
        var depth = ReadSelectionDepth(min, max);
        var boxedSubmeshes = new HashSet<SubmeshReference>();
        var boxedArmatures = new HashSet<MeshViewerContext>();

        foreach (var context in Viewer.MeshContexts) {
            if (!context.GameObject.ShouldDraw) continue;
            var lod = context.MeshFile.NativeMesh.MeshData?.LODs.FirstOrDefault();
            var renderMeshes = context.Component.MeshHandle?.Meshes.ToArray();
            if (lod != null && renderMeshes != null) {
                var submeshIndex = 0;
                foreach (var group in lod.MeshGroups) {
                    foreach (var submesh in group.Submeshes) {
                        var currentIndex = submeshIndex++;
                        var reference = new SubmeshReference(context, currentIndex);
                        var renderMesh = renderMeshes.ElementAtOrDefault(currentIndex);
                        if (renderMesh == null || hiddenSubmeshes.Contains(reference)
                            || !context.Component.MeshHandle!.GetMeshPartEnabled(renderMesh.MeshGroup)) continue;

                        for (var localIndex = 0; localIndex < submesh.vertCount; localIndex++) {
                            var vertex = new VertexReference(context, submesh.Buffer, submesh.vertsIndexOffset + localIndex);
                            var world = GetVertexWorldPosition(vertex, vertex.Buffer.Positions[vertex.Index]);
                            var projected = camera.WorldToViewportPosition(world, true, true);
                            if (projected.X == float.MaxValue || projected.X < min.X || projected.Y < min.Y
                                || projected.X > max.X || projected.Y > max.Y || !IsVertexVisible(projected, depth)) continue;
                            boxedSubmeshes.Add(reference);
                            break;
                        }
                    }
                }
            }

            var bones = context.Mesh?.Bones?.Bones;
            if (bones == null || hiddenArmatures.Contains(context)) continue;
            foreach (var bone in bones) {
                var reference = new BoneReference(context, bone);
                var head = GetBoneHeadViewportPosition(reference);
                var tail = GetBoneViewportPosition(reference);
                if (head.X != float.MaxValue && tail.X != float.MaxValue && SegmentIntersectsRectangle(head, tail, min, max)) {
                    boxedArmatures.Add(context);
                    break;
                }
            }
        }

        if (!extendSelection) {
            selectedSubmeshes.Clear();
            selectedArmatures.Clear();
        }
        selectedSubmeshes.UnionWith(boxedSubmeshes);
        selectedArmatures.UnionWith(boxedArmatures);
        selectedVertices.Clear();
        selectedBoneElements.Clear();
        submeshSelectionAnchor = boxedSubmeshes.Count > 0 ? boxedSubmeshes.First() : null;
        scrollToSubmesh = null;
        ApplyRenderState();
    }

    private static bool SegmentIntersectsRectangle(Vector2 start, Vector2 end, Vector2 min, Vector2 max)
    {
        if (PointInRectangle(start, min, max) || PointInRectangle(end, min, max)) return true;
        var topLeft = min;
        var topRight = new Vector2(max.X, min.Y);
        var bottomLeft = new Vector2(min.X, max.Y);
        var bottomRight = max;
        return SegmentsIntersect(start, end, topLeft, topRight)
            || SegmentsIntersect(start, end, topRight, bottomRight)
            || SegmentsIntersect(start, end, bottomRight, bottomLeft)
            || SegmentsIntersect(start, end, bottomLeft, topLeft);
    }

    private static bool PointInRectangle(Vector2 point, Vector2 min, Vector2 max) =>
        point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y;

    private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var ab = b - a;
        var cd = d - c;
        var denominator = Cross(ab, cd);
        if (Math.Abs(denominator) < 0.000001f) return false;
        var offset = c - a;
        var first = Cross(offset, cd) / denominator;
        var second = Cross(offset, ab) / denominator;
        return first is >= 0 and <= 1 && second is >= 0 and <= 1;
    }

    private static float Cross(Vector2 left, Vector2 right) => left.X * right.Y - left.Y * right.X;

    private void SelectBonesInBox(Vector2 start, Vector2 end, bool toggleSelection)
    {
        var min = Vector2.Min(start, end);
        var max = Vector2.Max(start, end);
        foreach (var element in EnumerateEditableBoneElements()) {
            var screen = GetBoneElementViewportPosition(element);
            if (screen.X == float.MaxValue || screen.X < min.X || screen.Y < min.Y || screen.X > max.X || screen.Y > max.Y) continue;
            if (toggleSelection) {
                if (!selectedBoneElements.Add(element)) selectedBoneElements.Remove(element);
            } else {
                selectedBoneElements.Add(element);
            }
        }
    }

    private BoneHit? FindClosestBone(Vector2 viewportPosition, IEnumerable<MeshViewerContext> contexts)
    {
        var maximumDistanceSquared = MathF.Pow(Math.Max(8.0f * UI.UIScale, 6.0f), 2.0f);
        BoneHit? closest = null;
        foreach (var context in contexts) {
            var bones = context.Mesh?.Bones?.Bones;
            if (bones == null || bones.Count == 0 || hiddenArmatures.Contains(context) || !context.GameObject.ShouldDraw) continue;
            foreach (var bone in bones) {
                var reference = new BoneReference(context, bone);
                var head = GetBoneHeadViewportPosition(reference);
                var tail = GetBoneViewportPosition(reference);
                if (head.X == float.MaxValue || tail.X == float.MaxValue) continue;

                var headDistance = Vector2.DistanceSquared(viewportPosition, head);
                var tailDistance = Vector2.DistanceSquared(viewportPosition, tail);
                var endpointDistance = Math.Min(headDistance, tailDistance);
                var element = headDistance <= tailDistance ? BoneElement.Head : BoneElement.Tail;
                var distanceSquared = endpointDistance;
                if (endpointDistance > maximumDistanceSquared) {
                    distanceSquared = DistanceSquaredToSegment(viewportPosition, head, tail);
                    element = BoneElement.Body;
                }
                if (distanceSquared > maximumDistanceSquared || closest is { } current && distanceSquared >= current.DistanceSquared) continue;
                closest = new BoneHit(new BoneElementReference(reference, element), distanceSquared);
            }
        }
        return closest;
    }

    private Vector2 GetBoneViewportPosition(BoneReference reference)
    {
        if (subscribedScene == null || reference.Context.Mesh is not { } mesh) return new Vector2(float.MaxValue);
        var bone = reference.Bone;
        var transform = (uint)bone.index < (uint)mesh.BoneMatrices.Length ? mesh.BoneMatrices[bone.index] : bone.globalTransform.ToSystem();
        var world = Vector3.Transform(transform.Translation, reference.Context.GameObject.Transform.WorldTransform);
        return subscribedScene.ActiveCamera.WorldToViewportXYPosition(world, true, true);
    }

    private Vector2 GetBoneHeadViewportPosition(BoneReference reference)
    {
        if (subscribedScene == null) return new Vector2(float.MaxValue);
        if (reference.Bone.Parent != null) {
            return GetBoneViewportPosition(new BoneReference(reference.Context, reference.Bone.Parent));
        }
        var world = Vector3.Transform(Vector3.Zero, reference.Context.GameObject.Transform.WorldTransform);
        return subscribedScene.ActiveCamera.WorldToViewportXYPosition(world, true, true);
    }

    private Vector2 GetBoneElementViewportPosition(BoneElementReference reference)
    {
        var head = GetBoneHeadViewportPosition(reference.Bone);
        var tail = GetBoneViewportPosition(reference.Bone);
        if (head.X == float.MaxValue || tail.X == float.MaxValue) return new Vector2(float.MaxValue);
        return reference.Element switch {
            BoneElement.Head => head,
            BoneElement.Tail => tail,
            _ => (head + tail) * 0.5f,
        };
    }

    //TO DO: needs to distinguish parts of bones better.
    private static float DistanceSquaredToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= float.Epsilon) return Vector2.DistanceSquared(point, start);
        var amount = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0.0f, 1.0f);
        return Vector2.DistanceSquared(point, start + segment * amount);
    }

    private void SelectVertex(Vector2 viewportPosition, bool extendSelection, bool toggleSelection)
    {
        if (subscribedScene == null) return;

        var camera = subscribedScene.ActiveCamera;
        var radiusSquared = vertexSelectionRadius * vertexSelectionRadius;
        var candidates = new List<(VertexReference vertex, Vector3 viewport, float distanceSquared)>();
        foreach (var vertex in EnumerateEditableVertices()) {
            var world = GetVertexWorldPosition(vertex, vertex.Buffer.Positions[vertex.Index]);
            var projected = camera.WorldToViewportPosition(world, true, true);
            if (projected.X == float.MaxValue) continue;
            var screen = new Vector2(projected.X, projected.Y);
            var distanceSquared = Vector2.DistanceSquared(screen, viewportPosition);
            if (distanceSquared <= radiusSquared) candidates.Add((vertex, projected, distanceSquared));
        }
        candidates.Sort(static (left, right) => left.distanceSquared.CompareTo(right.distanceSquared));

        var depth = ReadSelectionDepth(
            viewportPosition - new Vector2(vertexSelectionRadius),
            viewportPosition + new Vector2(vertexSelectionRadius));
        VertexReference? closest = null;
        foreach (var candidate in candidates) {
            if (!IsVertexVisible(candidate.viewport, depth)) continue;
            closest = candidate.vertex;
            break;
        }

        if (!extendSelection && !toggleSelection) selectedVertices.Clear();
        if (closest is not { } selected) return;
        if (toggleSelection) {
            if (!selectedVertices.Add(selected)) selectedVertices.Remove(selected);
        } else {
            selectedVertices.Add(selected);
        }
    }

    private void SelectVerticesInBox(Vector2 start, Vector2 end, bool extendSelection, bool toggleSelection)
    {
        if (subscribedScene == null) return;
        var min = Vector2.Min(start, end);
        var max = Vector2.Max(start, end);
        var camera = subscribedScene.ActiveCamera;
        var depth = ReadSelectionDepth(min, max);
        var boxVertices = new HashSet<VertexReference>();
        foreach (var vertex in EnumerateEditableVertices()) {
            var world = GetVertexWorldPosition(vertex, vertex.Buffer.Positions[vertex.Index]);
            var projected = camera.WorldToViewportPosition(world, true, true);
            if (projected.X == float.MaxValue || projected.X < min.X || projected.Y < min.Y || projected.X > max.X || projected.Y > max.Y) continue;
            if (IsVertexVisible(projected, depth)) boxVertices.Add(vertex);
        }

        if (!extendSelection && !toggleSelection) selectedVertices.Clear();
        if (toggleSelection) {
            foreach (var vertex in boxVertices) {
                if (!selectedVertices.Add(vertex)) selectedVertices.Remove(vertex);
            }
        } else {
            selectedVertices.UnionWith(boxVertices);
        }
    }

    private ViewportDepthRegion? ReadSelectionDepth(Vector2 min, Vector2 max)
    {
        if (subscribedScene?.RenderContext is not OpenGLRenderContext context) return null;
        var left = (int)MathF.Floor(min.X);
        var top = (int)MathF.Floor(min.Y);
        var right = (int)MathF.Ceiling(max.X) + 1;
        var bottom = (int)MathF.Ceiling(max.Y) + 1;
        return context.ReadViewportDepth(left, top, right - left, bottom - top);
    }

    private static bool IsVertexVisible(Vector3 viewportPosition, ViewportDepthRegion? depthRegion)
    {
        if (depthRegion is not { } depth || !depth.TryGetDepth(new Vector2(viewportPosition.X, viewportPosition.Y), out var surfaceDepth)) return false;
        var surfaceDeviceDepth = surfaceDepth * 2.0f - 1.0f;
        return viewportPosition.Z <= surfaceDeviceDepth + 0.0002f;
    }

    private IEnumerable<VertexReference> EnumerateEditableVertices()
    {
        var seen = new HashSet<VertexReference>();
        foreach (var context in Viewer.MeshContexts) {
            if (!context.GameObject.ShouldDraw) continue;
            var lod = context.MeshFile.NativeMesh.MeshData?.LODs.FirstOrDefault();
            var renderMeshes = context.Component.MeshHandle?.Meshes.ToArray();
            if (lod == null || renderMeshes == null) continue;

            var submeshIndex = 0;
            foreach (var group in lod.MeshGroups) {
                foreach (var submesh in group.Submeshes) {
                    var currentSubmeshIndex = submeshIndex++;
                    var renderMesh = renderMeshes.ElementAtOrDefault(currentSubmeshIndex);
                    if (renderMesh == null || !context.Component.MeshHandle!.GetMeshPartEnabled(renderMesh.MeshGroup)) continue;
                    if (context.Component.HiddenPreviewSubmeshIndices?.Contains(currentSubmeshIndex) == true) continue;
                    if (!selectedSubmeshes.Contains(new SubmeshReference(context, currentSubmeshIndex))) continue;
                    for (var localIndex = 0; localIndex < submesh.vertCount; localIndex++) {
                        var vertex = new VertexReference(context, submesh.Buffer, submesh.vertsIndexOffset + localIndex);
                        if (seen.Add(vertex)) yield return vertex;
                    }
                }
            }
        }
    }

    private IEnumerable<BoneElementReference> EnumerateEditableBoneElements()
    {
        foreach (var context in selectedArmatures) {
            if (hiddenArmatures.Contains(context) || !context.GameObject.ShouldDraw) continue;
            var bones = context.Mesh?.Bones?.Bones;
            if (bones == null) continue;
            foreach (var bone in bones) {
                var reference = new BoneReference(context, bone);
                yield return new BoneElementReference(reference, BoneElement.Head);
                yield return new BoneElementReference(reference, BoneElement.Body);
                yield return new BoneElementReference(reference, BoneElement.Tail);
            }
        }
    }

    private Dictionary<VertexReference, Vector3> BuildMirroredVertexMoveSet()
    {
        var result = selectedVertices.ToDictionary(vertex => vertex, _ => Vector3.One);
        var mirrorSigns = GetMirrorSigns().ToArray();
        if (mirrorSigns.Length == 0 || mirrorRadius <= 0) return result;

        var candidates = EnumerateEditableVertices()
            .Select(vertex => (reference: vertex, position: GetVertexWorldPosition(vertex, vertex.Buffer.Positions[vertex.Index])))
            .GroupBy(candidate => candidate.reference.Context)
            .ToDictionary(group => group.Key, group => BuildMirrorGrid(group, mirrorRadius));
        foreach (var source in selectedVertices) {
            if (!candidates.TryGetValue(source.Context, out var grid)) continue;
            var sourcePosition = GetVertexWorldPosition(source, source.Buffer.Positions[source.Index]);
            var origin = Vector3.Transform(Vector3.Zero, source.Context.GameObject.Transform.WorldTransform);
            foreach (var sign in mirrorSigns) {
                var expected = origin + (sourcePosition - origin) * sign;
                var match = FindClosestMirrorMatch(grid, expected, mirrorRadius, candidate => candidate.reference != source);
                if (match is { } counterpart) result.TryAdd(counterpart.reference, sign);
            }
        }
        return result;
    }

    private Dictionary<BoneReference, Vector3> BuildMirroredBoneMoveSet()
    {
        var result = selectedBoneElements.Select(element => element.Bone).Distinct().ToDictionary(bone => bone, _ => Vector3.One);
        var mirrorSigns = GetMirrorSigns().ToArray();
        if (mirrorSigns.Length == 0 || mirrorRadius <= 0) return result;

        var candidates = EnumerateEditableBoneElements()
            .Select(element => (reference: element, position: GetBoneElementWorldPosition(element)))
            .GroupBy(candidate => (candidate.reference.Bone.Context, candidate.reference.Element))
            .ToDictionary(group => group.Key, group => BuildMirrorGrid(group, mirrorRadius));
        foreach (var source in selectedBoneElements) {
            var key = (source.Bone.Context, source.Element);
            if (!candidates.TryGetValue(key, out var grid)) continue;
            var sourcePosition = GetBoneElementWorldPosition(source);
            var origin = Vector3.Transform(Vector3.Zero, source.Bone.Context.GameObject.Transform.WorldTransform);
            foreach (var sign in mirrorSigns) {
                var expected = origin + (sourcePosition - origin) * sign;
                var match = FindClosestMirrorMatch(grid, expected, mirrorRadius, candidate => candidate.reference.Bone != source.Bone);
                if (match is { } counterpart) result.TryAdd(counterpart.reference.Bone, sign);
            }
        }
        return result;
    }

    private IEnumerable<Vector3> GetMirrorSigns()
    {
        var axes = new List<int>(3);
        if (mirrorX) axes.Add(0);
        if (mirrorY) axes.Add(1);
        if (mirrorZ) axes.Add(2);
        for (var combination = 1; combination < 1 << axes.Count; combination++) {
            var sign = Vector3.One;
            for (var index = 0; index < axes.Count; index++) {
                if ((combination & 1 << index) == 0) continue;
                if (axes[index] == 0) sign.X = -1;
                else if (axes[index] == 1) sign.Y = -1;
                else sign.Z = -1;
            }
            yield return sign;
        }
    }

    private static Dictionary<MirrorGridKey, List<(T reference, Vector3 position)>> BuildMirrorGrid<T>(
        IEnumerable<(T reference, Vector3 position)> candidates, float cellSize) where T : struct
    {
        var grid = new Dictionary<MirrorGridKey, List<(T reference, Vector3 position)>>();
        foreach (var candidate in candidates) {
            var key = GetMirrorGridKey(candidate.position, cellSize);
            if (!grid.TryGetValue(key, out var cell)) grid[key] = cell = [];
            cell.Add(candidate);
        }
        return grid;
    }

    private static (T reference, Vector3 position)? FindClosestMirrorMatch<T>(
        Dictionary<MirrorGridKey, List<(T reference, Vector3 position)>> grid,
        Vector3 expected, float radius, Func<(T reference, Vector3 position), bool> predicate) where T : struct
    {
        var center = GetMirrorGridKey(expected, radius);
        var radiusSquared = radius * radius;
        (T reference, Vector3 position)? closest = null;
        var closestDistance = radiusSquared;
        for (var x = -1; x <= 1; x++) {
            for (var y = -1; y <= 1; y++) {
                for (var z = -1; z <= 1; z++) {
                    if (!grid.TryGetValue(new MirrorGridKey(center.X + x, center.Y + y, center.Z + z), out var cell)) continue;
                    foreach (var candidate in cell) {
                        if (!predicate(candidate)) continue;
                        var distance = Vector3.DistanceSquared(expected, candidate.position);
                        if (distance > closestDistance) continue;
                        closestDistance = distance;
                        closest = candidate;
                    }
                }
            }
        }
        return closest;
    }

    private static MirrorGridKey GetMirrorGridKey(Vector3 position, float cellSize) => new(
        (int)MathF.Floor(position.X / cellSize),
        (int)MathF.Floor(position.Y / cellSize),
        (int)MathF.Floor(position.Z / cellSize));

    private void BeginMove()
    {
        if (subscribedScene == null || selectedVertices.Count == 0 && selectedBoneElements.Count == 0) return;

        foreach (var context in selectedBoneElements.Select(element => element.Bone.Context).Distinct()) context.Animator?.Stop();
        moveVertexDeltaSigns = selectedVertices.Count > 0 ? BuildMirroredVertexMoveSet() : null;
        moveBoneDeltaSigns = selectedBoneElements.Count > 0 ? BuildMirroredBoneMoveSet() : null;
        moveOriginalPositions = moveVertexDeltaSigns != null
            ? moveVertexDeltaSigns.Keys.ToDictionary(vertex => vertex, vertex => vertex.Buffer.Positions[vertex.Index])
            : null;
        moveOriginalBoneTransforms = moveBoneDeltaSigns != null
            ? CaptureBoneTransforms(moveBoneDeltaSigns.Keys.Select(bone => bone.Context).Distinct())
            : null;
        moveAnchorWorld = Vector3.Zero;
        var anchorCount = 0;
        if (moveOriginalPositions != null) {
            foreach (var vertex in selectedVertices) {
                moveAnchorWorld += GetVertexWorldPosition(vertex, moveOriginalPositions[vertex]);
                anchorCount++;
            }
        }
        foreach (var element in selectedBoneElements) {
            moveAnchorWorld += GetBoneElementWorldPosition(element);
            anchorCount++;
        }
        moveAnchorWorld /= anchorCount;
        moveStartScreen = subscribedScene.Mouse.MouseScreenPosition;
        moveStartWorld = subscribedScene.ActiveCamera.ScreenToWorldPositionReproject(moveStartScreen, moveAnchorWorld);
        moveConstraint = MoveConstraint.None;
    }

    private void UpdateMovePreview()
    {
        if (subscribedScene == null || !IsMoving) return;
        var worldDelta = GetConstrainedMoveDelta(subscribedScene.Mouse.MouseScreenPosition);
        if (moveOriginalPositions != null) ApplyMovedPositions(moveOriginalPositions, worldDelta);
        if (moveOriginalBoneTransforms != null) ApplyMovedBones(moveOriginalBoneTransforms, worldDelta);
    }

    private void UpdateMoveConstraint()
    {
        var excludeAxis = ImGui.IsKeyDown(ImGuiKey.ModShift);
        if (ImGui.IsKeyPressed(ImGuiKey.X)) moveConstraint = excludeAxis ? MoveConstraint.ExceptX : MoveConstraint.X;
        if (ImGui.IsKeyPressed(ImGuiKey.Y)) moveConstraint = excludeAxis ? MoveConstraint.ExceptY : MoveConstraint.Y;
        if (ImGui.IsKeyPressed(ImGuiKey.Z)) moveConstraint = excludeAxis ? MoveConstraint.ExceptZ : MoveConstraint.Z;
    }

    private Vector3 GetConstrainedMoveDelta(Vector2 currentScreen)
    {
        if (subscribedScene == null) return Vector3.Zero;
        var camera = subscribedScene.ActiveCamera;
        var currentWorld = camera.ScreenToWorldPositionReproject(currentScreen, moveAnchorWorld);
        var viewPlaneDelta = currentWorld - moveStartWorld;
        return moveConstraint switch {
            MoveConstraint.X => GetAxisMoveDelta(Vector3.UnitX, currentScreen, viewPlaneDelta.X),
            MoveConstraint.Y => GetAxisMoveDelta(Vector3.UnitY, currentScreen, viewPlaneDelta.Y),
            MoveConstraint.Z => GetAxisMoveDelta(Vector3.UnitZ, currentScreen, viewPlaneDelta.Z),
            MoveConstraint.ExceptX => new Vector3(0, viewPlaneDelta.Y, viewPlaneDelta.Z),
            MoveConstraint.ExceptY => new Vector3(viewPlaneDelta.X, 0, viewPlaneDelta.Z),
            MoveConstraint.ExceptZ => new Vector3(viewPlaneDelta.X, viewPlaneDelta.Y, 0),
            _ => viewPlaneDelta,
        };
    }

    private Vector3 GetAxisMoveDelta(Vector3 axis, Vector2 currentScreen, float fallbackAmount)
    {
        if (subscribedScene == null) return axis * fallbackAmount;
        var camera = subscribedScene.ActiveCamera;
        var anchorScreen = camera.WorldToScreenPosition(moveAnchorWorld, false, false);
        var axisScreen = camera.WorldToScreenPosition(moveAnchorWorld + axis, false, false) - anchorScreen;
        var axisLengthSquared = axisScreen.LengthSquared();
        if (axisLengthSquared < 0.0001f || !float.IsFinite(axisLengthSquared)) return axis * fallbackAmount;
        var amount = Vector2.Dot(currentScreen - moveStartScreen, axisScreen) / axisLengthSquared;
        return axis * amount;
    }

    private string GetMoveStatus()
    {
        var constraint = moveConstraint switch {
            MoveConstraint.X => " — Global X",
            MoveConstraint.Y => " — Global Y",
            MoveConstraint.Z => " — Global Z",
            MoveConstraint.ExceptX => " — Global YZ",
            MoveConstraint.ExceptY => " — Global XZ",
            MoveConstraint.ExceptZ => " — Global XY",
            _ => string.Empty,
        };
        return Lang.MeshViewer.Editor_Moving.String + constraint;
    }

    private void ApplyMovedPositions(IReadOnlyDictionary<VertexReference, Vector3> originals, Vector3 worldDelta)
    {
        foreach (var (vertex, original) in originals) {
            var deltaSign = moveVertexDeltaSigns?.GetValueOrDefault(vertex, Vector3.One) ?? Vector3.One;
            var world = GetVertexWorldPosition(vertex, original) + worldDelta * deltaSign;
            if (!Matrix4x4.Invert(vertex.Context.GameObject.Transform.WorldTransform, out var inverse)) continue;
            var posedLocal = Vector3.Transform(world, inverse);
            if (TryGetSkinMatrix(vertex, out var skinMatrix) && Matrix4x4.Invert(skinMatrix, out var inverseSkin)) {
                vertex.Buffer.Positions[vertex.Index] = Vector3.Transform(posedLocal, inverseSkin);
            } else {
                vertex.Buffer.Positions[vertex.Index] = posedLocal;
            }
        }
        RefreshEditedRenderMeshes(originals.Keys.Select(vertex => vertex.Context).Distinct());
    }

    private Dictionary<BoneReference, BoneTransformState> CaptureBoneTransforms(IEnumerable<MeshViewerContext> contexts)
    {
        var states = new Dictionary<BoneReference, BoneTransformState>();
        foreach (var context in contexts) {
            var bones = context.Mesh?.Bones?.Bones;
            if (bones == null) continue;
            foreach (var bone in bones) {
                states[new BoneReference(context, bone)] = new BoneTransformState(
                    bone.localTransform.ToSystem(),
                    bone.globalTransform.ToSystem(),
                    bone.inverseGlobalTransform.ToSystem());
            }
        }
        return states;
    }

    private void ApplyMovedBones(IReadOnlyDictionary<BoneReference, BoneTransformState> originals, Vector3 worldDelta)
    {
        ApplyBoneTransforms(originals, false);
        var movedBones = moveBoneDeltaSigns ?? selectedBoneElements.Select(element => element.Bone).Distinct().ToDictionary(bone => bone, _ => Vector3.One);
        foreach (var context in movedBones.Keys.Select(bone => bone.Context).Distinct()) {
            if (!Matrix4x4.Invert(context.GameObject.Transform.WorldTransform, out var inverseWorld)) continue;

            foreach (var selected in movedBones.Keys.Where(bone => bone.Context == context)) {
                var global = originals[selected].Global;
                global.Translation += Vector3.TransformNormal(worldDelta * movedBones[selected], inverseWorld);
                selected.Bone.globalTransform = global;
            }

            // Every unselected bone keeps its original global transform. Rebuilding all local
            // transforms from those globals prevents a moved parent from carrying its children.
            var bones = context.Mesh?.Bones?.Bones;
            if (bones == null) continue;
            foreach (var bone in bones) {
                var global = bone.globalTransform.ToSystem();
                if (bone.Parent != null && Matrix4x4.Invert(bone.Parent.globalTransform.ToSystem(), out var inverseParent)) {
                    bone.localTransform = global * inverseParent;
                } else {
                    bone.localTransform = global;
                }
                if (Matrix4x4.Invert(global, out var inverseGlobal)) bone.inverseGlobalTransform = inverseGlobal;
            }
            UpdateArmatureMatrices(context);
        }
    }

    private static Vector3 GetBoneWorldPosition(BoneReference reference)
    {
        var mesh = reference.Context.Mesh;
        var local = mesh != null && (uint)reference.Bone.index < (uint)mesh.BoneMatrices.Length
            ? mesh.BoneMatrices[reference.Bone.index].Translation
            : reference.Bone.globalTransform.ToSystem().Translation;
        return Vector3.Transform(local, reference.Context.GameObject.Transform.WorldTransform);
    }

    private static Vector3 GetBoneElementWorldPosition(BoneElementReference reference)
    {
        var tail = GetBoneWorldPosition(reference.Bone);
        var head = reference.Bone.Bone.Parent != null
            ? GetBoneWorldPosition(new BoneReference(reference.Bone.Context, reference.Bone.Bone.Parent))
            : Vector3.Transform(Vector3.Zero, reference.Bone.Context.GameObject.Transform.WorldTransform);
        return reference.Element switch {
            BoneElement.Head => head,
            BoneElement.Tail => tail,
            _ => (head + tail) * 0.5f,
        };
    }

    private static void UpdateArmatureMatrices(MeshViewerContext context)
    {
        var mesh = context.Mesh;
        var hierarchy = mesh?.Bones;
        if (mesh == null || hierarchy == null) return;
        if (mesh.BoneMatrices.Length != hierarchy.Bones.Count) mesh.BoneMatrices = new Matrix4x4[hierarchy.Bones.Count];
        if (mesh.DeformBoneMatrices.Length != hierarchy.DeformBones.Count) mesh.DeformBoneMatrices = new Matrix4x4[hierarchy.DeformBones.Count];
        foreach (var bone in hierarchy.Bones) {
            var global = bone.globalTransform.ToSystem();
            mesh.BoneMatrices[bone.index] = global;
            if ((uint)bone.remapIndex < (uint)mesh.DeformBoneMatrices.Length) {
                mesh.DeformBoneMatrices[bone.remapIndex] = bone.inverseGlobalTransform.ToSystem() * global;
            }
        }
    }

    private static void ApplyBoneTransforms(IReadOnlyDictionary<BoneReference, BoneTransformState> states, bool markModified = true)
    {
        foreach (var (reference, state) in states) {
            reference.Bone.localTransform = state.Local;
            reference.Bone.globalTransform = state.Global;
            reference.Bone.inverseGlobalTransform = state.InverseGlobal;
        }
        var contexts = states.Keys.Select(reference => reference.Context).Distinct().ToArray();
        foreach (var context in contexts) {
            if (markModified) context.Handle.Modified = true;
            UpdateArmatureMatrices(context);
        }
    }

    private static Vector3 GetVertexWorldPosition(VertexReference vertex, Vector3 sourcePosition)
    {
        var local = TryGetSkinMatrix(vertex, out var skinMatrix)
            ? Vector3.Transform(sourcePosition, skinMatrix)
            : sourcePosition;
        return Vector3.Transform(local, vertex.Context.GameObject.Transform.WorldTransform);
    }

    private static bool TryGetSkinMatrix(VertexReference vertex, out Matrix4x4 matrix)
    {
        matrix = Matrix4x4.Identity;
        var animatedMesh = vertex.Context.Mesh;
        if (animatedMesh == null || animatedMesh.DeformBoneMatrices.Length == 0
            || (uint)vertex.Index >= (uint)vertex.Buffer.Weights.Length) return false;

        var weights = vertex.Buffer.Weights[vertex.Index];
        matrix = new Matrix4x4();
        var weightScale = weights.IndexCount == 6 ? 1.0f + weights.GetWeight(6) + weights.GetWeight(7) : 1.0f;
        var hasWeight = false;
        for (var index = 0; index < weights.IndexCount; index++) {
            var weight = weights.GetWeight(index) * weightScale;
            var boneIndex = weights.GetIndex(index);
            if (weight == 0 || (uint)boneIndex >= (uint)animatedMesh.DeformBoneMatrices.Length) continue;
            matrix += Matrix4x4.Multiply(animatedMesh.DeformBoneMatrices[boneIndex], weight);
            hasWeight = true;
        }
        return hasWeight;
    }

    private void ApplyPositions(IReadOnlyDictionary<VertexReference, Vector3> positions, bool markModified = true)
    {
        foreach (var (vertex, position) in positions) vertex.Buffer.Positions[vertex.Index] = position;
        var contexts = positions.Keys.Select(vertex => vertex.Context).Distinct().ToArray();
        if (markModified) {
            foreach (var context in contexts) context.Handle.Modified = true;
        }
        RefreshEditedRenderMeshes(contexts);
    }

    private static void RefreshEditedRenderMeshes(IEnumerable<MeshViewerContext> contexts)
    {
        foreach (var context in contexts) {
            var lod = context.MeshFile.NativeMesh.MeshData?.LODs.FirstOrDefault();
            var renderMeshes = context.Component.MeshHandle?.Meshes.ToArray();
            if (lod == null || renderMeshes == null) continue;

            var submeshIndex = 0;
            foreach (var group in lod.MeshGroups) {
                foreach (var submesh in group.Submeshes) {
                    renderMeshes.ElementAtOrDefault(submeshIndex++)?.UpdateVertexPositions(submesh.Positions);
                }
            }
        }
    }

    private void CommitMove()
    {
        if (!IsMoving) return;
        var originalPositions = moveOriginalPositions;
        var originalBones = moveOriginalBoneTransforms;
        var movedPositions = originalPositions?.Keys.ToDictionary(vertex => vertex, vertex => vertex.Buffer.Positions[vertex.Index]);
        var movedBones = originalBones == null ? null : CaptureBoneTransforms(originalBones.Keys.Select(bone => bone.Context).Distinct());
        moveOriginalPositions = null;
        moveOriginalBoneTransforms = null;
        moveVertexDeltaSigns = null;
        moveBoneDeltaSigns = null;
        moveConstraint = MoveConstraint.None;

        ApplyMoveState(originalPositions, originalBones, false);
        UndoRedo.RecordCallback(null,
            () => ApplyMoveState(movedPositions, movedBones),
            () => ApplyMoveState(originalPositions, originalBones),
            $"MeshGeometryMove_{Viewer.GetHashCode()}");
    }

    private void CancelMove()
    {
        if (!IsMoving) return;
        var originalPositions = moveOriginalPositions;
        var originalBones = moveOriginalBoneTransforms;
        moveOriginalPositions = null;
        moveOriginalBoneTransforms = null;
        moveVertexDeltaSigns = null;
        moveBoneDeltaSigns = null;
        moveConstraint = MoveConstraint.None;
        ApplyMoveState(originalPositions, originalBones, false);
    }

    private void ApplyMoveState(
        IReadOnlyDictionary<VertexReference, Vector3>? positions,
        IReadOnlyDictionary<BoneReference, BoneTransformState>? bones,
        bool markModified = true)
    {
        if (positions != null) ApplyPositions(positions, markModified);
        if (bones != null) ApplyBoneTransforms(bones, markModified);
    }

    private void DrawArmatures(Vector2 viewportPosition, Vector2 viewportSize)
    {
        if (subscribedScene == null) return;

        var drawList = ImGui.GetWindowDrawList();
        var camera = subscribedScene.ActiveCamera;
        var lineColor = ImGui.GetColorU32(new Vector4(0.78f, 0.78f, 0.78f, 0.9f));
        var dotColor = ImGui.GetColorU32(new Vector4(0.86f, 0.86f, 0.86f, 1.0f));
        var selectedColor = ImGui.GetColorU32(new Vector4(1.0f, 0.55f, 0.08f, 1.0f));
        var lineThickness = Math.Clamp(2.5f * UI.UIScale, 2.5f, 7.0f);

        drawList.PushClipRect(viewportPosition, viewportPosition + viewportSize, true);
        foreach (var context in Viewer.MeshContexts) {
            var mesh = context.Mesh;
            var bones = mesh?.Bones?.Bones;
            if (mesh == null || bones == null || bones.Count == 0 || hiddenArmatures.Contains(context) || !context.GameObject.ShouldDraw) continue;
            if (interactionMode == EditorInteractionMode.Edit && !selectedArmatures.Contains(context)) continue;
            var armatureSelected = interactionMode == EditorInteractionMode.Object && selectedArmatures.Contains(context);
            var armatureLineColor = armatureSelected ? selectedColor : lineColor;
            var armatureDotColor = armatureSelected ? selectedColor : dotColor;
            var dotRadius = Math.Clamp(GetAdaptiveVertexPointSize(context) * 0.65f, 3.5f, 10.0f);

            var requiredLength = Math.Max(bones.Count, bones.Max(bone => bone.index) + 1);
            if (!armatureScreenPositions.TryGetValue(context, out var screenPositions) || screenPositions.Length < requiredLength) {
                armatureScreenPositions[context] = screenPositions = new Vector2[requiredLength];
            }

            foreach (var bone in bones) {
                var localTransform = (uint)bone.index < (uint)mesh.BoneMatrices.Length
                    ? mesh.BoneMatrices[bone.index]
                    : bone.globalTransform.ToSystem();
                var worldPosition = Vector3.Transform(localTransform.Translation, context.GameObject.Transform.WorldTransform);
                screenPositions[bone.index] = camera.WorldToScreenPosition(worldPosition, false, true);
            }

            var rootPosition = camera.WorldToScreenPosition(
                Vector3.Transform(Vector3.Zero, context.GameObject.Transform.WorldTransform), false, true);
            foreach (var bone in bones) {
                var head = bone.parentIndex >= 0 && (uint)bone.parentIndex < (uint)screenPositions.Length
                    ? screenPositions[bone.parentIndex]
                    : rootPosition;
                var tail = screenPositions[bone.index];
                if (head.X == float.MaxValue || tail.X == float.MaxValue) continue;
                drawList.AddLine(head, tail, armatureLineColor, armatureSelected ? lineThickness + 1.0f : lineThickness);
                drawList.AddCircleFilled(head, dotRadius, armatureDotColor);
                drawList.AddCircleFilled(tail, dotRadius, armatureDotColor);
            }

            foreach (var element in selectedBoneElements.Where(element => element.Bone.Context == context)) {
                var bone = element.Bone.Bone;
                var head = bone.parentIndex >= 0 && (uint)bone.parentIndex < (uint)screenPositions.Length
                    ? screenPositions[bone.parentIndex]
                    : rootPosition;
                var tail = screenPositions[bone.index];
                if (head.X == float.MaxValue || tail.X == float.MaxValue) continue;
                switch (element.Element) {
                    case BoneElement.Head:
                        drawList.AddCircleFilled(head, dotRadius + 1.0f, selectedColor);
                        break;
                    case BoneElement.Body:
                        drawList.AddLine(head, tail, selectedColor, lineThickness + 1.5f);
                        break;
                    case BoneElement.Tail:
                        drawList.AddCircleFilled(tail, dotRadius + 1.0f, selectedColor);
                        break;
                }
            }
        }
        drawList.PopClipRect();
    }

    private void DrawSelectedVertices(Vector2 viewportPosition, Vector2 viewportSize)
    {
        if (subscribedScene == null || selectedVertices.Count == 0) return;
        var drawList = ImGui.GetWindowDrawList();
        var camera = subscribedScene.ActiveCamera;
        var color = ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.15f, 1.0f));
        var outlineColor = 0xff000000;
        var radii = selectedVertices.Select(vertex => vertex.Context).Distinct()
            .ToDictionary(context => context, context => Math.Max(GetAdaptiveVertexPointSize(context) + 1.0f, 2.0f));
        foreach (var vertex in selectedVertices) {
            var radius = radii[vertex.Context];
            var world = GetVertexWorldPosition(vertex, vertex.Buffer.Positions[vertex.Index]);
            var screen = camera.WorldToScreenPosition(world, true, true);
            if (screen.X == float.MaxValue || screen.X < viewportPosition.X || screen.Y < viewportPosition.Y
                || screen.X > viewportPosition.X + viewportSize.X || screen.Y > viewportPosition.Y + viewportSize.Y) continue;
            drawList.AddRectFilled(screen - new Vector2(radius + 1.0f), screen + new Vector2(radius + 1.0f), outlineColor);
            drawList.AddRectFilled(screen - new Vector2(radius), screen + new Vector2(radius), color);
        }
    }

    private void DrawBoxSelection(Vector2 viewportPosition)
    {
        if (!boxSelecting) return;
        var min = viewportPosition + Vector2.Min(boxSelectStart, boxSelectEnd);
        var max = viewportPosition + Vector2.Max(boxSelectStart, boxSelectEnd);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.25f, 0.55f, 1.0f, 0.12f)));
        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(0.45f, 0.72f, 1.0f, 1.0f)), 0, ImDrawFlags.None, 1.5f);
    }

    private float GetAdaptiveVertexPointSize(MeshViewerContext context)
    {
        if (subscribedScene == null) return vertexPointSize;
        var bounds = context.GameObject.GetWorldSpaceBounds();
        var objectSize = Math.Max(bounds.Size.Length(), 0.0001f);
        float scale;
        if (subscribedScene.ActiveCamera.ProjectionMode == CameraProjection.Orthographic) {
            scale = objectSize / Math.Max(subscribedScene.ActiveCamera.OrthoSize, 0.0001f);
        } else {
            scale = objectSize / Math.Max(Vector3.Distance(subscribedScene.ActiveCamera.Transform.Position, bounds.Center), 0.0001f);
        }
        return vertexPointSize * Math.Clamp(0.72f + scale * 0.6f, 0.72f, 1.5f);
    }

    private void OpenOptions()
    {
        if (optionsWindow == null) {
            optionsWindow = new MeshEditorOptionsWindow(this);
            optionsWindowData = EditorWindow.CurrentWindow?.AddSubwindow(optionsWindow);
            if (optionsWindowData != null) optionsWindowData.Size = new Vector2(460.0f * UI.UIScale, 250.0f * UI.UIScale);
        } else if (optionsWindowData != null) {
            ImGui.SetWindowFocus(optionsWindowData.Name);
        }
    }

    private void CloseOptions()
    {
        if (optionsWindowData?.ParentWindow is WindowBase owner) owner.CloseSubwindow(optionsWindowData);
        optionsWindow = null;
        optionsWindowData = null;
    }

    private sealed class MeshEditorOptionsWindow(MeshEditor editor) : IWindowHandler
    {
        public string HandlerName => Lang.MeshViewer.Editor_OptionsTitle.String;
        public bool HasUnsavedChanges => false;

        private WindowData data = null!;
        private bool shown;
        private bool receivedFocus;

        public void Init(UIContext context)
        {
            data = context.Get<WindowData>();
        }

        public unsafe void OnWindow()
        {
            if (!shown) ImGui.SetNextWindowFocus();
            if (!ImguiHelpers.BeginWindow(data, flags: ImGuiWindowFlags.NoDocking)) {
                editor.CloseOptions();
                return;
            }

            var focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
            if (SliderFloatWithReset(Lang.MeshViewer.Editor_VertexSize.String, ref editor.vertexPointSize, 1.0f, 32.0f, "%.1f px", MeshViewerSettings.DefaultEditorVertexSize, "VertexSize")) {
                AppConfig.Settings.MeshViewer.EditorVertexSize = editor.vertexPointSize;
                AppConfig.Settings.Save();
            }
            if (SliderFloatWithReset(Lang.MeshViewer.Editor_SelectionRadius.String, ref editor.vertexSelectionRadius, 3.0f, 100.0f, "%.0f px", MeshViewerSettings.DefaultEditorVertexSelectionRadius, "SelectionRadius")) {
                AppConfig.Settings.MeshViewer.EditorVertexSelectionRadius = editor.vertexSelectionRadius;
                AppConfig.Settings.Save();
            }
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Lang.MeshViewer.Editor_MirrorAxes);
            ImGui.SameLine();
            var mirrorChanged = ImGui.Checkbox("X##MirrorAxis", ref editor.mirrorX);
            ImGui.SameLine();
            mirrorChanged |= ImGui.Checkbox("Y##MirrorAxis", ref editor.mirrorY);
            ImGui.SameLine();
            mirrorChanged |= ImGui.Checkbox("Z##MirrorAxis", ref editor.mirrorZ);
            if (mirrorChanged) {
                AppConfig.Settings.MeshViewer.EditorMirrorX = editor.mirrorX;
                AppConfig.Settings.MeshViewer.EditorMirrorY = editor.mirrorY;
                AppConfig.Settings.MeshViewer.EditorMirrorZ = editor.mirrorZ;
                AppConfig.Settings.Save();
            }
            if (SliderFloatWithReset(Lang.MeshViewer.Editor_MirrorRadius.String, ref editor.mirrorRadius, 0.0001f, 1.0f, "%.4f", MeshViewerSettings.DefaultEditorMirrorRadius, "MirrorRadius", ImGuiSliderFlags.Logarithmic)) {
                AppConfig.Settings.MeshViewer.EditorMirrorRadius = editor.mirrorRadius;
                AppConfig.Settings.Save();
            }
            ImGui.Separator();
            if (ImGui.Checkbox(Lang.MeshViewer.Editor_StayOnTop, ref editor.optionsStayOnTop)) {
                AppConfig.Settings.MeshViewer.EditorOptionsStayOnTop = editor.optionsStayOnTop;
                AppConfig.Settings.Save();
            }

            if (editor.optionsStayOnTop) {
                ImGuiP.BringWindowToDisplayFront(ImGuiP.FindWindowByName(data.Name));
            }
            ImGui.End();

            shown = true;
            receivedFocus |= focused;
            if (!editor.optionsStayOnTop && receivedFocus && !focused) editor.CloseOptions();
        }

        private static bool SliderFloatWithReset(string label, ref float value, float minimum, float maximum, string format, float defaultValue, string id, ImGuiSliderFlags flags = ImGuiSliderFlags.None)
        {
            var showReset = Math.Abs(value - defaultValue) > 0.0001f;
            if (showReset) {
                var resetWidth = ImGui.CalcTextSize($"{AppIcons.SI_Reset}").X + ImGui.GetStyle().FramePadding.X * 2.0f;
                ImGui.SetNextItemWidth(Math.Max(1.0f, ImGui.CalcItemWidth() - resetWidth - ImGui.GetStyle().ItemSpacing.X));
            }

            var changed = ImGui.SliderFloat(label, ref value, minimum, maximum, format, flags);
            if (showReset) {
                ImGui.SameLine();
                if (ImGui.Button($"{AppIcons.SI_Reset}##Reset{id}")) {
                    value = defaultValue;
                    changed = true;
                }
                ImguiHelpers.Tooltip("Reset to default"u8);
            }
            return changed;
        }

        public void OnIMGUI() { }
        public bool RequestClose() => false;

        public void OnClosed()
        {
            if (editor.optionsWindow == this) {
                editor.optionsWindow = null;
                editor.optionsWindowData = null;
            }
        }
    }

    private void ApplyRenderState()
    {
        foreach (var context in Viewer.MeshContexts) {
            context.Component.PreviewDisplayMode = DisplayMode;
            var selectedIndices = selectedSubmeshes
                .Where(submesh => submesh.Context == context)
                .Select(submesh => submesh.Index)
                .ToHashSet();
            context.Component.HiddenPreviewSubmeshIndices = IsEnabled
                ? hiddenSubmeshes.Where(submesh => submesh.Context == context).Select(submesh => submesh.Index).ToHashSet()
                : null;
            context.Component.HighlightedSubmeshIndices = IsEnabled && interactionMode == EditorInteractionMode.Object ? selectedIndices : null;
            var editMode = IsEnabled && interactionMode == EditorInteractionMode.Edit;
            context.Component.EditSubmeshIndices = editMode ? selectedIndices : null;
            context.Component.EditWireframeOverlay = editMode;
            context.Component.ShowEditVertices = editMode && selectedIndices.Count > 0;
            context.Component.EditVertexPointSize = GetAdaptiveVertexPointSize(context);
        }
    }

    private static string GetSubmeshLabel(MeshViewerContext context, int meshIndex, int meshGroup)
    {
        var nativeMesh = context.MeshFile.NativeMesh;
        var lod = nativeMesh.MeshData?.LODs.FirstOrDefault();
        if (lod != null) {
            var flattenedIndex = 0;
            foreach (var group in lod.MeshGroups) {
                for (int submeshIndex = 0; submeshIndex < group.Submeshes.Count; submeshIndex++) {
                    var submesh = group.Submeshes[submeshIndex];
                    if (flattenedIndex++ != meshIndex) continue;
                    var materialName = nativeMesh.MaterialNames.ElementAtOrDefault(submesh.materialIndex);
                    return string.IsNullOrEmpty(materialName)
                        ? $"Submesh {meshIndex}  |  Group {group.groupId}"
                        : $"Submesh {meshIndex}  |  Group {group.groupId}  |  {materialName}";
                }
            }
        }
        return $"Submesh {meshIndex}  |  Group {meshGroup}";
    }

    //This is because for higher res displays it tends to not fit well otherwise
    private float GetContentWidth()
    {
        var width = ImGui.CalcTextSize(Lang.MeshViewer.Editor_Submeshes).X;
        foreach (var context in Viewer.MeshContexts) {
            width = Math.Max(width, ImGui.CalcTextSize(context.ShortName).X);
            if (context.Mesh?.Bones?.Bones.Count > 0) {
                width = Math.Max(width, ImGui.CalcTextSize($"{AppIcons.SI_FileType_FBXSKEL} {Lang.MeshViewer.Armature}").X);
            }
            var meshes = context.Component.MeshHandle?.Meshes;
            if (meshes == null) continue;
            var meshIndex = 0;
            foreach (var mesh in meshes) {
                width = Math.Max(width, ImGui.CalcTextSize(GetSubmeshLabel(context, meshIndex, mesh.MeshGroup)).X);
                meshIndex++;
            }
        }
        var style = ImGui.GetStyle();
        return width + ImGui.GetFrameHeight() + style.ItemSpacing.X
            + style.WindowPadding.X * 2.0f + style.FramePadding.X * 2.0f + style.ScrollbarSize;
    }

    public void Dispose()
    {
        CancelMove();
        CloseOptions();
        IsEnabled = false;
        DisplayMode = MeshDisplayMode.Default;
        ClearAllSelection();
        ApplyRenderState();
        if (subscribedScene != null) {
            subscribedScene.Mouse.Pressed -= OnScenePressed;
            subscribedScene.Mouse.Clicked -= OnSceneClicked;
            subscribedScene.Mouse.DoubleClicked -= OnSceneDoubleClicked;
            subscribedScene.Mouse.Dragging -= OnSceneDragging;
            subscribedScene.Mouse.StopDragging -= OnSceneStopDragging;
        }
        subscribedScene = null;
    }
}
