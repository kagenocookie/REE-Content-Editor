using System.Numerics;
using ContentEditor.App.Windowing;
using ImGuiNET;
using Silk.NET.Input;

namespace ContentEditor.App;

public class SceneMouseHandler
{
    public event Action<ImGuiMouseButton, Vector2>? Clicked;
    public event Action<ImGuiMouseButton, Vector2>? DoubleClicked;
    public event Action<ImGuiMouseButton, Vector2>? Pressed;
    public event Action<ImGuiMouseButton, Vector2>? Released;
    public event Action<Vector2>? Dragging;
    public event Action<Vector2>? StartDragging;
    public event Action<Vector2>? StopDragging;

    public Scene? scene;

    private DateTime lastDownLB;
    private DateTime lastDownRB;
    private DateTime lastDownMB;
    private Vector2? downPositionLB;
    private Vector2? downPositionRB;
    private Vector2? downPositionMB;

    private DateTime lastClickLB;
    private DateTime lastClickRB;
    private DateTime lastClickMB;
    private Vector2? clickPositionLB;
    private Vector2? clickPositionRB;
    private Vector2? clickPositionMB;

    private bool downLB;
    private bool downRB;
    private bool downMB;

    private bool isDragging;
    private Vector2 _dragDelta;
    private Vector2 _dragStartPos;
    private ImGuiMouseButton dragStartButton;

    public bool IsDragging => isDragging;
    public Vector2 DragDelta => _dragDelta;

    private Vector2 lastMousePos = new Vector2(float.MaxValue);

    private const float DoubleClickIntervalSeconds = 0.4f;
    private const float ClickMaxDistance = 4f;

    private bool AnyMouseDown => downLB || downRB || downMB;


    private float zoomSpeed = 0.1f;


    private ref DateTime DownTime(ImGuiMouseButton button)
    {
        switch (button) {
            case ImGuiMouseButton.Left: return ref lastDownLB;
            case ImGuiMouseButton.Right: return ref lastDownRB;
            case ImGuiMouseButton.Middle: return ref lastDownMB;
        }

        throw new NotImplementedException("Invalid mouse button " + button);
    }

    private ref Vector2? DownPosition(ImGuiMouseButton button)
    {
        switch (button) {
            case ImGuiMouseButton.Left: return ref downPositionLB;
            case ImGuiMouseButton.Right: return ref downPositionRB;
            case ImGuiMouseButton.Middle: return ref downPositionMB;
        }

        throw new NotImplementedException("Invalid mouse button " + button);
    }

    private ref DateTime ClickTime(ImGuiMouseButton button)
    {
        switch (button) {
            case ImGuiMouseButton.Left: return ref lastClickLB;
            case ImGuiMouseButton.Right: return ref lastClickRB;
            case ImGuiMouseButton.Middle: return ref lastClickMB;
        }

        throw new NotImplementedException("Invalid mouse button " + button);
    }

    private ref Vector2? ClickPosition(ImGuiMouseButton button)
    {
        switch (button) {
            case ImGuiMouseButton.Left: return ref clickPositionLB;
            case ImGuiMouseButton.Right: return ref clickPositionRB;
            case ImGuiMouseButton.Middle: return ref clickPositionMB;
        }

        throw new NotImplementedException("Invalid mouse button " + button);
    }

    private ref bool IsDown(ImGuiMouseButton button)
    {
        switch (button) {
            case ImGuiMouseButton.Left: return ref downLB;
            case ImGuiMouseButton.Right: return ref downRB;
            case ImGuiMouseButton.Middle: return ref downMB;
        }

        throw new NotImplementedException("Invalid mouse button " + button);
    }

    private MouseButtonFlags DownFlags => (downLB ? MouseButtonFlags.Left : 0) | (downRB ? MouseButtonFlags.Right : 0) | (downMB ? MouseButtonFlags.Middle : 0);

