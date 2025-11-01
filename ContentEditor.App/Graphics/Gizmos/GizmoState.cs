using System.Data.Common;
using System.Numerics;
using ImGuiNET;
using ReeLib.via;

namespace ContentEditor.App.Graphics;

public class GizmoState(Scene scene, GizmoContainer container)
{
    private List<(int id, HandleContainer handles)> previousChildren = new();
    private List<(int id, HandleContainer handles)> children = new();

    private (int id, int handleId)? activeHandle;
    private Vector3 activeHandleStartOffset;
    private Vector3 activeHandleStartWorldPosition;
    private Vector3 activeHandleStartAxis;

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
        foreach (var q in DrawListQueue) {
            q.Invoke();
        }
        DrawListQueue.Clear();
    }

    public void Push()
    {
        children.Add((children.Count, new HandleContainer()));
    }

    private static bool CheckOverlapLine2D(Vector2 cap1, Vector2 cap2, float radius, Vector2 point)
    {
        var v1 = cap2 - cap1;
        var d = v1.LengthSquared();
        if (d < 0.00001f) {
            return Vector2.DistanceSquared(point, cap1) < radius * radius;
        }

        d = Math.Clamp(((point.X - cap1.X) * v1.X + (point.Y - cap1.Y) * v1.Y) / d, 0, 1);
        point.X -= cap1.X + v1.X * d;
        point.Y -= cap1.Y + v1.Y * d;
        return point.LengthSquared() < radius * radius;
    }

    public enum Axis { X, Y, Z }

    public bool ArrowHandle(ref Vector3 position, out int handleId, Vector3 axis, Axis axisType, float handleRadius = 0.5f)
    {
        if (axis == Vector3.Zero) return PositionHandle(ref position, out handleId, handleRadius, axis, true);
        var cylinderEnd = position + axis * 0.8f;
        var coneEnd = position + axis;

        var current = children.Last();
        handleId = current.handles.count++;
        if (activeHandle != null && activeHandle != (current.id, handleId) || Scene.MouseHandler == null) {
            container.PushMaterial((GizmoMaterialPreset)axisType, ShapeBuilder.GeometryType.Filled);
            container.Add(new Cylinder(position, cylinderEnd, handleRadius * 0.08f));
            container.Add(new Cone(cylinderEnd, handleRadius * 0.5f, coneEnd, 0.0001f));
            container.PopMaterial();
            return false;
        }
        var pos1 = Scene.ActiveCamera.WorldToScreenPosition(position, false, true);
        var pos2 = Scene.ActiveCamera.WorldToScreenPosition(position + axis, false, true);
        var pos1Offset = Scene.ActiveCamera.WorldToScreenPosition(position + Scene.ActiveCamera.Transform.Right * handleRadius);
        var screenRadius = (pos1 - pos1Offset).Length() * 0.5f;
        if (activeHandle == null) {
            var mouse = Scene.MouseHandler.MouseScreenPosition;
            if (!Scene.MouseHandler.IsDragging) {
                if (CheckOverlapLine2D(pos1, pos2, screenRadius, mouse)) {
                    container.PushMaterial((GizmoMaterialPreset)(axisType + (int)GizmoMaterialPreset.AxisX_Highlight), ShapeBuilder.GeometryType.Filled);
                    if (Scene.MouseHandler.IsLeftDown) {
                        StartDragHandle(current.id, handleId, position, axis, mouse);
                    }
                } else {
                    container.PushMaterial((GizmoMaterialPreset)axisType, ShapeBuilder.GeometryType.Filled);
                }
            } else {
                container.PushMaterial((GizmoMaterialPreset)axisType, ShapeBuilder.GeometryType.Filled);
            }

            container.Add(new Cylinder(position, cylinderEnd, handleRadius * 0.08f));
            container.Add(new Cone(cylinderEnd, handleRadius * 0.5f, coneEnd, 0.0001f));
            container.PopMaterial();
        } else {
            if (!Scene.MouseHandler.IsLeftDown) {
                activeHandle = null;
            } else {
                var newPos = Scene.ActiveCamera.ScreenToWorldPositionReproject(Scene.MouseHandler.MouseScreenPosition, position) - activeHandleStartOffset;
                position = AlignToInitialAxis(newPos);
            }
            container.PushMaterial((GizmoMaterialPreset)(axisType + (int)GizmoMaterialPreset.AxisX_Active), ShapeBuilder.GeometryType.Filled);
            container.Add(new Cylinder(position, cylinderEnd, handleRadius * 0.08f));
            container.Add(new Cone(cylinderEnd, handleRadius * 0.5f, coneEnd, 0.0001f));
            container.PopMaterial();
            return true;
        }

        return false;
    }

    private void StartDragHandle(int id, int handleId, Vector3 position, Vector3 axis, Vector2 mouse)
    {
        activeHandle = (id, handleId);
        var mouseWorldPosition = Scene.ActiveCamera.ScreenToWorldPositionReproject(mouse, position);
        activeHandleStartOffset = mouseWorldPosition - position;
        activeHandleStartWorldPosition = position;
        activeHandleStartAxis = axis == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(axis);
    }

    public bool PositionHandle(ref Vector3 position, out int handleId, float handleSize = 5f, Vector3 primaryAxis = default, bool lockToAxis = false)
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
                    StartDragHandle(current.id, handleId, position, primaryAxis, mouse);
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
                position = Scene.ActiveCamera.ScreenToWorldPositionReproject(Scene.MouseHandler.MouseScreenPosition, position);
                if (activeHandleStartAxis != Vector3.Zero && lockToAxis) {
                    position = AlignToInitialAxis(position);
                }

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

    private Vector3 AlignToInitialAxis(Vector3 position)
    {
        Vector3 primaryAxis = activeHandleStartAxis;
        var totalOffset = position - activeHandleStartWorldPosition;
        var alignedOffset = totalOffset - totalOffset.ProjectOnPlane(primaryAxis);
        return activeHandleStartWorldPosition + primaryAxis * alignedOffset.Length() * (Vector3.Dot(alignedOffset, primaryAxis) > 0 ? 1 : -1);
    }
}
