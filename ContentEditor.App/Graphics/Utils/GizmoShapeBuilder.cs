using System.Numerics;
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

    public bool EditableBoxed(in Matrix4X4<float> offset, object shape, out object? newShape)
    {
        newShape = null;
        state.Push(shape);
        AddBoxed(in offset, shape);
        switch (shape) {
            // case AABB obj: Add(obj); return;
            // case OBB obj: Add(obj); return;
            case Sphere obj: if (EditableSphere(offset, ref obj)) { newShape = obj; return true; } return false;
            case Capsule obj: if (EditableCapsule(offset, ref obj)) { newShape = obj; return true; } return false;
            // case Capsule obj: Add(obj); return;
            // case Cylinder obj: Add(obj); return;
            default:
                Logger.Error("Unsupported shape type " + (shape?.GetType().Name ?? "NULL"));
                return false;
        }
    }

    public bool EditableSphere(in Matrix4X4<float> offsetMatrix, ref Sphere sphere)
    {
        var handlePoint = sphere.pos + sphere.r * Vector3.UnitY;
        if (state.PositionHandle(ref handlePoint, 0.5f)) {
            var newRadius = (handlePoint - sphere.pos).Length();
            sphere.r = newRadius;
        }
        return false;
    }

    public bool EditableCapsule(in Matrix4X4<float> offsetMatrix, ref Capsule cap)
    {
        return false;
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
