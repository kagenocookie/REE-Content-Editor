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
    private ShapeBuilder builder = new();
    private int lastShapeHash;

    public ShapeMesh? mesh;

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

    public bool EditableBoxed(object shape, out object? newShape)
    {
        newShape = null;
        state.Push(shape);
        builder.AddBoxed(shape);
        switch (shape) {
            // case AABB obj: Add(obj); return;
            // case OBB obj: Add(obj); return;
            case Sphere obj: if (EditableSphere(ref obj)) { newShape = obj; return true; } return false;
            case Capsule obj: if (EditableCapsule(ref obj)) { newShape = obj; return true; } return false;
            // case Capsule obj: Add(obj); return;
            // case Cylinder obj: Add(obj); return;
            default:
                Logger.Error("Unsupported shape type " + (shape?.GetType().Name ?? "NULL"));
                return false;
        }
    }

    public bool EditableSphere(ref Sphere sphere)
    {
        var handlePoint = sphere.pos + sphere.r * Vector3.UnitY;
        if (state.PositionHandle(ref handlePoint, 0.5f)) {
            var newRadius = (handlePoint - sphere.pos).Length();
            sphere.r = newRadius;
        }
        return false;
    }

    public bool EditableCapsule(ref Capsule cap)
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

    public void Dispose()
    {
        mesh?.Dispose();
    }
}
