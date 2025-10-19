using System.Numerics;
using ImGuiNET;

namespace ContentEditor.App.Graphics;

public class GizmoState(Scene scene)
{
    private List<(int id, object target, HandleContainer handles)> previousChildren = new();
    private List<(int id, object target, HandleContainer handles)> children = new();

    private (int id, int handleId)? activeHandle;
    private Vector2 activeHandleStartPosition;

    public Scene Scene { get; } = scene;

    private const uint ColorHandleFill = 0xD37354FF;
    private const uint ColorHandleBorder = 0xffffffff;
    private const uint ColorHandleFillHovered = 0xD85615FF;
    private const uint ColorHandleActive = 0xF82F00FF;
    private const float HandleBorderSize = 0.75f;

    private List<Action> DrawListQueue = new();

    private class HandleContainer
    {
        public int count;
    }

    public void FinishFrame()
    {
        (previousChildren, children) = (children, previousChildren);
        children.Clear();
        DrawImGui();
    }

    public void DrawImGui()
    {
        foreach (var q in DrawListQueue) q.Invoke();
        DrawListQueue.Clear();
    }

    public void Push(object obj)
    {
        children.Add((children.Count, obj, new HandleContainer()));
    }

    public bool PositionHandle(ref Vector3 position, out int handleId, float handleSize = 5f, Vector3 axis = default)
    {
        var current = children.Last();
        handleId = current.handles.count++;
        var screenPosition = Scene.ActiveCamera.WorldToScreenPosition(position);
        if (activeHandle == null) {
            var mouse = Scene.MouseHandler?.MouseScreenPosition ?? new(float.MaxValue);
            if ((mouse - screenPosition).LengthSquared() < handleSize * handleSize && !Scene.MouseHandler!.IsDragging) {
                // TODO not ideal - needless allocations - optimize later
                DrawListQueue.Add(() => {
                    ImGui.GetWindowDrawList().AddCircleFilled(screenPosition, handleSize, ColorHandleBorder);
                    ImGui.GetWindowDrawList().AddCircleFilled(screenPosition, handleSize * HandleBorderSize, ColorHandleFillHovered);
                });
                if (Scene.MouseHandler.IsLeftDown) {
                    activeHandle = (current.id, handleId);
                    activeHandleStartPosition = Scene.MouseHandler.MouseScreenPosition;
                }
            } else {
                DrawListQueue.Add(() => {
                    ImGui.GetWindowDrawList().AddCircleFilled(screenPosition, handleSize, ColorHandleBorder);
                    ImGui.GetWindowDrawList().AddCircleFilled(screenPosition, handleSize * HandleBorderSize, ColorHandleFill);
                });
            }
        } else if (activeHandle == (current.id, handleId)) {
            if (!Scene.MouseHandler!.IsLeftDown) {
                activeHandle = null;
            } else {
                var orgPos = activeHandleStartPosition;
                var prevPos = screenPosition;
                var newPos = Scene.MouseHandler.MouseScreenPosition;

                position = Scene.ActiveCamera.ScreenToWorldPositionReproject(newPos, position);
                screenPosition = Scene.ActiveCamera.WorldToScreenPosition(position, false);
            }
            DrawListQueue.Add(() => {
                ImGui.GetWindowDrawList().AddCircleFilled(screenPosition, handleSize * HandleBorderSize, ColorHandleActive);
            });
            return true;
        } else {
            DrawListQueue.Add(() => {
                ImGui.GetWindowDrawList().AddCircleFilled(screenPosition, handleSize, ColorHandleBorder);
                ImGui.GetWindowDrawList().AddCircleFilled(screenPosition, handleSize * HandleBorderSize, ColorHandleFill);
            });
        }
        return false;
    }
}
