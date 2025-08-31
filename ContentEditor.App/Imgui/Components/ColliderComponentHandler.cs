using System.Numerics;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

[RszClassHandler("via.physics.Collider")]
public class ColliderComponentHandler : IObjectUIHandler, IUIContextEventHandler
{
    private readonly NestedRszInstanceHandler inner = new NestedRszInstanceHandler();

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            WindowHandlerFactory.AddRszInstanceFieldChildren(context);
        }
        inner.OnIMGUI(context);
    }

    public bool HandleEvent(UIContext context, EditorUIEvent eventData)
    {
        var shapeCtx = context.children.FirstOrDefault(c => c.label == nameof(RszFieldCache.Collider.Shape));
        var eventShapeCtx = eventData.origin.FindInHierarchy(context, (c) => c == shapeCtx);
        if (eventShapeCtx != null) {
            var colliders = context.FindValueInParentValues<Colliders>();
            if (colliders != null) {
                colliders.UpdateColliderMesh(context.parent!.children.IndexOf(context));
            }
        }
        return true;
    }
}
