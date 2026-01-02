using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ContentEditor;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.Pfb;

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

public class GameObjectNodeEditor : NodeTreeEditor<GameObject, GameObjectNodeEditor>
{
    public GameObjectNodeEditor()
    {
        nodeColor = Colors.GameObject;
    }

    protected override void ShowPrefixes(UIContext context)
    {
        var go = context.Get<GameObject>();
        if (go.Scene?.RootScene.IsActive != true) return;
        var drawSelf = go.ShouldDrawSelf;
        var drawParentHierarchy = (go.Parent?.ShouldDraw ?? go.Folder?.ShouldDraw) != false;
        if (!drawParentHierarchy) {
            ImGui.BeginDisabled();
            ImGui.Button((drawSelf ? AppIcons.Eye : AppIcons.EyeBlocked) + "##" + context.label);
            ImGui.EndDisabled();
        } else {
            if (ImGui.Button((drawSelf ? AppIcons.Eye : AppIcons.EyeBlocked) + "##" + context.label)) {
                go.ShouldDrawSelf = !drawSelf;
            }
        }
        ImGui.SameLine();
    }

    protected override void HandleContextMenu(GameObject node, UIContext context)
    {
        if (node.Scene?.RootScene.IsActive == true) {
            if (ImGui.Selectable("Focus in 3D view")) {
                node.Scene?.ActiveCamera.LookAt(node, false);
                ImGui.CloseCurrentPopup();
            }
        }
        if (ImGui.Selectable("New GameObject")) {
            var ws = context.GetWorkspace();
            var newgo = new GameObject("New_GameObject", ws!.Env, node.Folder, node.Scene);
            UndoRedo.RecordAddChild(context, newgo, node);
            newgo.MakeNameUnique();
            context.FindHandlerInParents<IInspectorController>()?.SetPrimaryInspector(newgo);
            ImGui.CloseCurrentPopup();
        }

        var parent = ((INodeObject<GameObject>)node).GetParent();
        if (parent == null) {
            // the sole root instance mustn't be deleted or duplicated (pfb)
            return;
        }

        if (ImGui.Selectable("Delete")) {
            UndoRedo.RecordRemoveChild(context, node);
            ImGui.CloseCurrentPopup();
        }
        if (ImGui.Selectable("Duplicate")) {
            var clone = node.Clone();
            UndoRedo.RecordAddChild<GameObject>(context, clone, parent, parent.GetChildIndex(node) + 1);
            clone.MakeNameUnique();
            var inspector = context.FindHandlerInParents<IInspectorController>();
            inspector?.SetPrimaryInspector(clone);
            ImGui.CloseCurrentPopup();
        }
    }
}
