using System.Numerics;
using ContentEditor.Core;
using ImGuiNET;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App.Graphics;

public class GizmoShapeBuilder : IDisposable
{
    public Material material;
    public Material? obscuredMaterial;
    private GizmoState state;
    private ShapeBuilder builder = new() { GeoType = ShapeBuilder.GeometryType.Line };
    private int lastShapeHash;

    public ShapeMesh? mesh;
    public int renderPriority;

    private static readonly Vector3[] UnitDirections = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];

    public GizmoShapeBuilder(GizmoState state)
    {
        this.state = state;
        material = null!;
    }

    public void ClearShapes()
    {
        builder.Clear();
    }

    public void UpdateMesh()
    {
        var ogl = state.Scene.RenderContext as OpenGLRenderContext;
        if (ogl == null) return;

        if (builder.IsEmpty) {
            if (mesh != null) {
                mesh.Dispose();
                mesh = null;
            }
            lastShapeHash = 0;
            return;
        }

        // reuse previous vertex data if nothing changed
        var hash = builder.CalculateShapeHash();
        if (hash == lastShapeHash) return;

        if (mesh == null) {
            mesh = new ShapeMesh(ogl.GL);
        }

        lastShapeHash = hash;
        mesh.Build(builder);
    }

    public GizmoShapeBuilder Priority(int renderPriority)
    {
        this.renderPriority = renderPriority;
        return this;
    }

    /// <summary>
    /// Pushes a new interactable object to the context stack.
    /// </summary>
    public GizmoShapeBuilder Push()
    {
        state.Push();
        return this;
    }

    public GizmoShapeBuilder GeometryType(ShapeBuilder.GeometryType geometryType)
    {
        builder.GeoType = geometryType;
        return this;
    }

    public bool EditableBoxed(in Matrix4X4<float> offset, object shape, out object? newShape, out int handleId)
    {
        newShape = null;
        switch (shape) {
            case AABB obj: if (EditableAABB(offset, ref obj, out handleId)) { newShape = obj; return true; } return false;
            case OBB obj: if (EditableOBB(offset, ref obj, out handleId)) { newShape = obj; return true; } return false;
            case Sphere obj: if (EditableSphere(offset, ref obj, out handleId)) { newShape = obj; return true; } return false;
            case Capsule obj: if (EditableCapsule(offset, ref obj, out handleId)) { newShape = obj; return true; } return false;
            case Cylinder obj: if (EditableCylinder(offset, ref obj, out handleId)) { newShape = obj; return true; } return false;
            default:
                Logger.Error("Unsupported shape type " + (shape?.GetType().Name ?? "NULL"));
                handleId = -1;
                return false;
        }
    }

    public bool EditableSphere(in Matrix4X4<float> offsetMatrix, ref Sphere sphere, out int handleId)
    {
        Add(in offsetMatrix, sphere);
        var handlePoint = Vector3.Transform(sphere.pos + sphere.r * Vector3.UnitY, offsetMatrix.ToSystem());
        handleId = -1;
        if (state.PositionHandle(ref handlePoint, out var hid, 10f)) {
            handleId = hid;
            var worldPos = Vector3.Transform(sphere.pos, offsetMatrix.ToSystem());
            var newRadius = (handlePoint - worldPos).Length();
            sphere.r = newRadius;
        }

        if (PositionHandles(Matrix4x4.CreateTranslation(sphere.pos) * offsetMatrix.ToSystem(), out var newpos, out hid)) {
            handleId = hid;
            Matrix4x4.Invert(offsetMatrix.ToSystem(), out var inverse);
            sphere.pos = Vector3.Transform(newpos, inverse);
        }

        return handleId != -1;
    }

    public bool TransformHandle(Transform transform)
    {
        var pos = transform.LocalPosition;
        var handleId = -1;
        if (PositionHandles(transform.WorldTransform.ToSystem(), out var newWorldPos, out var hid)) {
            handleId = hid;
            var parentMatr = transform.GameObject.Parent?.WorldTransform ?? Matrix4X4<float>.Identity;
            var newLocalPos = Vector3.Transform(newWorldPos, parentMatr.ToSystem());
            UndoRedo.RecordCallbackSetter(null, transform, pos, newLocalPos, (t, v) => t.LocalPosition = v, $"{transform.GetHashCode()}p");
        }

        if (handleId != -1) {
            return true;
        } else {
            return false;
        }
    }

    public bool PositionHandles(in Matrix4x4 localToWorldMatrix, out Vector3 newWorldPosition, out int handleId)
    {
        var worldPosition = Vector3.Transform(Vector3.Zero, localToWorldMatrix);

        var camdist = (worldPosition - state.Scene.ActiveCamera.Transform.Position).Length();
        var handleLengthScale = camdist * 0.2f;
        var uiScale = camdist * 0.03f;

        var up = Vector3.Transform(Vector3.UnitY * handleLengthScale, localToWorldMatrix);
        var right = Vector3.Transform(Vector3.UnitX * handleLengthScale, localToWorldMatrix);
        var fwd = Vector3.Transform(-Vector3.UnitZ * handleLengthScale, localToWorldMatrix);
        var upAxis = (up - worldPosition);
        var rightAxis = (right - worldPosition);
        var backAxis = (fwd - worldPosition);
        handleId = -1;
        if (state.ArrowHandle(ref worldPosition, out var hid, upAxis, GizmoState.Axis.Y, uiScale)) {
            handleId = hid;
        }
        if (state.ArrowHandle(ref worldPosition, out hid, rightAxis, GizmoState.Axis.X, uiScale)) {
            handleId = hid;
        }
        if (state.ArrowHandle(ref worldPosition, out hid, backAxis, GizmoState.Axis.Z, uiScale)) {
            handleId = hid;
        }
        if (handleId != -1) {
            newWorldPosition = worldPosition;
            return true;
        } else {
            newWorldPosition = Vector3.Zero;
            return false;
        }
    }

    public unsafe bool EditableAABB(in Matrix4X4<float> offsetMatrix, ref AABB aabb, out int handleId)
    {
        Matrix4x4.Decompose(offsetMatrix.ToSystem(), out _, out _, out var offset);

        Add(aabb + offset);
        var center = aabb.Center;
        var worldCenter = Vector3.Transform(Vector3.Zero, offsetMatrix.ToSystem()) + center;
        var size = aabb.Size / 2;
        var pts = stackalloc Vector3[6];
        pts[0] = worldCenter + size * UnitDirections[0];
        pts[1] = worldCenter + size * UnitDirections[1];
        pts[2] = worldCenter + size * UnitDirections[2];
        pts[3] = worldCenter + size * UnitDirections[3];
        pts[4] = worldCenter + size * UnitDirections[4];
        pts[5] = worldCenter + size * UnitDirections[5];
        handleId = -1;
        for (int i = 0; i < 6; ++i) {
            var pt = pts[i];
            if (state.PositionHandle(ref pt, out var hid, 5, pt - worldCenter, ImGui.IsKeyDown(ImGuiKey.LeftShift))) {
                var previousDist = (size * UnitDirections[i]).Length();
                var newDist = ((pt - worldCenter) * UnitDirections[i]).Length();
                var deltaDist = (newDist - previousDist) * 0.5f;
                if (UnitDirections[i].X + UnitDirections[i].Y + UnitDirections[i].Z < 0) {
                    aabb.minpos += deltaDist * UnitDirections[i];
                } else {
                    aabb.maxpos += deltaDist * UnitDirections[i];
                }
                handleId = hid;
            }
        }

        var posMatrix = Matrix4x4.CreateTranslation(worldCenter);
        if (PositionHandles(posMatrix, out var newWorldCenter, out var posHandle)) {
            var worldOffset = newWorldCenter - worldCenter;
            aabb.minpos += worldOffset;
            aabb.maxpos += worldOffset;
            handleId = posHandle;
        }
        return handleId != -1;
    }

    public unsafe bool EditableOBB(in Matrix4X4<float> offsetMatrix, ref OBB box, out int handleId)
    {
        Add(in offsetMatrix, box);

        var center = box.Coord.Row3.ToVec3();
        var worldCenter = Vector3.Transform(center, offsetMatrix.ToSystem());
        var size = box.Extent;
        var pts = stackalloc Vector3[6];
        pts[0] = Vector3.Transform(center + size * UnitDirections[0], offsetMatrix.ToSystem());
        pts[1] = Vector3.Transform(center + size * UnitDirections[1], offsetMatrix.ToSystem());
        pts[2] = Vector3.Transform(center + size * UnitDirections[2], offsetMatrix.ToSystem());
        pts[3] = Vector3.Transform(center + size * UnitDirections[3], offsetMatrix.ToSystem());
        pts[4] = Vector3.Transform(center + size * UnitDirections[4], offsetMatrix.ToSystem());
        pts[5] = Vector3.Transform(center + size * UnitDirections[5], offsetMatrix.ToSystem());
        handleId = -1;
        for (int i = 0; i < 6; ++i) {
            var pt = pts[i];
            if (state.PositionHandle(ref pt, out var hid, 5, pt - worldCenter, ImGui.IsKeyDown(ImGuiKey.LeftShift))) {
                Matrix4x4.Invert(offsetMatrix.ToSystem(), out var invMat);
                var previousDist = (size * UnitDirections[i]).Length();
                var newDist = ((Vector3.Transform(pt, invMat) - center) * UnitDirections[i]).Length();
                var deltaDist = (newDist - previousDist) * 0.5f;
                if (UnitDirections[i].X + UnitDirections[i].Y + UnitDirections[i].Z < 0) {
                    box.Extent -= deltaDist * UnitDirections[i];
                } else {
                    box.Extent += deltaDist * UnitDirections[i];
                }
                handleId = hid;
            }
        }

        var posMatrix = Matrix4x4.CreateTranslation(center) * offsetMatrix.ToSystem();
        if (PositionHandles(posMatrix, out var newCenter, out var posHandle)) {
            Matrix4x4.Invert(offsetMatrix.ToSystem(), out var invMat);
            var localNewCenter = Vector3.Transform(newCenter, invMat);
            box.Coord = Matrix4x4.CreateTranslation(localNewCenter - center) * box.Coord.ToSystem();
            handleId = posHandle;
        }

        // TODO add rotation gizmo
        return handleId != -1;
    }

    public bool EditableCapsule(in Matrix4X4<float> offsetMatrix, ref Capsule cap, out int handleId)
    {
        Add(in offsetMatrix, cap);
        var cyl = new Cylinder(cap.p0, cap.p1, cap.R);
        if (EditableCylinder(offsetMatrix, ref cyl, out handleId)) {
            cap = new Capsule(cyl.p0, cyl.p1, cyl.r);
            return true;
        }

        return false;
    }

    public bool EditableCylinder(in Matrix4X4<float> offsetMatrix, ref Cylinder cap, out int handleId)
    {
        Add(in offsetMatrix, cap);
        var radius = MathF.Max(cap.r, 0.0001f);
        var up = (cap.p1 - cap.p0);
        var right = Vector3.UnitX * radius;
        if (up == Vector3.Zero) {
            up = Vector3.UnitY * radius;
        } else {
            up = Vector3.Normalize(up) * radius;
            right = Vector3.Normalize(Vector3.Cross(up, new Vector3(-up.Y, -up.Z, up.X))) * radius;
        }

        handleId = -1;
        var handleTop = Vector3.Transform(cap.p1, offsetMatrix.ToSystem());
        var handleBot = Vector3.Transform(cap.p0, offsetMatrix.ToSystem());
        var handleSide = Vector3.Transform((cap.p0 + cap.p1) * 0.5f + right, offsetMatrix.ToSystem());
        if (state.PositionHandle(ref handleTop, out var hid, 10f, handleTop - handleBot, ImGui.IsKeyDown(ImGuiKey.LeftShift))) {
            Matrix4X4.Invert(offsetMatrix, out var inverted);
            cap.p1 = Vector3.Transform(handleTop, inverted.ToSystem());
            handleId = hid;
        }
        if (state.PositionHandle(ref handleBot, out hid, 10f, handleTop - handleBot, ImGui.IsKeyDown(ImGuiKey.LeftShift))) {
            Matrix4X4.Invert(offsetMatrix, out var inverted);
            cap.p0 = Vector3.Transform(handleBot, inverted.ToSystem());
            handleId = hid;
        }
        if (state.PositionHandle(ref handleSide, out hid, 10f, handleSide - (handleBot + handleTop) * 0.5f, true)) {
            var center = Vector3.Transform((cap.p0 + cap.p1) * 0.5f, offsetMatrix.ToSystem());
            var newRadius = (handleSide - center).Length();
            cap.r = newRadius;
            handleId = hid;
        }

        return handleId != -1;
    }

    public void Add(LineSegment shape) => builder.Add(shape);
    public void Add(AABB shape) => builder.Add(shape);
    public void Add(OBB shape) => builder.Add(shape);
    public void Add(Sphere shape) => builder.Add(shape);
    public void Add(Capsule shape) => builder.Add(shape);
    public void Add(Cylinder shape) => builder.Add(shape);
    public void Add(Cone shape) => builder.Add(shape);

    public void Add(in Matrix4X4<float> offsetMatrix, LineSegment shape)
        => builder.Add(new LineSegment(Vector3.Transform(shape.start, offsetMatrix.ToSystem()), Vector3.Transform(shape.end, offsetMatrix.ToSystem())));
    public void Add(in Matrix4X4<float> offsetMatrix, AABB shape)
    {
        // AABBs don't rotate, must only apply the offset position here
        Matrix4X4.Decompose(offsetMatrix, out _, out _, out var offset);
        builder.Add(shape + offset.ToSystem());
    }
    public void Add(in Matrix4X4<float> offsetMatrix, OBB shape)
        => builder.Add(new OBB(shape.Coord.ToSystem() * offsetMatrix.ToSystem(), shape.Extent));
    public void Add(in Matrix4X4<float> offsetMatrix, Sphere shape)
        => builder.Add(new Sphere(Vector3.Transform(shape.pos, offsetMatrix.ToSystem()), shape.r));
    public void Add(in Matrix4X4<float> offsetMatrix, Capsule shape)
        => builder.Add(new Capsule(Vector3.Transform(shape.p0, offsetMatrix.ToSystem()), Vector3.Transform(shape.p1, offsetMatrix.ToSystem()), shape.R));
    public void Add(in Matrix4X4<float> offsetMatrix, Cylinder shape)
        => builder.Add(new Cylinder(Vector3.Transform(shape.p0, offsetMatrix.ToSystem()), Vector3.Transform(shape.p1, offsetMatrix.ToSystem()), shape.r));

    public void AddBoxed(object shape) => builder.AddBoxed(shape);
    public void AddBoxed(in Matrix4X4<float> offsetMatrix, object shape)
    {
        switch (shape) {
            case AABB obj: Add(in offsetMatrix, obj); return;
            case OBB obj: Add(in offsetMatrix, obj); return;
            case Sphere obj: Add(in offsetMatrix, obj); return;
            case Capsule obj: Add(in offsetMatrix, obj); return;
            case Cylinder obj: Add(in offsetMatrix, obj); return;
            default:
                Logger.Error("Unsupported shape type " + (shape?.GetType().Name ?? "NULL"));
                break;
        }
    }

    public void Dispose()
    {
        mesh?.Dispose();
    }
}