    public void UpdateMouseDown(IMouse mouse, bool leftDown, bool rightDown, bool middleDown, Vector2 position, Vector2 relativePosition)
    {
        if (leftDown != downLB) {
            if (leftDown) {
                HandleMouseDown(ImGuiMouseButton.Left, relativePosition);
            } else {
                HandleMouseUp(mouse, ImGuiMouseButton.Left, relativePosition);
            }
        }

        if (rightDown != downRB) {
            if (rightDown) {
                HandleMouseDown(ImGuiMouseButton.Right, relativePosition);
            } else {
                HandleMouseUp(mouse, ImGuiMouseButton.Right, relativePosition);
            }
        }

        if (middleDown != downMB) {
            if (middleDown) {
                HandleMouseDown(ImGuiMouseButton.Middle, relativePosition);
            } else {
                HandleMouseUp(mouse, ImGuiMouseButton.Middle, relativePosition);
            }
        }

        if (lastMousePos != relativePosition) {
            HandleMouseMove(mouse, position);
        }
    }

    public void HandleMouseDown(ImGuiMouseButton button, Vector2 position)
    {
        IsDown(button) = true;
        DownTime(button) = DateTime.Now;
        DownPosition(button) = position;
        Pressed?.Invoke(button, position);
    }

    public void HandleMouseUp(IMouse mouse, ImGuiMouseButton button, Vector2 position)
    {
        var lastDownPos = DownPosition(button) ?? new Vector2(float.MinValue);

        IsDown(button) = false;
        Released?.Invoke(button, position);
        if ((position - lastDownPos).Length() < ClickMaxDistance) {
            var now = DateTime.Now;
            if ((now - ClickTime(button)).TotalSeconds < DoubleClickIntervalSeconds) {
                DoubleClicked?.Invoke(button, position);
            } else {
                // TODO delay click until DoubleClickIntervalSeconds has passed?
                Clicked?.Invoke(button, position);
            }
            ClickTime(button) = DateTime.Now;
            ClickPosition(button) = position;
        }
        if (!AnyMouseDown && isDragging) {
            isDragging = false;
            StopDragging?.Invoke(position);
            scene?.Controller?.OnMouseDragEnd(mouse, dragStartButton, position, _dragStartPos);
        }
    }

    public void HandleMouseMove(IMouse mouse, Vector2 position)
    {
        var delta = position - lastMousePos;
        lastMousePos = position;
        if (AnyMouseDown) {
            _dragDelta = delta;
            if (delta != Vector2.Zero) {
                if (!isDragging) {
                    if (downLB) dragStartButton = ImGuiMouseButton.Left;
                    if (downRB && lastDownRB > lastDownLB) dragStartButton = ImGuiMouseButton.Right;
                    if (downMB && lastDownMB > lastDownLB && lastDownMB > lastDownRB) dragStartButton = ImGuiMouseButton.Middle;

                    _dragStartPos = position;
                    _dragDelta = Vector2.Zero;
                    StartDragging?.Invoke(position);
                    scene?.Controller?.OnMouseDragStart(mouse, dragStartButton, position);
                }
                isDragging = true;
                Dragging?.Invoke(position);
                scene?.Controller?.OnMouseDrag(DownFlags, position, delta);
            }
        }
    }

    public void Update(float deltaTime)
    {
        if (scene == null) return;

        float wheel = ImGui.GetIO().MouseWheel;
        if (Math.Abs(wheel) > float.Epsilon) {
            if (scene.ActiveCamera.ProjectionMode == CameraProjection.Perspective) {
                var zoom = scene.Camera.GameObject.Transform.LocalForward * (wheel * zoomSpeed) * -1.0f;
                scene.Camera.GameObject.Transform.LocalPosition += zoom;
            } else {
                float ortho = scene.ActiveCamera.OrthoSize;
                ortho *= (1.0f - wheel * zoomSpeed);
                ortho = Math.Clamp(ortho, 0.01f, 100.0f);
                scene.ActiveCamera.OrthoSize = ortho;
            }
        }
    }
}