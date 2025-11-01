using Silk.NET.Maths;

namespace ContentEditor.App.Graphics;

public sealed class GizmoManager(Scene scene) : IDisposable
{
    private readonly Dictionary<IGizmoComponent, GizmoContainer> containers = new();
    private readonly List<GizmoContainer> componentGizmos = new();
    private readonly List<IGizmoComponent> removedComponents = new();
    private readonly Dictionary<Transform, GizmoContainer> worldSpaceGizmos = new();
    private readonly HashSet<Transform> notRequestedStandalones = new();

    public void Update()
    {
        UpdateActiveGizmos();
        foreach (var gizmo in componentGizmos) {
            gizmo.UpdateMesh();
        }
    }

    private void UpdateActiveGizmos()
    {
        var cam = scene.ActiveCamera;
        componentGizmos.Clear();
        foreach (var stand in worldSpaceGizmos) {
            notRequestedStandalones.Add(stand.Key);
            stand.Value.Clear();
        }

        foreach (var (comp, cont) in containers) {
            if (!scene.Gizmos.components.Contains(comp)) {
                removedComponents.Add(comp);
            }
        }
        foreach (var cc in removedComponents) {
            if (containers.Remove(cc, out var container)) {
                container.Dispose();
            }
        }
        removedComponents.Clear();

        foreach (var comp in scene.Gizmos.components) {
            if (!comp.IsEnabled || !((Component)comp).GameObject.ShouldDraw) continue;
            var aabb = comp.Bounds.ToWorldBounds(((Component)comp).Transform.WorldTransform.ToSystem());
            if (!aabb.IsInvalid && !cam.IsVisible(aabb)) continue;

            var prevContainer = containers.GetValueOrDefault(comp);
            prevContainer?.Clear();
            var newContainer = comp.Update(prevContainer);
            if (prevContainer != newContainer) {
                if (prevContainer != null) {
                    prevContainer.Dispose();
                }
            }
            if (newContainer != null) {
                containers[comp] = newContainer;
                componentGizmos.Add(newContainer);
            }
        }
    }

    public GizmoContainer GetOrAddStandaloneGizmo(Transform transform)
    {
        notRequestedStandalones.Remove(transform);
        if (worldSpaceGizmos.TryGetValue(transform, out var gg)) {
            return gg;
        }

        return worldSpaceGizmos[transform] = new GizmoContainer(scene, transform);
    }

    public void RemoveStandaloneGizmo(Transform transform)
    {
        worldSpaceGizmos.GetValueOrDefault(transform)?.Dispose();
    }

    public void Render()
    {
        var ogl = (scene.RenderContext as OpenGLRenderContext);
        if (ogl == null) return;

        foreach (var nonreq in notRequestedStandalones) {
            if (worldSpaceGizmos.Remove(nonreq, out var gizmo)) {
                gizmo.Dispose();
            }
        }
        notRequestedStandalones.Clear();

        foreach (var cg in worldSpaceGizmos) {
            cg.Value.UpdateMesh();
        }
        // componentGizmos.AddRange(standaloneGizmos.Values);
        foreach (var gizmo in componentGizmos) {
            foreach (var item in gizmo.meshDraws) {
                ogl.Batch.Gizmo.Add(item);
            }

            foreach (var shape in gizmo.shapeBuilders.OrderByDescending(sb => sb.renderPriority)) {
                if (shape.mesh != null) {
                    ref readonly var transform = ref gizmo.Component.Transform.WorldTransform;
                    ogl.Batch.Gizmo.Add(new GizmoRenderBatchItem(shape.material, shape.mesh, transform, shape.obscuredMaterial));
                }
            }
        }
        foreach (var cg in worldSpaceGizmos) {
            foreach (var item in cg.Value.meshDraws) {
                ogl.Batch.Gizmo.Add(item);
            }

            foreach (var shape in cg.Value.shapeBuilders.OrderByDescending(sb => sb.renderPriority)) {
                if (shape.mesh != null) {
                    ogl.Batch.Gizmo.Add(new GizmoRenderBatchItem(shape.material, shape.mesh, Matrix4X4<float>.Identity, shape.obscuredMaterial));
                }
            }
        }
    }

    public void RenderUI()
    {
        foreach (var gizmo in componentGizmos) {
            gizmo.DrawImGui();
        }
        foreach (var gg in worldSpaceGizmos) {
            gg.Value.DrawImGui();
        }
    }

    public void Dispose()
    {
        foreach (var cont in containers) {
            cont.Value.Dispose();
        }
        foreach (var gg in worldSpaceGizmos) {
            gg.Value.Dispose();
        }
        containers.Clear();
    }
}
