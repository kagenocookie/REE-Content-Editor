using ContentPatcher;

namespace ContentEditor.App.ImguiHandling;

public record class InspectorComponentLink(Component Component, object? Object) : IVisibilityTarget
{
    public bool ShouldDrawSelf { get => Component.GameObject.ShouldDrawSelf; set => Component.GameObject.ShouldDrawSelf = value; }
    public bool ShouldDraw => Component.GameObject.ShouldDraw;
    public Scene? Scene => Component.GameObject.Scene;
    public IVisibilityTarget? Parent => Component.GameObject.Parent;
    public IEnumerable<IVisibilityTarget> VisibilityChildren => ((IVisibilityTarget)Component.GameObject).VisibilityChildren;

    public override string ToString() => $"{Component} | {Object}";
}

[ObjectImguiHandler(typeof(InspectorComponentLink))]
public class InspectorComponentLinkHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<InspectorComponentLink>();
        if (ImGui.Button(Lang.Buttons.Show_GameObject)) {
            var inspector = context.FindHandlerInParents<ObjectInspector>();
            inspector?.Target = instance.Component.GameObject;
        }
        ImGui.SameLine();
        ImGui.Text(instance.Component.ToString());
        ImGui.Separator();

        if (context.children.Count == 0) {
            context.AddChild("Object", instance.Object).AddDefaultHandler();
        }
        context.ShowChildrenUI();
    }
}