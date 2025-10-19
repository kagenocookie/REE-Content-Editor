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
    public Vector2 viewportOffset;

    public Vector2 MouseViewportPosition { get; private set; }
    public Vector2 MouseScreenPosition => MouseViewportPosition + viewportOffset;

    public bool IsLeftDown => IsDown(ImGuiMouseButton.Left);
    public bool IsRightDown => IsDown(ImGuiMouseButton.Right);
    public bool IsMiddleDown => IsDown(ImGuiMouseButton.Middle);

    public bool IsDragging => isDragging;
    public Vector2 DragDelta => _dragDelta;

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

    private Vector2 lastMousePos = new Vector2(float.MaxValue);

    private const float DoubleClickIntervalSeconds = 0.4f;
    private const float ClickMaxDistance = 4f;

    private bool AnyMouseDown => downLB || downRB || downMB;

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

    public void UpdateMouseDown(IMouse mouse, bool leftDown, bool rightDown, bool middleDown, bool allowDown, Vector2 mouseScreenPos, Vector2 viewportOffset)
    {
        this.viewportOffset = viewportOffset;
        if (leftDown != downLB) {
            if (!leftDown) {
                HandleMouseUp(mouse, ImGuiMouseButton.Left, mouseScreenPos);
            } else if (allowDown) {
                HandleMouseDown(ImGuiMouseButton.Left, mouseScreenPos);
            }
        }

        if (rightDown != downRB) {
            if (!rightDown) {
                HandleMouseUp(mouse, ImGuiMouseButton.Right, mouseScreenPos);
            } else if (allowDown) {
                HandleMouseDown(ImGuiMouseButton.Right, mouseScreenPos);
            }
        }

        if (middleDown != downMB) {
            if (middleDown) {
                HandleMouseUp(mouse, ImGuiMouseButton.Middle, mouseScreenPos);
            } else if (allowDown) {
                HandleMouseDown(ImGuiMouseButton.Middle, mouseScreenPos);
            }
        }

        if (lastMousePos != mouseScreenPos - viewportOffset) {
            HandleMouseMove(mouse, mouseScreenPos);
        }
    }

    public void HandleMouseDown(ImGuiMouseButton button, Vector2 position)
    {
        position -= viewportOffset;
        IsDown(button) = true;
        DownTime(button) = DateTime.Now;
        DownPosition(button) = position;
        Pressed?.Invoke(button, position);
    }

    public void HandleMouseUp(IMouse mouse, ImGuiMouseButton button, Vector2 position)
    {
        position -= viewportOffset;
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
            scene?.Controller?.OnMouseDragEnd(mouse, dragStartButton, position, _dragStartPos + viewportOffset);
        }
    }

    public void HandleMouseMove(IMouse mouse, Vector2 position)
    {
        position -= viewportOffset;
        MouseViewportPosition = position;
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
                scene?.Controller?.OnMouseDrag(DownFlags, position, _dragDelta);
            }
        }
    }

    public void Update(IMouse mouse)
    {
        if (downLB && !mouse.IsButtonPressed(MouseButton.Left)) {
            HandleMouseUp(mouse, ImGuiMouseButton.Left, lastMousePos);
        }
        if (downRB && !mouse.IsButtonPressed(MouseButton.Right)) {
            HandleMouseUp(mouse, ImGuiMouseButton.Right, lastMousePos);
        }
        if (downMB && !mouse.IsButtonPressed(MouseButton.Middle)) {
            HandleMouseUp(mouse, ImGuiMouseButton.Middle, lastMousePos);
        }
    }
}