using System.Data.Common;
using System.Numerics;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App.Graphics;

public class GizmoState(Scene scene, GizmoContainer container)
{
    private List<(int id, HandleContainer handles)> previousChildren = new();
    private List<(int id, HandleContainer handles)> children = new();

    private (int id, int handleId)? activeHandle;
    private bool isHandleActive;
    private Vector3 activeHandleStartOffset;
    private Vector3 activeHandleStartWorldPosition;
    private Quaternion activeHandleStartRotation;
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

#region Math
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

    private Vector3 ProjectMouseOnPlane(Vector3 origin, Vector3 axis, Vector2 mouse)
    {
        var ray = Scene.ActiveCamera.ScreenToRay(mouse);
        var d = Vector3.Dot(ray.dir, axis);
        if (MathF.Abs(d) < 0.00001f) {
            return new Vector3(float.MaxValue);
        }

        var t = Vector3.Dot(origin - ray.from, axis) / d;
        var p = ray.from + ray.dir * t;
        return p;
    }

    private bool CheckOverlapProjectedCircle(Vector3 position, Vector3 axis, Vector2 mouse, float radius, float selectionSize)
    {
        var projectedMousePos = ProjectMouseOnPlane(position, axis, mouse);
        var dist = Vector3.Distance(projectedMousePos, position);
        return MathF.Abs(dist - radius) < selectionSize;
    }
#endregion

    public enum Axis { X, Y, Z }

    public bool ArrowHandle(ref Vector3 position, out int handleId, Vector3 axis, Axis axisType, float handleRadius = 0.5f)
    {
        if (axis == Vector3.Zero) return PointHandle(ref position, out handleId, handleRadius, axis, true);
        var cylinderEnd = position + axis * 0.8f;
        var coneEnd = position + axis;

        var current = children.Last();
        handleId = current.handles.count++;
        if (activeHandle != null && activeHandle != (current.id, handleId) || !container.CanActivate) {
            container.PushMaterial((GizmoMaterialPreset)(axisType + (int)GizmoMaterialPreset.AxisX), ShapeBuilder.GeometryType.Filled);
            container.Add(new Cylinder(position, cylinderEnd, handleRadius * 0.08f));
            container.Add(new Cone(cylinderEnd, handleRadius * 0.5f, coneEnd, 0.0001f));
            container.PopMaterial();
            return false;
        }
        var pos1 = Scene.ActiveCamera.WorldToScreenPosition(position, false, true);
        var pos2 = Scene.ActiveCamera.WorldToScreenPosition(position + axis, false, true);
        if (!Scene.ActiveCamera.IsPointInViewport(pos1) && !Scene.ActiveCamera.IsPointInViewport(pos2)) {
            return false;
        }
        var pos1Offset = Scene.ActiveCamera.WorldToScreenPosition(position + Scene.ActiveCamera.Transform.Right * handleRadius, false, true);
        var screenRadius = (pos1 - pos1Offset).Length() * 0.5f;
        if (activeHandle == null || !isHandleActive) {
            var mouse = Scene.Mouse.MouseScreenPosition;
            if (!Scene.Mouse.IsDragging) {
                if (Scene.Mouse.IsViewportHovered && CheckOverlapLine2D(pos1, pos2, screenRadius, mouse)) {
                    ReserveHandle(current.id, handleId);
                    container.PushMaterial((GizmoMaterialPreset)(axisType + (int)GizmoMaterialPreset.AxisX_Highlight), ShapeBuilder.GeometryType.Filled);
                    if (Scene.Mouse.IsLeftDown) {
                        StartDragHandle(current.id, handleId, position, axis, mouse);
                    }
                } else {
                    UnreserveHandle(current.id, handleId);
                    container.PushMaterial((GizmoMaterialPreset)(axisType + (int)GizmoMaterialPreset.AxisX), ShapeBuilder.GeometryType.Filled);
                }
            } else {
                UnreserveHandle(current.id, handleId);
                container.PushMaterial((GizmoMaterialPreset)(axisType + (int)GizmoMaterialPreset.AxisX), ShapeBuilder.GeometryType.Filled);
            }

            container.Add(new Cylinder(position, cylinderEnd, handleRadius * 0.08f));
            container.Add(new Cone(cylinderEnd, handleRadius * 0.5f, coneEnd, 0.0001f));
            container.PopMaterial();
            return false;
        }

        if (!Scene.Mouse.IsLeftDown) {
            StopHandle();
        } else {
            var newPos = Scene.ActiveCamera.ScreenToWorldPositionReproject(Scene.Mouse.MouseScreenPosition, position) - activeHandleStartOffset;
            position = AlignToInitialAxis(newPos);
        }
        container.PushMaterial((GizmoMaterialPreset)(axisType + (int)GizmoMaterialPreset.AxisX_Active), ShapeBuilder.GeometryType.Filled);
        container.Add(new Cylinder(position, cylinderEnd, handleRadius * 0.08f));
        container.Add(new Cone(cylinderEnd, handleRadius * 0.5f, coneEnd, 0.0001f));
        container.PopMaterial();
        return true;
    }

