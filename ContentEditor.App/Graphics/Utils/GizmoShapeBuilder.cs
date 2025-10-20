using System.Numerics;
using ImGuiNET;
using ReeLib.UVar;
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

    public bool EditableBoxed(in Matrix4X4<float> offset, object shape, out object? newShape, out int handleId)
    {
        newShape = null;
        state.Push(shape);
        AddBoxed(in offset, shape);
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
        var handlePoint = Vector3.Transform(sphere.pos + sphere.r * Vector3.UnitY, offsetMatrix.ToSystem());
        if (state.PositionHandle(ref handlePoint, out handleId, 10f)) {
            var center = Vector3.Transform(sphere.pos, offsetMatrix.ToSystem());
            var newRadius = (handlePoint - center).Length();
            sphere.r = newRadius;
            return true;
        }
        return false;
    }

    public unsafe bool EditableAABB(in Matrix4X4<float> offsetMatrix, ref AABB aabb, out int handleId)
    {
        // TODO verify
        var box = new OBB(mat4.Identity, aabb.Size);
        if (EditableOBB(offsetMatrix, ref box, out handleId)) {
            var center = aabb.Center;
            aabb = new AABB(center - box.Extent, center + box.Extent);
            return true;
        }
        return false;
    }

    public unsafe bool EditableOBB(in Matrix4X4<float> offsetMatrix, ref OBB box, out int handleId)
    {
        // TODO verify
        var pts = stackalloc Vector3[6];
        pts[0] = Vector3.Transform(box.Extent * UnitDirections[0], offsetMatrix.ToSystem() * box.Coord.ToSystem());
        pts[1] = Vector3.Transform(box.Extent * UnitDirections[1], offsetMatrix.ToSystem() * box.Coord.ToSystem());
        pts[2] = Vector3.Transform(box.Extent * UnitDirections[2], offsetMatrix.ToSystem() * box.Coord.ToSystem());
        pts[3] = Vector3.Transform(box.Extent * UnitDirections[3], offsetMatrix.ToSystem() * box.Coord.ToSystem());
        pts[4] = Vector3.Transform(box.Extent * UnitDirections[4], offsetMatrix.ToSystem() * box.Coord.ToSystem());
        pts[5] = Vector3.Transform(box.Extent * UnitDirections[5], offsetMatrix.ToSystem() * box.Coord.ToSystem());
        handleId = -1;
        for (int i = 0; i < 6; ++i) {
            var pt = pts[i];
            if (state.PositionHandle(ref pt, out var hid)) {
                var center = Vector3.Transform(Vector3.Zero, offsetMatrix.ToSystem() * box.Coord.ToSystem());
                var dist = (pt - center).Length();
                box.Extent = box.Extent + dist * UnitDirections[i];
                handleId = hid;
            }
        }
        return handleId != -1;
    }

    public bool EditableCapsule(in Matrix4X4<float> offsetMatrix, ref Capsule cap, out int handleId)
    {
        var cyl = new Cylinder(cap.p0, cap.p1, cap.R);
        if (EditableCylinder(offsetMatrix, ref cyl, out handleId)) {
            cap = new Capsule(cyl.p0, cyl.p1, cyl.r);
            return true;
        }

        return false;
    }

    public bool EditableCylinder(in Matrix4X4<float> offsetMatrix, ref Cylinder cap, out int handleId)
    {
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
        if (state.PositionHandle(ref handleSide, out hid, 10f, handleSide - (handleBot + handleTop) * 0.5f, ImGui.IsKeyDown(ImGuiKey.LeftShift))) {
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

    public void AddBoxed(object shape)
    {
        state.Push(shape);
        builder.AddBoxed(shape);
    }

    public void Add(in Matrix4X4<float> offsetMatrix, LineSegment shape)
        => builder.Add(new LineSegment(Vector3.Transform(shape.start, offsetMatrix.ToSystem()), Vector3.Transform(shape.end, offsetMatrix.ToSystem())));
    public void Add(in Matrix4X4<float> offsetMatrix, AABB shape)
        => builder.Add(new AABB(Vector3.Transform(shape.minpos, offsetMatrix.ToSystem()), Vector3.Transform(shape.maxpos, offsetMatrix.ToSystem())));
    public void Add(in Matrix4X4<float> offsetMatrix, OBB shape)
        => builder.Add(new OBB(offsetMatrix.ToSystem() * shape.Coord.ToSystem(), shape.Extent));
    public void Add(in Matrix4X4<float> offsetMatrix, Sphere shape)
        => builder.Add(new Sphere(Vector3.Transform(shape.pos, offsetMatrix.ToSystem()), shape.r));
    public void Add(in Matrix4X4<float> offsetMatrix, Capsule shape)
        => builder.Add(new Capsule(Vector3.Transform(shape.p0, offsetMatrix.ToSystem()), Vector3.Transform(shape.p1, offsetMatrix.ToSystem()), shape.R));
    public void Add(in Matrix4X4<float> offsetMatrix, Cylinder shape)
        => builder.Add(new Cylinder(Vector3.Transform(shape.p0, offsetMatrix.ToSystem()), Vector3.Transform(shape.p1, offsetMatrix.ToSystem()), shape.r));

    public void AddBoxed(in Matrix4X4<float> offsetMatrix, object shape)
    {
        state.Push(shape);
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
