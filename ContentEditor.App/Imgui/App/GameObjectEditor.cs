using System.Reflection;
using ContentPatcher;

namespace ContentEditor.App.ImguiHandling;

[ObjectImguiHandler(typeof(GameObject), Stateless = true)]
public class GameObjectEditor : IObjectUIHandler
{
    private static readonly MemberInfo[] BaseMembers = [
        typeof(GameObject).GetField(nameof(GameObject.Tags))!,
        typeof(GameObject).GetField(nameof(GameObject.Update))!,
        typeof(GameObject).GetField(nameof(GameObject.Draw))!,
    ];
    private static readonly MemberInfo[] BaseMembers2 = [
        typeof(GameObject).GetField(nameof(GameObject.TimeScale))!,
    ];
    private static readonly MemberInfo[] SceneMembers = [
        typeof(GameObject).GetField(nameof(GameObject.guid))!,
        typeof(GameObject).GetField(nameof(GameObject.PrefabPath))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            var obj = context.Get<GameObject>();
            context.AddChild<GameObject, string>("Name", obj, new ConfirmedStringFieldHandler(), c => c!.Name, (c, v) => c.Name = v ?? string.Empty);
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(GameObject), members: BaseMembers);
            if (ws != null && RszFieldCache.GameObject.TimeScale.Exists(ws.Env.Classes.GameObject)) {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(GameObject), members: BaseMembers2);
            }
            if (obj.Folder != null) {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(GameObject), members: SceneMembers);
            }
            context.AddChild("Components", obj.Components, new ComponentListEditor());
        }

        context.ShowChildrenUI();
    }
}

public class GameObjectTreeEditor : SceneTreeEditor
{
    protected override IEnumerable<IVisibilityTarget> GetRootChildren(UIContext context)
    {
        yield return context.Get<GameObject>();
    }
}

[ObjectImguiHandler(typeof(Component), Stateless = true, Inherited = true, Priority = 10)]
public class BaseComponentEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var child = context.AddChild(context.label, context.Get<Component>().Data);
            child.AddDefaultHandler();
            if (child.uiHandler is NestedRszInstanceHandler nrsz) {
                nrsz.ForceDefaultClose = true;
            }
        }
        context.children[0].ShowUI();
    }
}
