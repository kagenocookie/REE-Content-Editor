using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ReeLib;
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
    public MeshDisplayMode DisplayMode { get; private set; }
    public EditorInteractionMode interactionMode = EditorInteractionMode.Object;
    public GeometrySelectionMode geometrySelectionMode = GeometrySelectionMode.Vertex;
    public enum EditorInteractionMode
    {
        Object,
        Edit,
    }

    public enum GeometrySelectionMode
    {
        Vertex,
        Edge,
        Face,
    }

    private Scene? subscribedScene;
    internal readonly HashSet<SubmeshReference> selectedSubmeshes = [];
    internal readonly HashSet<MeshViewerContext> selectedArmatures = [];
    internal readonly HashSet<SubmeshReference> hiddenSubmeshes = [];
    internal readonly HashSet<MeshViewerContext> hiddenArmatures = [];
    private readonly Dictionary<MeshViewerContext, Vector2[]> armatureScreenPositions = [];
    private readonly Dictionary<MeshViewerContext, SubmeshCache> submeshCache = [];
    internal SubmeshReference? submeshSelectionAnchor;
    internal SubmeshReference? scrollToSubmesh;
    private readonly HashSet<VertexReference> selectedVertices = [];
    private readonly HashSet<EdgeReference> selectedEdges = [];
    private readonly HashSet<FaceReference> selectedFaces = [];
    private readonly HashSet<BoneElementReference> selectedBoneElements = [];
    private Dictionary<VertexReference, Vector3>? moveOriginalPositions;
    private Dictionary<BoneReference, BoneTransformState>? moveOriginalBoneTransforms;
    private Dictionary<VertexReference, Vector3>? moveVertexDeltaSigns;
    private Dictionary<BoneReference, Vector3>? moveBoneDeltaSigns;
    private Vector3 moveAnchorWorld;
    private Vector3 moveStartWorld;
    private Vector2 moveStartScreen;
    private Vector3? lastMovePreviewDelta;
    private MoveConstraint moveConstraint;
    private bool extendVertexSelection;
    private bool toggleVertexSelection;
    private Vector2 boxSelectStart;
    private Vector2 boxSelectEnd;
    private bool boxSelecting;
    private bool shiftSubmeshSelection;
    private bool ctrlSubmeshSelection;
    private bool suppressNextSceneClick;
    private WindowData? optionsWindowData;
    public float vertexPointSize = AppConfig.Settings.MeshViewer.EditorVertexSize;
    public float vertexSelectionRadius = AppConfig.Settings.MeshViewer.EditorVertexSelectionRadius;
    public bool mirrorX = AppConfig.Settings.MeshViewer.EditorMirrorX;
    public bool mirrorY = AppConfig.Settings.MeshViewer.EditorMirrorY;
    public bool mirrorZ = AppConfig.Settings.MeshViewer.EditorMirrorZ;
    public float mirrorRadius = AppConfig.Settings.MeshViewer.EditorMirrorRadius;
    internal OutlinerHoverState hoveredInOutliner = OutlinerHoverState.None;
    private bool renderStateDirty;
    internal readonly record struct SubmeshReference(MeshViewerContext Context, int Index);
    private readonly record struct VertexReference(MeshViewerContext Context, MeshBuffer Buffer, int Index);
    private readonly record struct EdgeReference(VertexReference First, VertexReference Second);
    private readonly record struct FaceReference(MeshViewerContext Context, int SubmeshIndex, int TriangleIndex);
    private readonly record struct FaceVertices(FaceReference Reference, VertexReference First, VertexReference Second, VertexReference Third);
    private readonly record struct BoneReference(MeshViewerContext Context, MeshBone Bone);
    private readonly record struct BoneElementReference(BoneReference Bone, BoneElement Element);
    private readonly record struct BoneHit(BoneElementReference Reference, float DistanceSquared);
    private readonly record struct BoneTransformState(Matrix4x4 Local, Matrix4x4 Global, Matrix4x4 InverseGlobal);
    private readonly record struct MirrorGridKey(int X, int Y, int Z);
    private sealed record VisibilityState(HashSet<SubmeshReference> Submeshes, HashSet<MeshViewerContext> Armatures);
    internal readonly record struct SubmeshLabel(byte[] UTF8);
    internal sealed record SubmeshCache(object NativeMesh, SubmeshLabel[] Labels, Submesh[] Submeshes);

    private enum BoneElement
    {
        Head,
        Body,
        Tail,
    }

    private bool IsMoving => moveOriginalPositions != null || moveOriginalBoneTransforms != null;
    private bool HasSelectedGeometry => selectedVertices.Count > 0 || selectedEdges.Count > 0 || selectedFaces.Count > 0;

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

    public void ShowMeshEditorButton(MeshViewerContext context)
    {
        EnsureSceneSubscription();
        if (renderStateDirty) ApplyRenderState();
        else UpdateEditVertexPointSizes();
        
        ImGui.PushStyleColor(ImGuiCol.Button, IsEnabled ? ImguiHelpers.GetColor(ImGuiCol.TabSelected) with { W = 0.25f } : Vector4.Zero);
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_MeshEditor, [Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary, Colors.IconSecondary, Colors.IconSecondary], null, Lang.MeshViewer.Menu_Editor.String)) {
            SetEnabled(!IsEnabled);
        }
        ImGui.PopStyleColor();
    }

    public bool ShowViewportModeControls(Vector2 viewportPosition, Vector2 viewportSize)
    {
        if (!IsEnabled) return false;

        var viewportHovered = ImGui.IsMouseHoveringRect(viewportPosition, viewportPosition + viewportSize);
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || viewportHovered) {
            //var keyboard = EditorWindow.CurrentWindow?.LastKeyboard;
            var selectAllPressed = AppConfig.Instance.Key_MeshViewer_SelectAll.Get().IsPressed();
            if (interactionMode == EditorInteractionMode.Object) {
                if (selectAllPressed) ToggleAllObjects();
                if (AppConfig.Instance.Key_Scene_Hide.Get().IsPressed()) HideSelectedObjects();
                if (AppConfig.Instance.Key_Scene_UnhideAll.Get().IsPressed()) UnhideAllObjects();
            }
            if (interactionMode == EditorInteractionMode.Edit) {
                if (!IsMoving) {
                    if (AppConfig.Instance.Key_MeshViewer_VertexSelection.Get().IsPressed()) SetGeometrySelectionMode(GeometrySelectionMode.Vertex);
                    else if (AppConfig.Instance.Key_MeshViewer_EdgeSelection.Get().IsPressed()) SetGeometrySelectionMode(GeometrySelectionMode.Edge);
                    else if (AppConfig.Instance.Key_MeshViewer_FaceSelection.Get().IsPressed()) SetGeometrySelectionMode(GeometrySelectionMode.Face);
                }
                if (!IsMoving && selectAllPressed) ToggleAllEditableElements();
                if (!IsMoving && (HasSelectedGeometry || selectedBoneElements.Count > 0) && AppConfig.Instance.Key_MeshViewer_MoveGeometry.Get().IsPressed()) BeginMove();
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
            DrawSelectedGeometry(viewportPosition, viewportSize);
        }
        DrawBoxSelection(viewportPosition);

        var controlsStart = ImGui.GetCursorPos();
        var hovered = false;        

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
    internal void ToggleSubmeshVisibility(SubmeshReference submesh)
    {
        var state = CaptureVisibilityState();
        if (!state.Submeshes.Add(submesh)) state.Submeshes.Remove(submesh);
        RecordVisibilityState(state);
    }

    internal void ToggleMeshGroupVisibility(MeshViewerContext context, List<int> submeshIndices)
    {
        var state = CaptureVisibilityState();
        var anyVisible = submeshIndices.Any(index => !state.Submeshes.Contains(new SubmeshReference(context, index)));
        foreach (var index in submeshIndices) {
            var submesh = new SubmeshReference(context, index);
            if (anyVisible) {
                state.Submeshes.Add(submesh);
            } else {
                state.Submeshes.Remove(submesh);
            }
        }
        RecordVisibilityState(state);
    }

    internal void ToggleArmatureVisibility(MeshViewerContext context)
    {
        var state = CaptureVisibilityState();
        if (!state.Armatures.Add(context)) state.Armatures.Remove(context);
        RecordVisibilityState(state);
    }

    private void SetEnabled(bool enabled)
    {
        if (IsEnabled == enabled) return;
        if (!enabled) {
            CancelMove();
        }
        IsEnabled = enabled;
        WasEverEnabled |= enabled;
        ClearAllSelection();
        if (enabled) {
            Viewer.outlinerWidthInitialized = false;
            Viewer.outlinerWidthUserResized = false;
        }
        EnsureSceneSubscription();
        ApplyRenderState();
    }

    public void SetDisplayMode(MeshDisplayMode mode)
    {
        DisplayMode = mode;
        ApplyRenderState();
    }

    public void SetGeometrySelectionMode(GeometrySelectionMode mode)
    {
        if (geometrySelectionMode == mode) return;
        ConvertGeometrySelection(mode);
        geometrySelectionMode = mode;
        ApplyRenderState();
    }

    private void ConvertGeometrySelection(GeometrySelectionMode targetMode)
    {
        var convertedVertices = new HashSet<VertexReference>();
        var convertedEdges = new HashSet<EdgeReference>();
        var convertedFaces = new HashSet<FaceReference>();

        if (targetMode == GeometrySelectionMode.Vertex) {
            if (geometrySelectionMode == GeometrySelectionMode.Edge) {
                foreach (var edge in selectedEdges) {
                    convertedVertices.Add(edge.First);
                    convertedVertices.Add(edge.Second);
                }
            } else {
                foreach (var reference in selectedFaces) {
                    if (!TryGetFaceVertices(reference, out var face)) continue;
                    convertedVertices.Add(face.First);
                    convertedVertices.Add(face.Second);
                    convertedVertices.Add(face.Third);
                }
            }
        } else if (targetMode == GeometrySelectionMode.Edge) {
            if (geometrySelectionMode == GeometrySelectionMode.Vertex) {
                foreach (var edge in EnumerateEditableEdges()) {
                    if (selectedVertices.Contains(edge.First) && selectedVertices.Contains(edge.Second)) convertedEdges.Add(edge);
                }
            } else {
                foreach (var reference in selectedFaces) {
                    if (!TryGetFaceVertices(reference, out var face)) continue;
                    convertedEdges.Add(CreateEdge(face.First, face.Second));
                    convertedEdges.Add(CreateEdge(face.Second, face.Third));
                    convertedEdges.Add(CreateEdge(face.Third, face.First));
                }
            }
        } else if (geometrySelectionMode == GeometrySelectionMode.Vertex) {
            foreach (var face in EnumerateEditableFaces()) {
                if (selectedVertices.Contains(face.First) && selectedVertices.Contains(face.Second)
                    && selectedVertices.Contains(face.Third)) convertedFaces.Add(face.Reference);
            }
        } else {
            foreach (var face in EnumerateEditableFaces()) {
                if (selectedEdges.Contains(CreateEdge(face.First, face.Second))
                    && selectedEdges.Contains(CreateEdge(face.Second, face.Third))
                    && selectedEdges.Contains(CreateEdge(face.Third, face.First))) convertedFaces.Add(face.Reference);
            }
        }

        ClearGeometrySelection();
        selectedVertices.UnionWith(convertedVertices);
        selectedEdges.UnionWith(convertedEdges);
        selectedFaces.UnionWith(convertedFaces);
    }

    private void ClearGeometrySelection()
    {
        selectedVertices.Clear();
        selectedEdges.Clear();
        selectedFaces.Clear();
    }

    public void SetInteractionMode(EditorInteractionMode mode)
    {
        if (interactionMode == mode) return;
        CancelMove();
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
        ClearGeometrySelection();
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

            var hit = handle.Handle.GetIntersection(ray, context.GameObject.Transform.WorldTransform, context.Component.PreviewRenderOptions?.HiddenSubmeshIndices);
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
        ClearGeometrySelection();
        ApplyRenderState();
    }

    private void SelectViewportArmature(MeshViewerContext context, bool extendSelection)
    {
        if (!extendSelection) {
            selectedSubmeshes.Clear();
            selectedArmatures.Clear();
            submeshSelectionAnchor = null;
            scrollToSubmesh = null;
            ClearGeometrySelection();
        }
        selectedArmatures.Add(context);
        selectedBoneElements.Clear();
        ApplyRenderState();
    }

    internal void SelectSubmesh(SubmeshReference submesh, bool shift, bool ctrl, bool scrollToSelected)
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
        ClearGeometrySelection();
        ApplyRenderState();
    }

    internal void SelectArmature(MeshViewerContext context, bool shift, bool ctrl)
    {
        if (!shift && !ctrl) {
            selectedSubmeshes.Clear();
            submeshSelectionAnchor = null;
            scrollToSubmesh = null;
            ClearGeometrySelection();
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
        ClearGeometrySelection();
        selectedBoneElements.Clear();
        ApplyRenderState();
    }

    private void HideSelectedObjects()
    {
        if (selectedSubmeshes.Count == 0 && selectedArmatures.Count == 0) return;

        var state = CaptureVisibilityState();
        state.Submeshes.UnionWith(selectedSubmeshes);
        state.Armatures.UnionWith(selectedArmatures);
        RecordVisibilityState(state);
    }

    private void UnhideAllObjects()
    {
        if (hiddenSubmeshes.Count == 0 && hiddenArmatures.Count == 0) return;

        RecordVisibilityState(new VisibilityState([], []));
    }

    private VisibilityState CaptureVisibilityState() => new([.. hiddenSubmeshes], [.. hiddenArmatures]);

    private void RecordVisibilityState(VisibilityState state)
    {
        if (hiddenSubmeshes.SetEquals(state.Submeshes) && hiddenArmatures.SetEquals(state.Armatures)) return;

        var previous = CaptureVisibilityState();
        UndoRedo.RecordCallback(null,
            () => ApplyVisibilityState(state),
            () => ApplyVisibilityState(previous));
    }

    private void ApplyVisibilityState(VisibilityState state)
    {
        hiddenSubmeshes.Clear();
        hiddenSubmeshes.UnionWith(state.Submeshes);
        hiddenArmatures.Clear();
        hiddenArmatures.UnionWith(state.Armatures);

        selectedSubmeshes.RemoveWhere(hiddenSubmeshes.Contains);
        selectedArmatures.RemoveWhere(hiddenArmatures.Contains);
        var hiddenSubmeshContexts = hiddenSubmeshes.Select(submesh => submesh.Context).ToHashSet();
        selectedVertices.RemoveWhere(vertex => hiddenSubmeshContexts.Contains(vertex.Context));
        selectedEdges.RemoveWhere(edge => hiddenSubmeshContexts.Contains(edge.First.Context));
        selectedFaces.RemoveWhere(face => hiddenSubmeshContexts.Contains(face.Context));
        selectedBoneElements.RemoveWhere(element => hiddenArmatures.Contains(element.Bone.Context));
        if (submeshSelectionAnchor is { } anchor && hiddenSubmeshes.Contains(anchor)) submeshSelectionAnchor = null;
        if (scrollToSubmesh is { } scrollTarget && hiddenSubmeshes.Contains(scrollTarget)) scrollToSubmesh = null;
        ApplyRenderState();
    }

    private void ToggleAllEditableElements()
    {
        var vertices = geometrySelectionMode == GeometrySelectionMode.Vertex ? EnumerateEditableVertices().ToArray() : [];
        var edges = geometrySelectionMode == GeometrySelectionMode.Edge ? EnumerateEditableEdges().ToArray() : [];
        var faces = geometrySelectionMode == GeometrySelectionMode.Face ? EnumerateEditableFaces().Select(face => face.Reference).ToArray() : [];
        var boneElements = EnumerateEditableBoneElements().ToArray();
        if ((vertices.Length > 0 || edges.Length > 0 || faces.Length > 0 || boneElements.Length > 0)
            && vertices.All(selectedVertices.Contains)
            && edges.All(selectedEdges.Contains)
            && faces.All(selectedFaces.Contains)
            && boneElements.All(selectedBoneElements.Contains)) {
            ClearGeometrySelection();
            selectedBoneElements.Clear();
        } else {
            ClearGeometrySelection();
            selectedVertices.UnionWith(vertices);
            selectedEdges.UnionWith(edges);
            selectedFaces.UnionWith(faces);
            selectedBoneElements.Clear();
            selectedBoneElements.UnionWith(boneElements);
        }
    }

    private void ClearSubmeshSelection()
    {
        selectedSubmeshes.Clear();
        submeshSelectionAnchor = null;
        scrollToSubmesh = null;
        ClearGeometrySelection();
        ApplyRenderState();
    }

    internal void ClearAllSelection()
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
                ClearGeometrySelection();
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
        switch (geometrySelectionMode) {
            case GeometrySelectionMode.Vertex:
                SelectVertex(viewportPosition, extendSelection, toggleSelection);
                break;
            case GeometrySelectionMode.Edge:
                SelectEdge(viewportPosition, extendSelection, toggleSelection);
                break;
            case GeometrySelectionMode.Face:
                SelectFace(viewportPosition, extendSelection, toggleSelection);
                break;
        }
    }

    private void SelectElementsInBox(Vector2 start, Vector2 end, bool extendSelection, bool toggleSelection)
    {
        if (!extendSelection && !toggleSelection) {
            ClearGeometrySelection();
            selectedBoneElements.Clear();
        }
        switch (geometrySelectionMode) {
            case GeometrySelectionMode.Vertex:
                SelectVerticesInBox(start, end, true, toggleSelection);
                break;
            case GeometrySelectionMode.Edge:
                SelectEdgesInBox(start, end, true, toggleSelection);
                break;
            case GeometrySelectionMode.Face:
                SelectFacesInBox(start, end, true, toggleSelection);
                break;
        }
        SelectBonesInBox(start, end, toggleSelection);
    }

    private void SelectObjectsInBox(Vector2 start, Vector2 end, bool extendSelection)
    {
        if (subscribedScene == null) return;
        var min = Vector2.Min(start, end);
        var max = Vector2.Max(start, end);
        var camera = subscribedScene.ActiveCamera;
        using var depth = ReadSelectionDepth(min, max);
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
        ClearGeometrySelection();
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
        return GetBoneViewportPosition(reference);
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

    private void SelectEdge(Vector2 viewportPosition, bool extendSelection, bool toggleSelection)
    {
        var radiusSquared = vertexSelectionRadius * vertexSelectionRadius;
        var candidates = new List<(EdgeReference edge, Vector3 viewport, float distanceSquared)>();
        foreach (var edge in EnumerateEditableEdges()) {
            var first = GetVertexViewportPosition(edge.First);
            var second = GetVertexViewportPosition(edge.Second);
            if (first.X == float.MaxValue || second.X == float.MaxValue) continue;
            var segment = new Vector2(second.X - first.X, second.Y - first.Y);
            var lengthSquared = segment.LengthSquared();
            var amount = lengthSquared <= float.Epsilon
                ? 0.0f
                : Math.Clamp(Vector2.Dot(viewportPosition - new Vector2(first.X, first.Y), segment) / lengthSquared, 0.0f, 1.0f);
            var closestPoint = Vector3.Lerp(first, second, amount);
            var distanceSquared = Vector2.DistanceSquared(viewportPosition, new Vector2(closestPoint.X, closestPoint.Y));
            if (distanceSquared <= radiusSquared) candidates.Add((edge, closestPoint, distanceSquared));
        }
        candidates.Sort(static (left, right) => left.distanceSquared.CompareTo(right.distanceSquared));

        using var depth = ReadSelectionDepth(
            viewportPosition - new Vector2(vertexSelectionRadius),
            viewportPosition + new Vector2(vertexSelectionRadius));
        EdgeReference? closest = null;
        foreach (var candidate in candidates) {
            if (!IsVertexVisible(candidate.viewport, depth)) continue;
            closest = candidate.edge;
            break;
        }

        if (!extendSelection && !toggleSelection) ClearGeometrySelection();
        if (closest is not { } selected) return;
        if (toggleSelection) {
            if (!selectedEdges.Add(selected)) selectedEdges.Remove(selected);
        } else {
            selectedEdges.Add(selected);
        }
    }

    private void SelectFace(Vector2 viewportPosition, bool extendSelection, bool toggleSelection)
    {
        var closest = FindHitFace(viewportPosition);
        if (!extendSelection && !toggleSelection) ClearGeometrySelection();
        if (closest is not { } selected) return;
        if (toggleSelection) {
            if (!selectedFaces.Add(selected)) selectedFaces.Remove(selected);
        } else {
            selectedFaces.Add(selected);
        }
    }

    private FaceReference? FindHitFace(Vector2 viewportPosition)
    {
        if (subscribedScene == null) return null;
        var ray = subscribedScene.ActiveCamera.ViewportToRay(viewportPosition);
        FaceReference? closest = null;
        var closestDistance = float.MaxValue;
        foreach (var group in selectedSubmeshes.GroupBy(submesh => submesh.Context)) {
            var context = group.Key;
            var handle = context.Component.MeshHandle;
            if (handle == null || !context.GameObject.ShouldDraw) continue;
            var allowed = group
                .Where(submesh => !hiddenSubmeshes.Contains(submesh)
                    && (uint)submesh.Index < (uint)handle.MeshCount
                    && handle.GetMeshPartEnabled(handle.GetMesh(submesh.Index).MeshGroup))
                .Select(submesh => submesh.Index)
                .ToHashSet();
            if (allowed.Count == 0) continue;
            var excluded = Enumerable.Range(0, handle.MeshCount).Where(index => !allowed.Contains(index)).ToHashSet();
            var hit = handle.Handle.GetIntersection(ray, context.GameObject.Transform.WorldTransform, excluded);
            if (!hit.IsHit || hit.distanceSquared >= closestDistance) continue;
            var reference = new FaceReference(context, hit.meshIndex, hit.triangleIndex);
            if (!TryGetFaceVertices(reference, out _)) continue;
            closest = reference;
            closestDistance = hit.distanceSquared;
        }
        return closest;
    }

    private void SelectEdgesInBox(Vector2 start, Vector2 end, bool extendSelection, bool toggleSelection)
    {
        if (subscribedScene == null) return;
        var min = Vector2.Min(start, end);
        var max = Vector2.Max(start, end);
        using var depth = ReadSelectionDepth(min, max);
        var boxed = new HashSet<EdgeReference>();
        foreach (var edge in EnumerateEditableEdges()) {
            var first = GetVertexViewportPosition(edge.First);
            var second = GetVertexViewportPosition(edge.Second);
            if (first.X == float.MaxValue || second.X == float.MaxValue
                || !SegmentIntersectsRectangle(new Vector2(first.X, first.Y), new Vector2(second.X, second.Y), min, max)) continue;
            var midpoint = (first + second) * 0.5f;
            if (IsVertexVisible(midpoint, depth) || IsVertexVisible(first, depth) || IsVertexVisible(second, depth)) boxed.Add(edge);
        }

        if (!extendSelection && !toggleSelection) ClearGeometrySelection();
        if (toggleSelection) {
            foreach (var edge in boxed) {
                if (!selectedEdges.Add(edge)) selectedEdges.Remove(edge);
            }
        } else {
            selectedEdges.UnionWith(boxed);
        }
    }

    private void SelectFacesInBox(Vector2 start, Vector2 end, bool extendSelection, bool toggleSelection)
    {
        if (subscribedScene == null) return;
        var min = Vector2.Min(start, end);
        var max = Vector2.Max(start, end);
        using var depth = ReadSelectionDepth(min, max);
        var boxed = new HashSet<FaceReference>();
        foreach (var face in EnumerateEditableFaces()) {
            var first = GetVertexViewportPosition(face.First);
            var second = GetVertexViewportPosition(face.Second);
            var third = GetVertexViewportPosition(face.Third);
            if (first.X == float.MaxValue || second.X == float.MaxValue || third.X == float.MaxValue) continue;
            var first2 = new Vector2(first.X, first.Y);
            var second2 = new Vector2(second.X, second.Y);
            var third2 = new Vector2(third.X, third.Y);
            if (!TriangleIntersectsRectangle(first2, second2, third2, min, max)) continue;
            var center = (first + second + third) / 3.0f;
            if (IsVertexVisible(center, depth) || IsVertexVisible(first, depth)
                || IsVertexVisible(second, depth) || IsVertexVisible(third, depth)) boxed.Add(face.Reference);
        }

        if (!extendSelection && !toggleSelection) ClearGeometrySelection();
        if (toggleSelection) {
            foreach (var face in boxed) {
                if (!selectedFaces.Add(face)) selectedFaces.Remove(face);
            }
        } else {
            selectedFaces.UnionWith(boxed);
        }
    }

    private static bool TriangleIntersectsRectangle(Vector2 first, Vector2 second, Vector2 third, Vector2 min, Vector2 max)
    {
        if (PointInRectangle(first, min, max) || PointInRectangle(second, min, max) || PointInRectangle(third, min, max)) return true;
        var topRight = new Vector2(max.X, min.Y);
        var bottomLeft = new Vector2(min.X, max.Y);
        if (PointInTriangle(min, first, second, third) || PointInTriangle(topRight, first, second, third)
            || PointInTriangle(max, first, second, third) || PointInTriangle(bottomLeft, first, second, third)) return true;
        return SegmentIntersectsRectangle(first, second, min, max)
            || SegmentIntersectsRectangle(second, third, min, max)
            || SegmentIntersectsRectangle(third, first, min, max);
    }

    private static bool PointInTriangle(Vector2 point, Vector2 first, Vector2 second, Vector2 third)
    {
        var firstSign = Cross(second - first, point - first);
        var secondSign = Cross(third - second, point - second);
        var thirdSign = Cross(first - third, point - third);
        var hasNegative = firstSign < 0 || secondSign < 0 || thirdSign < 0;
        var hasPositive = firstSign > 0 || secondSign > 0 || thirdSign > 0;
        return !(hasNegative && hasPositive);
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

        using var depth = ReadSelectionDepth(
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
        using var depth = ReadSelectionDepth(min, max);
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
        var context = subscribedScene?.RenderContext;
        if (context == null) return null;
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
        foreach (var selectedSubmesh in selectedSubmeshes) {
            var context = selectedSubmesh.Context;
            var handle = context.Component.MeshHandle;
            if (handle == null || !context.GameObject.ShouldDraw || hiddenSubmeshes.Contains(selectedSubmesh)) continue;
            var cache = GetSubmeshCache(context);
            if ((uint)selectedSubmesh.Index >= (uint)cache.Submeshes.Length || (uint)selectedSubmesh.Index >= (uint)handle.MeshCount) continue;
            var renderMesh = handle.GetMesh(selectedSubmesh.Index);
            if (!handle.GetMeshPartEnabled(renderMesh.MeshGroup)) continue;

            var submesh = cache.Submeshes[selectedSubmesh.Index];
            for (var localIndex = 0; localIndex < submesh.vertCount; localIndex++) {
                var vertex = new VertexReference(context, submesh.Buffer, submesh.vertsIndexOffset + localIndex);
                if (seen.Add(vertex)) yield return vertex;
            }
        }
    }

    private IEnumerable<FaceVertices> EnumerateEditableFaces()
    {
        foreach (var selectedSubmesh in selectedSubmeshes) {
            var context = selectedSubmesh.Context;
            var handle = context.Component.MeshHandle;
            if (handle == null || !context.GameObject.ShouldDraw || hiddenSubmeshes.Contains(selectedSubmesh)
                || (uint)selectedSubmesh.Index >= (uint)handle.MeshCount) continue;
            var renderMesh = handle.GetMesh(selectedSubmesh.Index);
            if (!handle.GetMeshPartEnabled(renderMesh.MeshGroup)) continue;
            var triangleCount = renderMesh.Indices.Length / 3;
            for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++) {
                var reference = new FaceReference(context, selectedSubmesh.Index, triangleIndex);
                if (TryGetFaceVertices(reference, out var face)) yield return face;
            }
        }
    }

    private IEnumerable<EdgeReference> EnumerateEditableEdges()
    {
        var seen = new HashSet<EdgeReference>();
        foreach (var face in EnumerateEditableFaces()) {
            var first = CreateEdge(face.First, face.Second);
            var second = CreateEdge(face.Second, face.Third);
            var third = CreateEdge(face.Third, face.First);
            if (seen.Add(first)) yield return first;
            if (seen.Add(second)) yield return second;
            if (seen.Add(third)) yield return third;
        }
    }

    private bool TryGetFaceVertices(FaceReference reference, out FaceVertices face)
    {
        face = default;
        var handle = reference.Context.Component.MeshHandle;
        if (handle == null || (uint)reference.SubmeshIndex >= (uint)handle.MeshCount) return false;
        var cache = GetSubmeshCache(reference.Context);
        if ((uint)reference.SubmeshIndex >= (uint)cache.Submeshes.Length) return false;
        var renderMesh = handle.GetMesh(reference.SubmeshIndex);
        var indexOffset = reference.TriangleIndex * 3;
        if (indexOffset < 0 || indexOffset + 2 >= renderMesh.Indices.Length) return false;
        var submesh = cache.Submeshes[reference.SubmeshIndex];
        var firstIndex = renderMesh.Indices[indexOffset];
        var secondIndex = renderMesh.Indices[indexOffset + 1];
        var thirdIndex = renderMesh.Indices[indexOffset + 2];
        if ((uint)firstIndex >= (uint)submesh.vertCount || (uint)secondIndex >= (uint)submesh.vertCount
            || (uint)thirdIndex >= (uint)submesh.vertCount) return false;
        face = new FaceVertices(
            reference,
            new VertexReference(reference.Context, submesh.Buffer, submesh.vertsIndexOffset + firstIndex),
            new VertexReference(reference.Context, submesh.Buffer, submesh.vertsIndexOffset + secondIndex),
            new VertexReference(reference.Context, submesh.Buffer, submesh.vertsIndexOffset + thirdIndex));
        return true;
    }

    private static EdgeReference CreateEdge(VertexReference first, VertexReference second) =>
        first.Index <= second.Index ? new EdgeReference(first, second) : new EdgeReference(second, first);

    private Vector3 GetVertexViewportPosition(VertexReference vertex)
    {
        if (subscribedScene == null) return new Vector3(float.MaxValue);
        var world = GetVertexWorldPosition(vertex, vertex.Buffer.Positions[vertex.Index]);
        return subscribedScene.ActiveCamera.WorldToViewportPosition(world, true, true);
    }

    private HashSet<VertexReference> GetSelectedGeometryVertices()
    {
        var vertices = new HashSet<VertexReference>();
        switch (geometrySelectionMode) {
            case GeometrySelectionMode.Vertex:
                vertices.UnionWith(selectedVertices);
                break;
            case GeometrySelectionMode.Edge:
                foreach (var edge in selectedEdges) {
                    vertices.Add(edge.First);
                    vertices.Add(edge.Second);
                }
                break;
            case GeometrySelectionMode.Face:
                foreach (var reference in selectedFaces) {
                    if (!TryGetFaceVertices(reference, out var face)) continue;
                    vertices.Add(face.First);
                    vertices.Add(face.Second);
                    vertices.Add(face.Third);
                }
                break;
        }
        return vertices;
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

    private Dictionary<VertexReference, Vector3> BuildMirroredVertexMoveSet(IReadOnlyCollection<VertexReference> selectedGeometryVertices)
    {
        var result = selectedGeometryVertices.ToDictionary(vertex => vertex, _ => Vector3.One);
        var mirrorSigns = GetMirrorSigns().ToArray();
        if (mirrorSigns.Length == 0 || mirrorRadius <= 0) return result;

        var candidates = EnumerateEditableVertices()
            .Select(vertex => (reference: vertex, position: GetVertexWorldPosition(vertex, vertex.Buffer.Positions[vertex.Index])))
            .GroupBy(candidate => candidate.reference.Context)
            .ToDictionary(group => group.Key, group => BuildMirrorGrid(group, mirrorRadius));
        foreach (var source in selectedGeometryVertices) {
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
        var selectedGeometryVertices = GetSelectedGeometryVertices();
        if (subscribedScene == null || selectedGeometryVertices.Count == 0 && selectedBoneElements.Count == 0) return;

        foreach (var context in selectedBoneElements.Select(element => element.Bone.Context).Distinct()) context.Animator?.Stop();
        moveVertexDeltaSigns = selectedGeometryVertices.Count > 0 ? BuildMirroredVertexMoveSet(selectedGeometryVertices) : null;
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
            foreach (var vertex in selectedGeometryVertices) {
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
        lastMovePreviewDelta = null;
    }

    private void UpdateMovePreview()
    {
        if (subscribedScene == null || !IsMoving) return;
        var worldDelta = GetConstrainedMoveDelta(subscribedScene.Mouse.MouseScreenPosition);
        if (lastMovePreviewDelta == worldDelta) return;
        lastMovePreviewDelta = worldDelta;
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
        var inverseWorldTransforms = new Dictionary<MeshViewerContext, Matrix4x4>();
        foreach (var context in originals.Keys.Select(vertex => vertex.Context).Distinct()) {
            if (Matrix4x4.Invert(context.GameObject.Transform.WorldTransform, out var inverseWorld)) {
                inverseWorldTransforms[context] = inverseWorld;
            }
        }
        foreach (var (vertex, original) in originals) {
            if (!inverseWorldTransforms.TryGetValue(vertex.Context, out var inverseWorld)) continue;
            var deltaSign = moveVertexDeltaSigns?.GetValueOrDefault(vertex, Vector3.One) ?? Vector3.One;
            var hasSkinMatrix = TryGetSkinMatrix(vertex, out var skinMatrix);
            var local = hasSkinMatrix ? Vector3.Transform(original, skinMatrix) : original;
            var world = Vector3.Transform(local, vertex.Context.GameObject.Transform.WorldTransform) + worldDelta * deltaSign;
            var posedLocal = Vector3.Transform(world, inverseWorld);
            if (hasSkinMatrix && Matrix4x4.Invert(skinMatrix, out var inverseSkin)) {
                vertex.Buffer.Positions[vertex.Index] = Vector3.Transform(posedLocal, inverseSkin);
            } else {
                vertex.Buffer.Positions[vertex.Index] = posedLocal;
            }
        }
        RefreshEditedRenderMeshes(originals.Keys);
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
            : tail;
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
        RefreshEditedRenderMeshes(positions.Keys);
    }

    private void RefreshEditedRenderMeshes(IEnumerable<VertexReference> vertices)
    {
        foreach (var contextVertices in vertices.GroupBy(vertex => vertex.Context)) {
            var context = contextVertices.Key;
            var handle = context.Component.MeshHandle;
            if (handle == null) continue;
            var changedIndices = contextVertices
                .GroupBy(vertex => vertex.Buffer)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(vertex => vertex.Index).Distinct().Order().ToArray());
            var submeshes = GetSubmeshCache(context).Submeshes;
            var submeshCount = Math.Min(submeshes.Length, handle.MeshCount);
            for (var submeshIndex = 0; submeshIndex < submeshCount; submeshIndex++) {
                var submesh = submeshes[submeshIndex];
                if (!changedIndices.TryGetValue(submesh.Buffer, out var indices)) continue;
                var firstIndex = Array.BinarySearch(indices, submesh.vertsIndexOffset);
                if (firstIndex < 0) firstIndex = ~firstIndex;
                if ((uint)firstIndex >= (uint)indices.Length || indices[firstIndex] >= submesh.vertsIndexOffset + submesh.vertCount) continue;
                handle.GetMesh(submeshIndex).UpdateVertexPositions(submesh.Positions);
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
        lastMovePreviewDelta = null;

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
        lastMovePreviewDelta = null;
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

            foreach (var bone in bones) {
                var tail = screenPositions[bone.index];
                var head = bone.parentIndex >= 0 && (uint)bone.parentIndex < (uint)screenPositions.Length
                    ? screenPositions[bone.parentIndex]
                    : tail;
                if (head.X == float.MaxValue || tail.X == float.MaxValue) continue;
                drawList.AddLine(head, tail, armatureLineColor, armatureSelected ? lineThickness + 1.0f : lineThickness);
                drawList.AddCircleFilled(head, dotRadius, armatureDotColor);
                drawList.AddCircleFilled(tail, dotRadius, armatureDotColor);
            }

            foreach (var element in selectedBoneElements.Where(element => element.Bone.Context == context)) {
                var bone = element.Bone.Bone;
                var tail = screenPositions[bone.index];
                var head = bone.parentIndex >= 0 && (uint)bone.parentIndex < (uint)screenPositions.Length
                    ? screenPositions[bone.parentIndex]
                    : tail;
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

    private void DrawSelectedGeometry(Vector2 viewportPosition, Vector2 viewportSize)
    {
        if (subscribedScene == null || !HasSelectedGeometry) return;
        var drawList = ImGui.GetWindowDrawList();
        var camera = subscribedScene.ActiveCamera;
        var color = ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.15f, 1.0f));
        var faceColor = ImGui.GetColorU32(new Vector4(1.0f, 0.48f, 0.05f, 0.28f));
        var outlineColor = 0xff000000;
        drawList.PushClipRect(viewportPosition, viewportPosition + viewportSize, true);
        if (geometrySelectionMode == GeometrySelectionMode.Vertex) {
            var radii = selectedVertices.Select(vertex => vertex.Context).Distinct()
                .ToDictionary(context => context, context => Math.Max(GetAdaptiveVertexPointSize(context) + 1.0f, 2.0f));
            foreach (var vertex in selectedVertices) {
                var radius = radii[vertex.Context];
                var screen = GetVertexScreenPosition(vertex, camera);
                if (screen.X == float.MaxValue) continue;
                drawList.AddRectFilled(screen - new Vector2(radius + 1.0f), screen + new Vector2(radius + 1.0f), outlineColor);
                drawList.AddRectFilled(screen - new Vector2(radius), screen + new Vector2(radius), color);
            }
        } else if (geometrySelectionMode == GeometrySelectionMode.Edge) {
            foreach (var edge in selectedEdges) {
                var first = GetVertexScreenPosition(edge.First, camera);
                var second = GetVertexScreenPosition(edge.Second, camera);
                if (first.X == float.MaxValue || second.X == float.MaxValue) continue;
                drawList.AddLine(first, second, outlineColor, 4.0f * UI.UIScale);
                drawList.AddLine(first, second, color, 2.0f * UI.UIScale);
            }
        } else {
            foreach (var reference in selectedFaces) {
                if (!TryGetFaceVertices(reference, out var face)) continue;
                var first = GetVertexScreenPosition(face.First, camera);
                var second = GetVertexScreenPosition(face.Second, camera);
                var third = GetVertexScreenPosition(face.Third, camera);
                if (first.X == float.MaxValue || second.X == float.MaxValue || third.X == float.MaxValue) continue;
                drawList.AddTriangleFilled(first, second, third, faceColor);
                drawList.AddTriangle(first, second, third, color, 2.0f * UI.UIScale);
            }
        }
        drawList.PopClipRect();
    }

    private static Vector2 GetVertexScreenPosition(VertexReference vertex, Camera camera)
    {
        var world = GetVertexWorldPosition(vertex, vertex.Buffer.Positions[vertex.Index]);
        return camera.WorldToScreenPosition(world, true, true);
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
    internal sealed record OutlinerHoverState(MeshViewerContext? Context, HashSet<int>? SubmeshIndices)
    {
        public static readonly OutlinerHoverState None = new(null, null);

        public bool HasSameContentAs(OutlinerHoverState other)
        {
            if (Context != other.Context) return false;
            if (SubmeshIndices == null || other.SubmeshIndices == null) return SubmeshIndices == other.SubmeshIndices;
            return SubmeshIndices.SetEquals(other.SubmeshIndices);
        }
    }
    internal void ApplyRenderState()
    {
        foreach (var context in Viewer.MeshContexts) {
            var selectedIndices = selectedSubmeshes
                .Where(submesh => submesh.Context == context)
                .Select(submesh => submesh.Index)
                .ToHashSet();
            var hiddenIndices = hiddenSubmeshes.Where(submesh => submesh.Context == context).Select(submesh => submesh.Index).ToHashSet();
            var highlightedIndices = IsEnabled && interactionMode == EditorInteractionMode.Object ? selectedIndices : !IsEnabled && hoveredInOutliner.Context == context && hoveredInOutliner.SubmeshIndices != null ? hoveredInOutliner.SubmeshIndices : null;
            var editMode = IsEnabled && interactionMode == EditorInteractionMode.Edit;
            var editIndices = editMode ? selectedIndices : null;
            var wireframeOverlay = context.Component.Scene?.WireframeOverlay == true;
            var showEditVertices = editMode && geometrySelectionMode == GeometrySelectionMode.Vertex && selectedIndices.Count > 0;
            var previewActive = DisplayMode != MeshDisplayMode.Default
                || hiddenIndices?.Count > 0
                || highlightedIndices?.Count > 0
                || wireframeOverlay
                || editMode && editIndices?.Count > 0
                || showEditVertices;
            if (!previewActive) {
                context.Component.PreviewRenderOptions = null;
                continue;
            }

            var options = context.Component.PreviewRenderOptions ?? new MeshPreviewRenderOptions();
            options.DisplayMode = DisplayMode;
            options.HiddenSubmeshIndices = hiddenIndices;
            options.HighlightedSubmeshIndices = highlightedIndices;
            options.EditSubmeshIndices = editIndices;
            options.WireframeOverlay = wireframeOverlay;
            options.EditWireframeOverlay = editMode;
            options.ShowEditVertices = showEditVertices;
            options.EditVertexPointSize = GetAdaptiveVertexPointSize(context);
            context.Component.PreviewRenderOptions = options;
        }
        renderStateDirty = false;
    }

    private void UpdateEditVertexPointSizes()
    {
        if (!IsEnabled || interactionMode != EditorInteractionMode.Edit) return;
        foreach (var context in selectedSubmeshes.Select(submesh => submesh.Context).Distinct()) {
            var options = context.Component.PreviewRenderOptions;
            if (options != null) options.EditVertexPointSize = GetAdaptiveVertexPointSize(context);
        }
    }

    internal SubmeshLabel GetSubmeshLabel(MeshViewerContext context, int meshIndex, int meshGroup)
    {
        var cache = GetSubmeshCache(context);
        return meshIndex >= 0 && meshIndex < cache.Labels.Length
            ? cache.Labels[meshIndex]
            : new SubmeshLabel(TranslatableBase.GetNullTerminatedUTF8($"Submesh {meshIndex}  |  Group {meshGroup}"));
    }

    internal SubmeshCache GetSubmeshCache(MeshViewerContext context)
    {
        var nativeMesh = context.MeshFile.NativeMesh;
        if (!submeshCache.TryGetValue(context, out var cache) || !ReferenceEquals(cache.NativeMesh, nativeMesh)) {
            var labels = new List<SubmeshLabel>();
            var submeshes = new List<Submesh>();
            var lod = nativeMesh.MeshData?.LODs.FirstOrDefault();
            if (lod != null) {
                foreach (var group in lod.MeshGroups) {
                    foreach (var submesh in group.Submeshes) {
                        submeshes.Add(submesh);
                        var materialName = nativeMesh.MaterialNames.ElementAtOrDefault(submesh.materialIndex);
                        var label = string.IsNullOrEmpty(materialName)
                            ? $"Submesh {labels.Count}"
                            : $"Submesh {labels.Count} | {materialName}";
                        labels.Add(new SubmeshLabel(TranslatableBase.GetNullTerminatedUTF8(label)));
                    }
                }
            }
            var meshes = context.Component.MeshHandle?.Meshes;
            if (meshes != null) {
                var fallbackIndex = 0;
                foreach (var mesh in meshes) {
                    if (fallbackIndex >= labels.Count) {
                        var label = $"Submesh {fallbackIndex}  |  Group {mesh.MeshGroup}";
                        labels.Add(new SubmeshLabel(TranslatableBase.GetNullTerminatedUTF8(label)));
                    }
                    fallbackIndex++;
                }
            }
            submeshCache[context] = cache = new SubmeshCache(nativeMesh, labels.ToArray(), submeshes.ToArray());
        }
        return cache;
    }

    internal void InvalidateSubmeshCache(MeshViewerContext context)
    {
        submeshCache.Remove(context);
        renderStateDirty = true;
    }

    public void Dispose()
    {
        CancelMove();
        submeshCache.Clear();
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
