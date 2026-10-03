using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(RSZObjectListResource))]
public sealed class ObjectListResourceHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var list = context.Get<RSZObjectListResource>();
        var field = context.GetEntityField<ObjectArray>()!;
        if (context.children.Count == 0) {
            // prioritize the actual resource's classname to cover subtypes correctly (when merged-group)
            var classname = list.ResourceType.RszClass?.name ?? field.Classname;
            if (classname == null) {
                context.AddChild("", null, new FixedLabelHandler($"Missing classname for field {field.Field.name}", Colors.Error));
                return;
            }
            var child = context.AddChild(context.label, list.Instances);
            child.uiHandler = new ArrayRSZHandler(new RszField() {
                name = "",
                type = RszFieldType.Object,
                original_type = classname,
            });
        }
        context.children[0].ShowUI();
    }
}