    public bool RotationHandle(Vector3 position, ref Quaternion quaternion, out int handleId, Vector3 axis, Axis axisType, float handleSize = 50f)
    {
        handleId = -1;
        if (axis == Vector3.Zero) return false;

        var current = children.Last();
        handleId = current.handles.count++;
        if (activeHandle != null && activeHandle != (current.id, handleId) || !container.CanActivate) {
            container.PushMaterial((GizmoMaterialPreset)(axisType + (int)GizmoMaterialPreset.AxisX), ShapeBuilder.GeometryType.Line);
            container.Cur.AddCircle(position, axis, handleSize);
            container.PopMaterial();
            return false;
        }

        if (activeHandle == null || !isHandleActive) {
            var mouse = Scene.Mouse.MouseScreenPosition;
            if (!Scene.Mouse.IsDragging) {
                var screenPosition = Scene.ActiveCamera.WorldToScreenPosition(position);
                if (Scene.Mouse.IsViewportHovered && Scene.ActiveCamera.IsPointInViewport(screenPosition) && CheckOverlapProjectedCircle(position, axis, mouse, handleSize, handleSize * 0.1f)) {
                    ReserveHandle(current.id, handleId);
                    container.PushMaterial((GizmoMaterialPreset)(axisType + (int)GizmoMaterialPreset.AxisX_Highlight), ShapeBuilder.GeometryType.Line);
                    if (Scene.Mouse.IsLeftDown) {
                        var projectedMousePos = position + Vector3.Normalize(ProjectMouseOnPlane(position, axis, mouse) - position) * handleSize;
                        StartRotationHandle(current.id, handleId, position, axis, projectedMousePos);
                        activeHandleStartRotation = quaternion;
                    }
                } else {
                    UnreserveHandle(current.id, handleId);
                    container.PushMaterial((GizmoMaterialPreset)(axisType + (int)GizmoMaterialPreset.AxisX), ShapeBuilder.GeometryType.Line);
                }
            } else {
                UnreserveHandle(current.id, handleId);
                container.PushMaterial((GizmoMaterialPreset)(axisType + (int)GizmoMaterialPreset.AxisX), ShapeBuilder.GeometryType.Line);
            }

            container.Cur.AddCircle(position, axis, handleSize);
            container.PopMaterial();
            return false;
        }

        if (!Scene.Mouse.IsLeftDown) {
            StopHandle();
        } else {
            var len = activeHandleStartOffset.Length();
            var initialVec = activeHandleStartOffset;
            axis = activeHandleStartAxis;
            var currentVec = Vector3.Normalize(ProjectMouseOnPlane(position, axis, Scene.Mouse.MouseScreenPosition) - position) * len;
            var angle = TransformExtensions.SignedAngleBetween(initialVec, currentVec, axis);

            if (ImGui.IsKeyDown(ImGuiKey.LeftCtrl)) { // snap to 15 deg
                var angleDeg = angle * 180 / MathF.PI;
                angleDeg = MathF.Round(angleDeg / 15) * 15;
                angle = angleDeg * MathF.PI / 180;
                currentVec = Vector3.Transform(initialVec, Quaternion.CreateFromAxisAngle(axis, angle));
            }
            var rotationDelta = Quaternion.CreateFromAxisAngle(axis, angle);
            container.PushMaterial(GizmoMaterialPreset.Default, ShapeBuilder.GeometryType.Line);
            container.Cur.Add(new LineSegment(position, position + initialVec));
            container.Cur.Add(new LineSegment(position, position + currentVec));
            container.PopMaterial();
            quaternion = Quaternion.Normalize(rotationDelta * activeHandleStartRotation);
            DebugUI.DrawKeyed("Rotated angle", $"{angle * 180 / MathF.PI}°");
        }
        container.PushMaterial((GizmoMaterialPreset)(axisType + (int)GizmoMaterialPreset.AxisX_Active), ShapeBuilder.GeometryType.Line);
        container.Cur.AddCircle(position, axis, handleSize);
        container.PopMaterial();
        return true;
    }

