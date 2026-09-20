using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(RSZObjectResource))]
public sealed class ObjectResourceHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<RSZObjectResource>();
        var field = context.GetEntityField<ObjectField>();
        if (context.children.Count == 0 || context.children[0].uiHandler == null) {
            var child = context.AddChild(context.label, instance.Instance, setter: (ctx, val) => instance.Instance = (RszInstance?)val!);
            WindowHandlerFactory.SetupRSZInstanceHandler(child);
        }
        var nested = field?.forceNested ?? instance.Instance.Fields.Length > 2;
        if (nested) {
            if (ImGui.TreeNode(context.label)) {
                context.children[0].ShowUI();
                ImGui.TreePop();
            }
        } else {
            context.children[0].ShowUI();
        }
    }
}
