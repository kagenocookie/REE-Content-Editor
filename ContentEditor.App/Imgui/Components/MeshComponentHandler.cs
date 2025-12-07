using System.Numerics;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

[ObjectImguiHandler(typeof(MeshComponent), Stateless = true, Priority = 0)]
public class MeshComponentHandler : BaseComponentEditor, IUIContextEventHandler
{
    public bool HandleEvent(UIContext context, EditorUIEvent eventData)
    {
        if (eventData.type is UIContextEvent.Changed or UIContextEvent.Reverted or UIContextEvent.Updated) {
            // we could be explicit about which fields to check (mesh, material, enableParts)
            // but we may in the future also add material parameters or other fields
            // may as well just refresh on any change
            context.Get<MeshComponent>().RefreshIfActive();
        }
        return true;
    }
}
