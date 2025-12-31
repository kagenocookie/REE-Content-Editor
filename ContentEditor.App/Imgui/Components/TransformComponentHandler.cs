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
        var show = ImGui.TreeNode("via.Transform");
        if (ImGui.IsItemHovered()) {
            if (context.children.Count == 0) {
                context.AddChild(context.label, context.Get<Component>().Data, ChildrenOnlyHandler.Instance);
            }
        }
        // RszInstanceHandler.ShowDefaultContextMenu(context);
        if (show) {
            var localpos = instance.LocalPosition;
            var localrot = instance.LocalRotation.ToVector4();
            var localscale = instance.LocalScale;
            var w = ImGui.CalcItemWidth();
            ImGui.SetNextItemWidth(w * 0.75f);
            if (ImGui.DragFloat3("##Local Position", ref localpos, 0.005f)) {
                UndoRedo.RecordCallbackSetter(context, instance, instance.LocalPosition, localpos, (i, v) => i.LocalPosition = v, $"{instance.GetHashCode()} LocalPos");
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(w * 0.25f - ImGui.GetStyle().FramePadding.X * 2);
            ImGui.LabelText("Local Position", "##labelP");
            if (ImGui.DragFloat4("Local Rotation", ref localrot, 0.002f)) {
                var newQ = Quaternion.Normalize(localrot.ToQuaternion());
                UndoRedo.RecordCallbackSetter(context, instance, (Quaternion)data.Values[1], newQ, static (inst, value) => {
                    inst.Data.Values[1] = value;
                    inst.InvalidateTransform();
                }, $"{instance.GetHashCode()} LocalRot");
            }

            ImGui.SetNextItemWidth(w * 0.75f);
            if (ImGui.DragFloat3("##Local Scale", ref localscale, 0.005f)) {
                UndoRedo.RecordCallbackSetter(context, instance, instance.LocalScale, localscale, (i, v) => i.LocalScale = v, $"{instance.GetHashCode()} LocalScale");
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(w * 0.25f - ImGui.GetStyle().FramePadding.X * 2);
            ImGui.LabelText("Local Scale", "##labelS");

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
