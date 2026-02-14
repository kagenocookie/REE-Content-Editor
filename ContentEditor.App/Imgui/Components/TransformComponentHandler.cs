using System.Diagnostics;
using System.Numerics;
using ContentEditor.Core;
using ContentPatcher;

namespace ContentEditor.App.ImguiHandling;

[ObjectImguiHandler(typeof(Transform), Stateless = true, Priority = 0)]
public class TransformComponentHandler : IObjectUIHandler, IUIContextEventHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<Transform>();
        var data = instance.Data;
        var show = ImGui.TreeNode("Transform"u8);
        if (ImGui.IsItemHovered()) {
            if (context.children.Count == 0) {
                context.AddChild(context.label, context.Get<Component>().Data, ChildrenOnlyHandler.Instance);
            }
        }
        if (ImGui.BeginPopupContextItem(context.label)) {
            if (RszInstanceHandler.ShowContextMenuItemActions(context, static (ctx) => ctx.Get<Transform>().Data, static (ctx, newData) => {
                Debug.Assert(newData == ctx.Get<Transform>().Data);
            })) {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (show) {
            var useEuler = AppConfig.Instance.ShowQuaternionsAsEuler.Get();
            var vec3Width = useEuler ? 1 : 0.75f;
            var localpos = instance.LocalPosition;
            var localrot = instance.LocalRotation;
            var localscale = instance.LocalScale;
            var w = ImGui.CalcItemWidth();
            ImGui.SetNextItemWidth(w * vec3Width);
            if (ImGui.DragFloat3(useEuler ? "Local Position" : "##Local Position", ref localpos, 0.005f)) {
                UndoRedo.RecordCallbackSetter(context, instance, instance.LocalPosition, localpos, static (i, v) => i.LocalPosition = v, $"{instance.GetHashCode()} LocalPos");
            }
            if (ImGui.BeginPopupContextItem("##Local Position"u8)) {
                AppImguiHelpers.ShowJsonCopyManualSet<Vector3>(localpos, context, (c, v) => c.Get<Transform>().LocalPosition = v, $"{instance.GetHashCode()}_t");
                ImGui.EndPopup();
            }
            ImGui.SameLine();
            if (vec3Width < 1) ImGui.SetNextItemWidth(w * 0.25f - ImGui.GetStyle().FramePadding.X * 2);
            ImGui.LabelText("Local Position"u8, "##labelP"u8);

            if (QuaternionFieldHandler.HandleQuaternion("Local Rotation", ref localrot, useEuler)) {
                UndoRedo.RecordCallbackSetter(context, instance, (Quaternion)data.Values[1], localrot, static (inst, value) => inst.LocalRotation = value, $"{instance.GetHashCode()} LocalRot");
            }
            if (ImGui.BeginPopupContextItem("Local Rotation")) {
                if (QuaternionFieldHandler.DefaultContextItems("Local Rotation", ref localrot)) {
                    UndoRedo.RecordCallbackSetter(context, instance, (Quaternion)data.Values[1], localrot, static (inst, value) => inst.LocalRotation = value, $"{instance.GetHashCode()} LocalRot");
                }
                ImGui.EndPopup();
            }

            ImGui.SetNextItemWidth(w * vec3Width);
            if (ImGui.DragFloat3(useEuler ? "Local Scale" : "##Local Scale", ref localscale, 0.005f)) {
                UndoRedo.RecordCallbackSetter(context, instance, instance.LocalScale, localscale, static (i, v) => i.LocalScale = v, $"{instance.GetHashCode()} LocalScale");
            }
            if (ImGui.BeginPopupContextItem("##Local Scale"u8)) {
                AppImguiHelpers.ShowJsonCopyManualSet<Vector3>(localscale, context, (c, v) => c.Get<Transform>().LocalScale = v, $"{instance.GetHashCode()}_s");
                ImGui.EndPopup();
            }
            ImGui.SameLine();
            if (vec3Width < 1) ImGui.SetNextItemWidth(w * 0.25f - ImGui.GetStyle().FramePadding.X * 2);
            ImGui.LabelText("Local Scale"u8, "##labelS"u8);

            if (context.children.Count == 0) {
                context.AddChild(context.label, context.Get<Component>().Data, ChildrenOnlyHandler.Instance);
            }

            var child = context.children[0];
            if (child.children.Count == 0) {
                WindowHandlerFactory.AddRszInstanceFieldChildren(child, 3);
            }

            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }

    public bool HandleEvent(UIContext context, EditorUIEvent eventData)
    {
        if (eventData.type is UIContextEvent.Updated or UIContextEvent.Changed or UIContextEvent.Reverted) {
            var inst = context.Get<Transform>();
            inst.InvalidateTransform();
        }
        return true;
    }
}
