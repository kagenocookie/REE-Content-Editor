using System.Numerics;
using ContentEditor.Core;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App.Graphics;

public class GizmoShapeBuilder : IDisposable
{
    public Material material;
    public Material? obscuredMaterial;
    private GizmoState state;
    private ShapeBuilder builder = new(ShapeBuilder.GeometryType.Line, MeshLayout.PositionOnly);
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

    public void SetGeometryType(ShapeBuilder.GeometryType geometryType)
    {
        builder.GeoType = geometryType;
    }

    public bool EditableBoxed(in Matrix4x4 offset, object shape, out object? newShape, out int handleId)
    {
        newShape = null;
        switch (shape) {
            case AABB obj: if (EditableAABB(offset.ToGeneric(), ref obj, out handleId)) { newShape = obj; return true; } return false;
            case OBB obj: if (EditableOBB(offset.ToGeneric(), ref obj, out handleId)) { newShape = obj; return true; } return false;
            case Sphere obj: if (EditableSphere(offset.ToGeneric(), ref obj, out handleId)) { newShape = obj; return true; } return false;
            case Capsule obj: if (EditableCapsule(offset.ToGeneric(), ref obj, out handleId)) { newShape = obj; return true; } return false;
            case Cylinder obj: if (EditableCylinder(offset.ToGeneric(), ref obj, out handleId)) { newShape = obj; return true; } return false;
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
        if (state.PointHandle(ref handlePoint, out var hid, 10f)) {
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
        var handleId = -1;
        if (PositionHandles(transform.WorldTransform.ToSystem(), out var newWorldPos, out var hid)) {
            handleId = hid;
            var parentMat = transform.GameObject.Parent?.WorldTransform.ToSystem() ?? Matrix4x4.Identity;
            Matrix4x4.Invert(parentMat, out parentMat);
            var newLocalPos = Vector3.Transform(newWorldPos, parentMat);
            UndoRedo.RecordCallbackSetter(null, transform, transform.LocalPosition, newLocalPos, (t, v) => t.LocalPosition = v, $"{transform.GetHashCode()}p");
        }
        if (RotationHandles(transform.WorldTransform.ToSystem(), out var newQuat, out hid)) {
            handleId = hid;
            var parentMat = transform.GameObject.Parent?.WorldTransform.ToSystem() ?? Matrix4x4.Identity;
            var inverse = Quaternion.Inverse(Quaternion.CreateFromRotationMatrix(parentMat));
            var newRotation = inverse * newQuat;
            UndoRedo.RecordCallbackSetter(null, transform, transform.LocalRotation, newRotation, (t, v) => t.LocalRotation = v, $"{transform.GetHashCode()}r");
        }

        if (handleId != -1) {
            return true;
        } else {
            return false;
        }
    }

    public bool TransformHandle(in Matrix4x4 localToWorld, ReeLib.via.Transform transform, out ReeLib.via.Transform newTransform, out int handleId)
    {
        var pos = transform.pos;
        handleId = -1;

        var matrix = transform.ToMatrix() * localToWorld;
        var transformed = matrix * localToWorld;
        if (PositionHandles(transformed, out var newWorldPos, out var hid)) {
            handleId = hid;
            transform.pos = Vector3.Transform(newWorldPos, localToWorld);
        }
        if (RotationHandles(transformed, out var newQuat, out hid)) {
            handleId = hid;
            var inverse = Quaternion.Inverse(Quaternion.CreateFromRotationMatrix(localToWorld));
            transform.rot = inverse * newQuat;
        }

        newTransform = transform;
        return handleId != -1;
    }

    public bool PositionHandles(in Matrix4x4 localToWorldMatrix, out Vector3 newWorldPosition, out int handleId)
    {
        var worldPosition = Vector3.Transform(Vector3.Zero, localToWorldMatrix);

        var camdist = (worldPosition - state.Scene.ActiveCamera.Transform.Position).Length();
        var handleLengthScale = camdist * 0.2f;
        var uiScale = camdist * 0.03f;

        var upAxis = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, localToWorldMatrix)) * handleLengthScale;
        var rightAxis = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, localToWorldMatrix)) * handleLengthScale;
        var fwdAxis = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, localToWorldMatrix)) * handleLengthScale;
        handleId = -1;
        if (state.ArrowHandle(ref worldPosition, out var hid, upAxis, GizmoState.Axis.Y, uiScale)) {
            handleId = hid;
        }
        if (state.ArrowHandle(ref worldPosition, out hid, rightAxis, GizmoState.Axis.X, uiScale)) {
            handleId = hid;
        }
        if (state.ArrowHandle(ref worldPosition, out hid, fwdAxis, GizmoState.Axis.Z, uiScale)) {
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

    public bool RotationHandles(in Matrix4x4 localToWorldMatrix, out Quaternion newRotation, out int handleId)
    {
        var worldPosition = Vector3.Transform(Vector3.Zero, localToWorldMatrix);

        var camdist = (worldPosition - state.Scene.ActiveCamera.Transform.Position).Length();
        var handleSize = camdist * 0.24f;

        var upAxis = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, localToWorldMatrix));
        var rightAxis = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, localToWorldMatrix));
        var fwdAxis = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, localToWorldMatrix));
        handleId = -1;
        var rotation = Quaternion.CreateFromRotationMatrix(localToWorldMatrix);

        if (state.RotationHandle(worldPosition, ref rotation, out var hid, upAxis, GizmoState.Axis.Y, handleSize)) {
            handleId = hid;
        }
        if (state.RotationHandle(worldPosition, ref rotation, out hid, rightAxis, GizmoState.Axis.X, handleSize)) {
            handleId = hid;
        }
        if (state.RotationHandle(worldPosition, ref rotation, out hid, fwdAxis, GizmoState.Axis.Z, handleSize)) {
            handleId = hid;
        }
        newRotation = rotation;
        if (handleId != -1) {
            return true;
        } else {
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
            if (state.PointHandle(ref pt, out var hid, 5, pt - worldCenter, ImGui.IsKeyDown(ImGuiKey.LeftShift))) {
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

        var localCenter = box.Coord.Row3.ToVec3();
        var worldCenter = Vector3.Transform(localCenter, offsetMatrix.ToSystem());
        var size = box.Extent;
        var pts = stackalloc Vector3[6];
        var pointMtx = box.Coord.ToSystem() * offsetMatrix.ToSystem();
        pts[0] = Vector3.Transform(size * UnitDirections[0], pointMtx);
        pts[1] = Vector3.Transform(size * UnitDirections[1], pointMtx);
        pts[2] = Vector3.Transform(size * UnitDirections[2], pointMtx);
        pts[3] = Vector3.Transform(size * UnitDirections[3], pointMtx);
        pts[4] = Vector3.Transform(size * UnitDirections[4], pointMtx);
        pts[5] = Vector3.Transform(size * UnitDirections[5], pointMtx);
        handleId = -1;
        for (int i = 0; i < 6; ++i) {
            var pt = pts[i];
            if (state.PointHandle(ref pt, out var hid, 5, pt - worldCenter, true)) {
                Matrix4x4.Invert(pointMtx, out var invMat);
                var previousDist = (size * UnitDirections[i]).Length();
                var newDist = ((Vector3.Transform(pt, invMat)) * UnitDirections[i]).Length();
                var deltaDist = (newDist - previousDist) * 0.5f;
                if (UnitDirections[i].X + UnitDirections[i].Y + UnitDirections[i].Z < 0) {
                    box.Extent -= deltaDist * UnitDirections[i];
                } else {
                    box.Extent += deltaDist * UnitDirections[i];
                }
                handleId = hid;
            }
        }

        var posMatrix = Matrix4x4.CreateTranslation(localCenter) * offsetMatrix.ToSystem();
        if (PositionHandles(posMatrix, out var newCenter, out var posHandle)) {
            Matrix4x4.Invert(offsetMatrix.ToSystem(), out var invMat);
            var localNewCenter = Vector3.Transform(newCenter, invMat);
            box.Coord = box.Coord.ToSystem() * Matrix4x4.CreateTranslation(localNewCenter - localCenter);
            handleId = posHandle;
        }

        var orgLocalRotation = Quaternion.CreateFromRotationMatrix(box.Coord.ToSystem());
        var rotMatrix = Matrix4x4.CreateFromQuaternion(orgLocalRotation) * posMatrix;
        if (RotationHandles(rotMatrix, out var newQuat, out var hidd)) {
            handleId = hidd;
            Matrix4x4.Invert(posMatrix, out var invMat);
            var invRotation = Quaternion.CreateFromRotationMatrix(invMat);
            box.Coord = Matrix4x4.CreateFromQuaternion(Quaternion.Inverse(orgLocalRotation) * invRotation * newQuat) * box.Coord.ToSystem();
        }
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
        if (state.PointHandle(ref handleTop, out var hid, 10f, handleTop - handleBot, ImGui.IsKeyDown(ImGuiKey.LeftShift))) {
            Matrix4X4.Invert(offsetMatrix, out var inverted);
            cap.p1 = Vector3.Transform(handleTop, inverted.ToSystem());
            handleId = hid;
        }
        if (state.PointHandle(ref handleBot, out hid, 10f, handleTop - handleBot, ImGui.IsKeyDown(ImGuiKey.LeftShift))) {
            Matrix4X4.Invert(offsetMatrix, out var inverted);
            cap.p0 = Vector3.Transform(handleBot, inverted.ToSystem());
            handleId = hid;
        }
        if (state.PointHandle(ref handleSide, out hid, 10f, handleSide - (handleBot + handleTop) * 0.5f, true)) {
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

    public void AddCircle(Vector3 position, Vector3 forward, float handleSize) => builder.AddCircle(position, forward, handleSize);

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
    public void AddBoxed(in Matrix4x4 offsetMatrix, object shape)
    {
        switch (shape) {
            case AABB obj: Add(offsetMatrix.ToGeneric(), obj); return;
            case OBB obj: Add(offsetMatrix.ToGeneric(), obj); return;
            case Sphere obj: Add(offsetMatrix.ToGeneric(), obj); return;
            case Capsule obj: Add(offsetMatrix.ToGeneric(), obj); return;
            case Cylinder obj: Add(offsetMatrix.ToGeneric(), obj); return;
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
