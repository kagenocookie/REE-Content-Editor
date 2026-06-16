using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using System.Numerics;
using System.Reflection;

namespace ContentEditor.App.ImguiHandling;

[ObjectImguiHandler(typeof(GameObject), Stateless = true)]
public class GameObjectEditor : IObjectUIHandler
{
    private static readonly MemberInfo[] SceneMembers = [
        typeof(GameObject).GetField(nameof(GameObject.Guid))!,
        typeof(GameObject).GetField(nameof(GameObject.PrefabPath))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            var obj = context.Get<GameObject>();
            context.AddChild<GameObject, string>(Lang.Fields.Name, obj, StringFieldHandler.Instance, c => c!.Name, (c, v) => c.Name = v ?? string.Empty);
            context.AddChild<GameObject, string>(Lang.Fields.Tags, obj, new TagListStringHandler(), c => c!.Tags, (c, v) => c.Tags = v ?? string.Empty);
            context.AddChild<GameObject, GameObject>(Lang.Fields.Flags, obj, new GameObjectFlagsHandler());
            if (obj.Folder != null) {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(GameObject), members: SceneMembers);
            }
            context.AddChild(Lang.Fields.Components, obj.Components, new ComponentListEditor());
        }

        context.ShowChildrenUI();
    }

    private class TagListStringHandler : IObjectUIHandler
    {
        public void OnIMGUI(UIContext context)
        {
            var tags = context.Get<string>() ?? "";
            if (!context.StateBool) {
                ImGui.SameLine();
                if (string.IsNullOrEmpty(tags)) {
                    ImGui.TextColored(Colors.Faded, Lang.Fields.TagsNone);
                } else {
                    ImGui.TextColored(Colors.Faded, tags);
                }
                if (ImGui.IsItemHovered()) {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                        context.StateBool = true;
                        context.InputClassname = tags;
                        ImGui.SetKeyboardFocusHere();
                    }
                }
            }
            if (context.StateBool) {
                var str = context.InputClassname;
                if (ImGui.InputText(Lang.Fields.Tags, ref str, 200)) {
                    context.InputClassname = str;
                }
                if (ImGui.Button(Lang.Buttons.Confirm)) {
                    context.StateBool = false;
                    UndoRedo.RecordSet(context, context.InputClassname);
                }
                ImGui.SameLine();
                if (ImGui.Button(Lang.Buttons.Cancel)) {
                    context.StateBool = false;
                }
            }
        }
    }

    private class GameObjectFlagsHandler : IObjectUIHandler
    {
        public void OnIMGUI(UIContext context)
        {
            var go = context.Get<GameObject>();
            var update = go.Update;
            var draw = go.Draw;
            var timescale = go.TimeScale;
            var w = ImGui.CalcItemWidth();
            var start = ImGui.GetCursorPosX();
            if (ImGui.Checkbox(Lang.Fields.Update, ref update)) {
                UndoRedo.RecordCallbackSetter(context, go, !update, update, static (g, v) => g.Update = v);
            }
            ImGui.SameLine();
            if (ImGui.Checkbox(Lang.Fields.Draw, ref draw)) {
                UndoRedo.RecordCallbackSetter(context, go, !draw, draw, static (g, v) => g.Draw = v);
            }
            if (go.Instance != null && RszFieldCache.GameObject.TimeScale.Exists(go.Instance.RszClass)) {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(w - (ImGui.GetCursorPosX() - start));
                var timescaleNew = timescale;
                if (ImGui.DragFloat(Lang.Fields.TimeScale, ref timescaleNew)) {
                    UndoRedo.RecordCallbackSetter(context, go, timescale, timescaleNew, static (g, v) => g.TimeScale = v, $"{go.GetHashCode()}ts");
                }
            }
        }
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

    internal static bool DrawComponentTreeNode(UIContext context, RszInstance instance, bool allowRevert = true)
    {
        ImGui.BeginGroup();

        ReadOnlySpan<byte> label = context.label;
        ReadOnlySpan<byte> suffix = ReferenceEquals(context.annotation, null) ? ReadOnlySpan<byte>.Empty : context.annotation.GetUTF8(instance);
        ImGui.PushID(label);

        // begin node header
        var show = ImGui.TreeNodeEx("##treenode", ImGuiTreeNodeFlags.FramePadding |
                                                  ImGuiTreeNodeFlags.Framed |
                                                  ImGuiTreeNodeFlags.AllowOverlap);
        // end node header

        object? componentFirstField = instance.Values.Length > 0 ? instance.Values[0] : null;
        if (componentFirstField is bool enabledBool) {
            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPos().Y + 1.75f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 1));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Colors.IconOverlayBackground);
            ImGui.PushStyleColor(ImGuiCol.Border, ImguiHelpers.GetColor(ImGuiCol.ButtonHovered));
            if (ImGui.Checkbox("##chkbox", ref enabledBool)) {
                UndoRedo.RecordCallbackSetter(context, instance, instance.Values[0], enabledBool, (inst, val) => inst.Values[0] = val);
            }
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(2);
        }
        ImGui.SameLine();

        // begin name labels
        // using class name here because i couldn't find an efficient way to strip the suffix from the label
        // it's also accurate to their editor ig
        ImGui.TextUnformatted(instance.RszClass.ShortName);
        if (!suffix.IsEmpty) {
            ImGui.SameLine();
            ImGui.TextColored(Colors.Faded, suffix);
        }
        // end name labels

        if (allowRevert) {
            ImGui.SameLine();

            float buttonWidth = ImGui.CalcTextSize($"{AppIcons.SI_Reset}").X + ImGui.GetStyle().FramePadding.X * 2;
            float rightEdge = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X + ImGui.GetStyle().ItemInnerSpacing.X;

            float posX = rightEdge - buttonWidth;

            ImGui.SetCursorScreenPos(new Vector2(posX, ImGui.GetCursorScreenPos().Y));

            using (var _ = ImguiHelpers.Disabled(!context.IsChanged)) {
                ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
                if (ImGui.Button($"{AppIcons.SI_Reset}")) {
                    try {
                        context.Revert();
                        context.ResetState();
                        // some components throw this while being reverted. data seems to be fine if we catch this though
                    } catch (NotImplementedException) {
                    }
                }
                ImGui.PopStyleColor();
            }
        }

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
