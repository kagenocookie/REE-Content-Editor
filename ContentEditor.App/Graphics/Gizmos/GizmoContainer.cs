using Silk.NET.Maths;

namespace ContentEditor.App.Graphics;

public class GizmoContainer(Scene scene, Component component) : IDisposable
{
    internal Dictionary<string, MeshHandle> ownedMeshes = new();
    internal List<GizmoRenderBatchItem> meshDraws = new();
    internal List<GizmoShapeBuilder> shapeBuilders = new();
    private readonly GizmoState state = new(scene);

    // public IGizmoComponent GComponent { get; } = component;
    public Component Component { get; } = (Component)component;

    public GizmoShapeBuilder Shape(int index, Material material) => Shape(index, material, null!);
    public GizmoShapeBuilder Shape(int index, Material material, Material obscuredMaterial)
    {
        while (shapeBuilders.Count <= index) {
            shapeBuilders.Add(new GizmoShapeBuilder(state));
        }
        var builder = shapeBuilders[index] ??= new GizmoShapeBuilder(state);
        builder.material = material;
        builder.obscuredMaterial = obscuredMaterial;
        return builder;
    }

    public void UpdateMesh()
    {
        foreach (var shape in shapeBuilders) {
            shape.UpdateMesh();
        }
    }

    public void Clear()
    {
        meshDraws.Clear();
        foreach (var shape in shapeBuilders) {
            shape.ClearShapes();
        }
    }

    public void DrawImGui()
    {
        state.FinishFrame();
    }

    public void Dispose()
    {
        foreach (var shape in shapeBuilders) {
            shape.Dispose();
        }
        foreach (var (path, mesh) in ownedMeshes) {
            scene.RenderContext.UnloadMesh(mesh);
        }
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
}
