using Silk.NET.Maths;

namespace ContentEditor.App.Graphics;

public sealed class GizmoManager(Scene scene) : IDisposable
{
    private readonly Dictionary<IGizmoComponent, GizmoContainer> containers = new();
    private readonly List<GizmoContainer> currentGizmos = new();
    private readonly List<IGizmoComponent> removedComponents = new();

    public void Update()
    {
        UpdateActiveGizmos();
        foreach (var gizmo in currentGizmos) {
            gizmo.UpdateMesh();
        }
    }

    private void UpdateActiveGizmos()
    {
        var cam = scene.ActiveCamera;
        currentGizmos.Clear();

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
                    // TODO dispose?
                }
            }
            if (newContainer != null) {
                containers[comp] = newContainer;
                currentGizmos.Add(newContainer);
            }
        }
    }

    public void Render()
    {
        var ogl = (scene.RenderContext as OpenGLRenderContext);
        if (ogl == null) return;

        foreach (var gizmo in currentGizmos) {
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
    }

    public void RenderUI()
    {
        foreach (var gizmo in currentGizmos) {
            gizmo.DrawImGui();
        }
    }

    public void Dispose()
    {
        foreach (var cont in containers) {
            cont.Value.Dispose();
        }
        containers.Clear();
    }
}
