using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ContentEditor;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Pfb;

namespace ContentEditor.App.ImguiHandling;

[ObjectImguiHandler(typeof(GameObject))]
public class GameObjectEditor : IObjectUIHandler
{
    private static readonly MemberInfo[] BaseMembers = [
        typeof(GameObject).GetProperty(nameof(GameObject.Name))!,
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
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(GameObject), members: BaseMembers);
            if (ws?.Env.Classes.GameObject.IndexOfField(nameof(GameObject.TimeScale)) != -1) {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(GameObject), members: BaseMembers2);
            }
            if (obj.folder != null) {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(GameObject), members: SceneMembers);
            }
            context.AddChild("Components", obj.Components, new ComponentListEditor());
        }

        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(Component))]
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

public class GameObjectChildListEditor : NodeTreeEditor<GameObject, GameObjectChildListEditor>
{
}
