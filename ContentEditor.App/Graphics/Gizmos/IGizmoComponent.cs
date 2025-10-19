using ReeLib.via;

namespace ContentEditor.App.Graphics;

public interface IGizmoComponent
{
    bool IsEnabled { get; }
    AABB Bounds => (this as RenderableComponent)?.WorldSpaceBounds ?? AABB.Invalid;
    GizmoContainer? Update(GizmoContainer? gizmo);
}