    public bool PointHandle(ref Vector3 position, out int handleId, float handleSize = 5f, Vector3 primaryAxis = default, bool lockToAxis = false)
    {
        var current = children.Last();
        handleId = current.handles.count++;
        var screenPosition = Scene.ActiveCamera.WorldToScreenPosition(position);
        if (container.CanActivate && (activeHandle == null || !isHandleActive)) {
            var mouse = Scene.Mouse.MouseScreenPosition;
            if (Scene.Mouse.IsViewportHovered && (mouse - screenPosition).LengthSquared() < handleSize * handleSize && !Scene.Mouse.IsDragging) {
                ReserveHandle(current.id, handleId);
                // TODO not ideal - needless allocations - optimize later
                DrawListQueue.Add(() => {
                    ImGui.GetWindowDrawList().AddCircleFilled(screenPosition, handleSize, ColorHandleBorder);
                    ImGui.GetWindowDrawList().AddCircleFilled(screenPosition, handleSize * HandleBorderSize, ColorHandleFillHovered);
                });
                if (Scene.Mouse.IsLeftDown) {
                    StartDragHandle(current.id, handleId, position, primaryAxis, mouse);
                }
            } else {
                UnreserveHandle(current.id, handleId);
                DrawListQueue.Add(() => {
                    ImGui.GetWindowDrawList().AddCircleFilled(screenPosition, handleSize, ColorHandleBorder);
                    ImGui.GetWindowDrawList().AddCircleFilled(screenPosition, handleSize * HandleBorderSize, ColorHandleFill);
                });
            }
        } else if (activeHandle == (current.id, handleId)) {
            if (!Scene.Mouse.IsLeftDown) {
                StopHandle();
            } else {
                position = Scene.ActiveCamera.ScreenToWorldPositionReproject(Scene.Mouse.MouseScreenPosition, position);
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

    private void StartDragHandle(int id, int handleId, Vector3 position, Vector3 axis, Vector2 mouse)
    {
        ReserveHandle(id, handleId);
        isHandleActive = true;
        var mouseWorldPosition = Scene.ActiveCamera.ScreenToWorldPositionReproject(mouse, position);
        activeHandleStartOffset = mouseWorldPosition - position;
        activeHandleStartWorldPosition = position;
        activeHandleStartAxis = axis == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(axis);
    }

    private void StartRotationHandle(int id, int handleId, Vector3 position, Vector3 axis, Vector3 mouseWorldPosition)
    {
        ReserveHandle(id, handleId);
        isHandleActive = true;
        activeHandleStartOffset = mouseWorldPosition - position;
        activeHandleStartWorldPosition = position;
        activeHandleStartAxis = axis == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(axis);
    }

    private void ReserveHandle(int id, int handleId)
    {
        container.GrabFocus();
        activeHandle = (id, handleId);
    }
    private void UnreserveHandle(int id, int handleId)
    {
        if (activeHandle == (id, handleId)) {
            container.LoseFocusSafe();
            activeHandle = null;
            isHandleActive = false;
        }
    }

    private void StopHandle()
    {
        activeHandle = null;
        container.LoseFocus();
        isHandleActive = false;
    }

    private Vector3 AlignToInitialAxis(Vector3 position)
    {
        Vector3 primaryAxis = activeHandleStartAxis;
        var totalOffset = position - activeHandleStartWorldPosition;
        var alignedOffset = totalOffset - totalOffset.ProjectOnPlane(primaryAxis);
        return activeHandleStartWorldPosition + primaryAxis * alignedOffset.Length() * (Vector3.Dot(alignedOffset, primaryAxis) > 0 ? 1 : -1);
    }
}
