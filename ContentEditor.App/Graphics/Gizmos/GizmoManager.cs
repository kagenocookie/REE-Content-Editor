using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App.Graphics;

public sealed class GizmoManager(Scene scene) : IDisposable
{
    private readonly Dictionary<IGizmoComponent, GizmoContainer> containers = new();
    private readonly List<GizmoContainer> componentGizmos = new();
    private readonly List<IGizmoComponent> removedComponents = new();
    private readonly Dictionary<Transform, GizmoContainer> worldSpaceGizmos = new();
    private readonly HashSet<Transform> notRequestedStandalones = new();
    private readonly List<(GizmoMaterialPreset preset, Material material)> presetMaterials = new();

    public GizmoContainer? ActiveContainer { get; internal set; }

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
            if (!scene.Root.Gizmos.components.Contains(comp)) {
                removedComponents.Add(comp);
            }
        }
        foreach (var cc in removedComponents) {
            if (containers.Remove(cc, out var container)) {
                container.Dispose();
            }
        }
        removedComponents.Clear();

        foreach (var comp in scene.Root.Gizmos.components) {
            if (!comp.IsEnabled || !((Component)comp).GameObject.ShouldDraw) continue;
            var localBounds = comp.Bounds;
            if (!localBounds.IsInvalid) {
                var aabb = localBounds.ToWorldBounds(((Component)comp).Transform.WorldTransform.ToSystem());
                if (!cam.IsVisible(aabb)) continue;
            }

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
            componentGizmos.Add(cg.Value);
        }

        foreach (var gizmo in componentGizmos) {
            foreach (var item in gizmo.meshDraws) {
                ogl.Batch.Gizmo.Add(item);
            }

            foreach (var shape in gizmo.shapeBuilders.Values.OrderByDescending(sb => sb.renderPriority)) {
                if (shape.mesh != null) {
                    // note: the shapes should've already been generated with their matrices applied
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

    public Material GetMaterial(GizmoMaterialPreset preset)
    {
        foreach (var pm in presetMaterials) {
            if (pm.preset == preset) {
                return pm.material;
            }
        }

        var material = preset switch {
            GizmoMaterialPreset.Default => scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "default").Color("_MainColor", new Color(0xff, 0xff, 0xff, 0xff)),
            GizmoMaterialPreset.AxisX => scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "axis_x").Color("_MainColor", new Color(0xff, 0, 0, 0xff)),
            GizmoMaterialPreset.AxisY => scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "axis_y").Color("_MainColor", new Color(0, 0xff, 0, 0xff)),
            GizmoMaterialPreset.AxisZ => scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "axis_z").Color("_MainColor", new Color(0, 0, 0xff, 0xff)),
            GizmoMaterialPreset.AxisX_Highlight => scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "axis_x").Color("_MainColor", new Color(0xff, 0xaa, 0xaa, 0xff)),
            GizmoMaterialPreset.AxisY_Highlight => scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "axis_y").Color("_MainColor", new Color(0xaa, 0xff, 0xaa, 0xff)),
            GizmoMaterialPreset.AxisZ_Highlight => scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "axis_z").Color("_MainColor", new Color(0xaa, 0xaa, 0xff, 0xff)),
            GizmoMaterialPreset.AxisX_Active => scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "axis_x").Color("_MainColor", new Color(0xff, 0xcc, 0xcc, 0xff)),
            GizmoMaterialPreset.AxisY_Active => scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "axis_y").Color("_MainColor", new Color(0xcc, 0xff, 0xcc, 0xff)),
            GizmoMaterialPreset.AxisZ_Active => scene.RenderContext.GetMaterialBuilder(BuiltInMaterials.MonoColor, "axis_z").Color("_MainColor", new Color(0xcc, 0xcc, 0xff, 0xff)),
            _ => throw new NotImplementedException($"Unsupported gizmo material preset {preset}"),
        };
        presetMaterials.Add((preset, material));
        return material;
    }
}
