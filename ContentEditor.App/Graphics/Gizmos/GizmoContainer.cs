using System.Diagnostics;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App.Graphics;

public enum GizmoMaterialPreset
{
    AxisX,
    AxisY,
    AxisZ,
    AxisX_Highlight,
    AxisY_Highlight,
    AxisZ_Highlight,
    AxisX_Active,
    AxisY_Active,
    AxisZ_Active,
}

public class GizmoContainer : IDisposable
{
    internal Dictionary<string, MeshHandle> ownedMeshes = new();
    internal List<GizmoRenderBatchItem> meshDraws = new();

    private readonly GizmoState state;

    private readonly Stack<GizmoShapeBuilder> shapeStack = new();
    private readonly Scene scene;

    // public IGizmoComponent GComponent { get; } = component;
    public Component Component { get; }

    internal Dictionary<int, GizmoShapeBuilder> shapeBuilders = new();

    public bool IsActive => scene.GizmoManager!.ActiveContainer == this;
    public bool CanActivate => scene.GizmoManager!.ActiveContainer == null || scene.GizmoManager!.ActiveContainer == this;

    public GizmoContainer(Scene scene, Component component)
    {
        this.scene = scene;
        Component = (Component)component;
        state = new(scene, this);
    }

    public void PushMaterial(Material material, Material? obscuredMaterial = null, ShapeBuilder.GeometryType geometryType = ShapeBuilder.GeometryType.Line, int priority = 0)
    {
        var hash = HashCode.Combine(material.Hash, obscuredMaterial?.Hash << 8);
        if (!shapeBuilders.TryGetValue(hash, out var sb)) {
            shapeBuilders[hash] = sb = new GizmoShapeBuilder(state);
            sb.material = material;
            sb.obscuredMaterial = obscuredMaterial;
            sb.Priority(priority);
            sb.GeometryType(geometryType);
        }
        shapeStack.Push(sb);
    }

    public void SetShapeGeometryType(ShapeBuilder.GeometryType type)
    {
        shapeStack.Peek().GeometryType(type);
    }

    public void PushMaterial(GizmoMaterialPreset material, ShapeBuilder.GeometryType geometryType = ShapeBuilder.GeometryType.Line, bool showObscured = true)
    {
        var mat = scene.GizmoManager!.GetMaterial(material);
        PushMaterial(mat, showObscured ? mat : null, geometryType);
    }

    public void GrabFocus()
    {
        Debug.Assert(scene.GizmoManager!.ActiveContainer == null);
        scene.GizmoManager!.ActiveContainer = this;
    }

    public void LoseFocus()
    {
        Debug.Assert(scene.GizmoManager!.ActiveContainer == this);
        scene.GizmoManager!.ActiveContainer = null;
    }

    private void LoseFocusSafe()
    {
        if (scene.GizmoManager?.ActiveContainer == this)
            scene.GizmoManager.ActiveContainer = null;
    }

    public void PopMaterial()
    {
        shapeStack.Pop();
    }

    public void BeginControl()
    {
        shapeStack.Peek().Push();
    }

    public void UpdateMesh()
    {
        foreach (var shape in shapeBuilders) {
            shape.Value.UpdateMesh();
        }
    }

    public void Clear()
    {
        meshDraws.Clear();
        foreach (var shape in shapeBuilders) {
            shape.Value.ClearShapes();
        }
    }

    public void DrawImGui()
    {
        state.FinishFrame();
    }

    public void Dispose()
    {
        foreach (var shape in shapeBuilders) {
            shape.Value.Dispose();
        }
        foreach (var (path, mesh) in ownedMeshes) {
            scene.RenderContext.UnloadMesh(mesh);
        }
        LoseFocusSafe();
        ownedMeshes.Clear();
    }

    public void Mesh(string meshPath, in Matrix4X4<float> transform, Material? materialOverride = null)
    {
        if (!ownedMeshes.TryGetValue(meshPath, out var mesh)) {
            mesh = scene.RenderContext.LoadMesh(meshPath);
            if (mesh != null) {
                ownedMeshes[meshPath] = mesh;
                mesh.Update();
            } else {
                ownedMeshes.Remove(meshPath);
                return;
            }
        }

        for (int i = 0; i < mesh.Handle.Meshes.Count; i++) {
            var sub = mesh.Handle.Meshes[i];
            if (!mesh.GetMeshPartEnabled(sub.MeshGroup)) continue;

            var material = materialOverride ?? mesh.GetMaterial(i);
            // TODO support material override
            meshDraws.Add(new GizmoRenderBatchItem(material, sub, transform));
        }
    }
    public void Mesh(MeshHandle mesh, in Matrix4X4<float> transform)
    {
        int i = 0;
        foreach (var m in mesh.Meshes) {
            meshDraws.Add(new GizmoRenderBatchItem(mesh.GetMaterial(i++), m, transform));
        }
    }

    public void Mesh(MeshHandle mesh, in Matrix4X4<float> transform, Material material)
    {
        foreach (var m in mesh.Meshes) {
            meshDraws.Add(new GizmoRenderBatchItem(material, m, transform));
        }
    }

    public void Mesh(MeshHandle mesh, in Matrix4X4<float> transform, Material material, Material obscuredMaterial)
    {
        foreach (var m in mesh.Meshes) {
            meshDraws.Add(new GizmoRenderBatchItem(material, m, transform, obscuredMaterial));
        }
    }

    public void Add(OBB shape) => shapeStack.Peek().Add(shape);
    public void Add(AABB shape) => shapeStack.Peek().Add(shape);
    public void Add(Cone shape) => shapeStack.Peek().Add(shape);
    public void Add(Sphere shape) => shapeStack.Peek().Add(shape);
    public void Add(Capsule shape) => shapeStack.Peek().Add(shape);
    public void Add(Cylinder shape) => shapeStack.Peek().Add(shape);
    public void Add(LineSegment shape) => shapeStack.Peek().Add(shape);

    public GizmoShapeBuilder Cur => shapeStack.Peek();
}
