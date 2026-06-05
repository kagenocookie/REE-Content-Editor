using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using System.Numerics;
using System.Reflection;

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
            SetupDefaultUI(context);
        }
        context.ShowChildrenUI();
    }

    protected static UIContext SetupDefaultUI(UIContext context)
    {
        var component = context.Get<Component>();
        // #if DEBUG
        //             if (component.GetType() != typeof(Component)) {
        //                 var rawComponentChild = context.AddChild(context.label + " [DEBUG]", component);
        //                 WindowHandlerFactory.SetupObjectUIContext(rawComponentChild, component.GetType(), true, orderFunc: (f, i) => {
        //                     if (f.Name == nameof(Component.GameObject) || f.Name == nameof(Component.Data)) return -1;
        //                     return i;
        //                 });
        //                 rawComponentChild.uiHandler ??= new LazyPlainObjectHandler(component.GetType());
        //             }
        // #endif
        var child = context.AddChild(context.label, component.Data);
        WindowHandlerFactory.SetupRSZInstanceHandler(child);
        return child;
    }
}

public class ComponentDataHandler : IObjectUIHandler
{
    private readonly string classname;

    public ComponentDataHandler(string classname)
    {
        this.classname = classname;
    }

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<RszInstance>();
        context.annotation ??= (TranslatableBase?)WindowHandlerFactory.GetStringFormatter(instance) ?? FixedString.Cached(instance.RszClass.name);
        var show = DrawComponentTreeNode(context, instance);
        RszInstanceHandler.ShowDefaultContextMenu(context);
        if (show) {
            if (context.children.Count == 0) {
                if (instance.RSZUserData != null) {
                    context.AddChild(context.label, instance, new UserDataReferenceHandler());
                } else {
                    WindowHandlerFactory.SetupRSZInstanceHandler(context);
                }
            }
            ImGui.PushID(context.GetRaw()!.GetHashCode());
            ShowContents(context);
            ImGui.PopID();
            ImGui.TreePop();
        }
    }

    private bool DrawComponentTreeNode(UIContext context, RszInstance instance)
    {
        ImGui.BeginGroup();

        ReadOnlySpan<byte> label = context.label;
        ReadOnlySpan<byte> suffix = context.annotation.GetUTF8(instance);
        ImGui.PushID(label);

        // begin node header
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.231f, 0.231f, 0.231f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.35f, 0.35f, 0.35f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));


        var show = ImGui.TreeNodeEx("##treenode", ImGuiTreeNodeFlags.FramePadding |
                                                  ImGuiTreeNodeFlags.Framed |
                                                  ImGuiTreeNodeFlags.AllowOverlap);

        ImGui.PopStyleColor(3);
        // end node header

        object? componentFirstField = instance.Values.Length > 0 ? instance.Values[0] : null;
        if (componentFirstField is bool enabledBool) {
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.356f, 0.356f, 0.356f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.08f, 0.08f, 0.08f, 1.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2.0f);
            // hacky way to make the checkbox a bit smaller, i guess we'd need a custom one if we wanna make it smaller without doing this
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 1));

            if (ImGui.Checkbox("##chkbox", ref enabledBool)) {
                UndoRedo.RecordCallbackSetter(context, instance, instance.Values[0], enabledBool, (inst, val) => inst.Values[0] = val);
            }

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }
        ImGui.SameLine();

        // begin name labels
        // using class name here because i couldn't find an efficient way to strip the suffix from the label
        // it's also accurate to their editor ig
        ImGui.TextUnformatted(instance.RszClass.ShortName);
        ImGui.SameLine();
        ImGui.TextColored(Colors.Faded, suffix);
        // end name labels

        // begin context buttons
        ImGui.SameLine();

        float buttonWidth = ImGui.CalcTextSize($"{AppIcons.SI_Reset}").X + ImGui.GetStyle().FramePadding.X * 2;
        float avail = ImGui.GetContentRegionAvail().X;

        float rightEdge = ImGui.GetCursorScreenPos().X + avail;

        float posX = rightEdge - buttonWidth;

        ImGui.SetCursorScreenPos(new Vector2(posX, ImGui.GetCursorScreenPos().Y));

        ImGui.BeginDisabled(!context.IsChanged);
        if (ImGui.Button($"{AppIcons.SI_Reset}")) {
            try {
                context.Revert();
                context.ResetState();
                // some components throw this while being reverted. data seems to be fine if we catch this though
            } catch(NotImplementedException) {
            }
        }
        ImGui.EndDisabled();
        // end context buttons

        ImGui.PopID();
        ImGui.EndGroup();
        // hack: doing BeginGroup means the indent doesn't apply, but we need the group if we want to have a context menu trigger on both node and suffix
        // doing a manual indent fixes that
        if (show) ImGui.Indent(ImGui.GetStyle().IndentSpacing);
        return show;
    }

    protected virtual void ShowContents(UIContext context)
    {
        RszInstanceHandler.OnIMGUI(context, false);
    }
}
