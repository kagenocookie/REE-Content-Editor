using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(RSZObjectListResource))]
public sealed class ObjectListResourceHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var list = context.Get<RSZObjectListResource>().Instances;
        var field = context.GetEntityField<ObjectArray>()!;
        if (context.children.Count == 0) {
            if (field.Classname == null) {
                context.AddChild("", null, new FixedLabelHandler($"Missing classname for field {field.Field.name}", Colors.Error));
                return;
            }
            var child = context.AddChild(context.label, list);
            child.uiHandler = new ArrayRSZHandler(new RszField() {
                name = "",
                type = RszFieldType.Object,
                original_type = field.Classname,
            });
        }
        context.children[0].ShowUI();
    }
}
