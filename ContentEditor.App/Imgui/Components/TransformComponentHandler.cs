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
        var show = ImGui.TreeNode("via.Transform"u8);
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
            var localpos = instance.LocalPosition;
            var localrot = instance.LocalRotation.ToVector4();
            var localscale = instance.LocalScale;
            var w = ImGui.CalcItemWidth();
            ImGui.SetNextItemWidth(w * 0.75f);
            if (ImGui.DragFloat3("##Local Position", ref localpos, 0.005f)) {
                UndoRedo.RecordCallbackSetter(context, instance, instance.LocalPosition, localpos, static (i, v) => i.LocalPosition = v, $"{instance.GetHashCode()} LocalPos");
            }
            if (ImGui.BeginPopupContextItem("##Local Position")) {
                AppImguiHelpers.ShowJsonCopyManualSet<Vector3>(localpos, context, (c, v) => c.Get<Transform>().LocalPosition = v, $"{instance.GetHashCode()}_t");
                ImGui.EndPopup();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(w * 0.25f - ImGui.GetStyle().FramePadding.X * 2);
            ImGui.LabelText("Local Position", "##labelP"u8);
            if (ImGui.DragFloat4("Local Rotation", ref localrot, 0.002f)) {
                if (localrot == Vector4.Zero) localrot = new Vector4(0, 0, 0, 1);
                var newQ = Quaternion.Normalize(localrot.ToQuaternion());
                UndoRedo.RecordCallbackSetter(context, instance, (Quaternion)data.Values[1], newQ, static (inst, value) => inst.LocalRotation = value, $"{instance.GetHashCode()} LocalRot");
            }
            if (ImGui.BeginPopupContextItem("##Local Rotation")) {
                AppImguiHelpers.ShowJsonCopyManualSet<Quaternion>(localrot.ToQuaternion(), context,
                    static (c, v) => c.Get<Transform>().LocalRotation = v.LengthSquared() == 0 ? Quaternion.Identity : Quaternion.Normalize(v),
                    $"{instance.GetHashCode()}_r");
                ImGui.EndPopup();
            }

            ImGui.SetNextItemWidth(w * 0.75f);
            if (ImGui.DragFloat3("##Local Scale", ref localscale, 0.005f)) {
                UndoRedo.RecordCallbackSetter(context, instance, instance.LocalScale, localscale, static (i, v) => i.LocalScale = v, $"{instance.GetHashCode()} LocalScale");
            }
            if (ImGui.BeginPopupContextItem("##Local Scale")) {
                AppImguiHelpers.ShowJsonCopyManualSet<Vector3>(localscale, context, (c, v) => c.Get<Transform>().LocalScale = v, $"{instance.GetHashCode()}_s");
                ImGui.EndPopup();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(w * 0.25f - ImGui.GetStyle().FramePadding.X * 2);
            ImGui.LabelText("Local Scale", "##labelS"u8);

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
