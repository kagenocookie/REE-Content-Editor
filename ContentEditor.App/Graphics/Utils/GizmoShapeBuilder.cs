using System.Numerics;
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

    public unsafe bool EditableSphere(ref Sphere sphere, out int handleId) => EditableSphere(Matrix4X4<float>.Identity, ref sphere, out handleId);
    public bool EditableSphere(in Matrix4X4<float> offsetMatrix, ref Sphere sphere, out int handleId)
    {
        Add(in offsetMatrix, sphere);
        var handlePoint = Vector3.Transform(sphere.pos + sphere.r * Vector3.UnitY, offsetMatrix.ToSystem());
        if (state.PositionHandle(ref handlePoint, out handleId, 10f)) {
            var center = Vector3.Transform(sphere.pos, offsetMatrix.ToSystem());
            var newRadius = (handlePoint - center).Length();
            sphere.r = newRadius;
            return true;
        }
        return false;
    }

    public unsafe bool EditableAABB(ref AABB aabb, out int handleId) => EditableAABB(Matrix4X4<float>.Identity, ref aabb, out handleId);
    public unsafe bool EditableAABB(in Matrix4X4<float> offsetMatrix, ref AABB aabb, out int handleId)
    {
        Add(in offsetMatrix, aabb);
        var pts = stackalloc Vector3[6];
        var center = aabb.Center;
        var worldCenter = Vector3.Transform(center, offsetMatrix.ToSystem());
        var size = aabb.Size / 2;
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
                    aabb.minpos += deltaDist * UnitDirections[i];
                    aabb.maxpos -= deltaDist * UnitDirections[i];
                } else {
                    aabb.minpos -= deltaDist * UnitDirections[i];
                    aabb.maxpos += deltaDist * UnitDirections[i];
                }
                handleId = hid;
            }
        }
        // TODO add position handle
        return handleId != -1;
    }

    public bool EditableOBB(ref OBB box, out int handleId) => EditableOBB(Matrix4X4<float>.Identity, ref box, out handleId);
    public bool EditableOBB(in Matrix4X4<float> offsetMatrix, ref OBB box, out int handleId)
    {
        Add(in offsetMatrix, box);
        var aabb = new AABB(-box.Extent, box.Extent);
        if (EditableAABB(offsetMatrix * box.Coord.ToGeneric(), ref aabb, out handleId)) {
            box.Extent = aabb.Size / 2;
        }
        // TODO add matrix gizmo
        return handleId != -1;
    }

    public bool EditableCapsule(ref Capsule cap, out int handleId) => EditableCapsule(Matrix4X4<float>.Identity, ref cap, out handleId);
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

    public bool EditableCylinder(ref Cylinder cap, out int handleId) => EditableCylinder(Matrix4X4<float>.Identity, ref cap, out handleId);
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

    public void Add(in Matrix4X4<float> offsetMatrix, LineSegment shape)
        => builder.Add(new LineSegment(Vector3.Transform(shape.start, offsetMatrix.ToSystem()), Vector3.Transform(shape.end, offsetMatrix.ToSystem())));
    public void Add(in Matrix4X4<float> offsetMatrix, AABB shape)
    {
        if (offsetMatrix.IsIdentity) {
            builder.Add(shape);
        } else {
            // if the matrix isn't identity, we can't draw it as a pure AABB shpae and need to redirect to OBB instead - probably
            // unless the game treats AABBs specially in that no rotations are made, in which case we could decompose and take only the translation from the matrix
            // TODO verify AABB shape matrix handling
            Add(offsetMatrix, new OBB(Matrix4x4.CreateTranslation(shape.Center), shape.Size / 2));
        }
    }
    public void Add(in Matrix4X4<float> offsetMatrix, OBB shape)
        => builder.Add(new OBB(offsetMatrix.ToSystem() * shape.Coord.ToSystem(), shape.Extent));
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
