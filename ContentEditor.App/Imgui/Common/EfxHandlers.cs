
using System.Collections;
using System.Numerics;
using System.Reflection;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Efx;
using ReeLib.Il2cpp;
using ReeLib.via;

namespace ContentEditor.App.ImguiHandling;

[ObjectImguiHandler(typeof(EFXExpressionParameter))]
public class EFXExpressionParameterHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<EFXExpressionParameter>();
        // ImguiHelpers.TextSuffix(context.label, instance.ToString());
        var label = instance.name ?? instance.expressionParameterNameUTF8Hash.ToString();

        var type = instance.type;
        switch (type) {
            case EfxExpressionParameterType.Float: {
                    var value = instance.value1;
                    if (ImGui.DragFloat(label, ref value)) {
                        UndoRedo.RecordCallbackSetter(context, instance, instance.value1, value, (o, v) => o.value1 = v, $"{instance.GetHashCode()}");
                    }
                    break;
                }
            case EfxExpressionParameterType.Float2: {
                    var value2 = instance.Float2;
                    if (ImGui.DragFloat2(label, ref value2)) {
                        UndoRedo.RecordCallbackSetter(context, instance, instance.Float2, value2, (o, v) => {
                            o.value1 = v.X;
                            o.value2 = v.Y;
                        }, $"{instance.GetHashCode()}");
                    }
                    break;
                }
            case EfxExpressionParameterType.Range: {
                    var value3 = new Vector3(instance.value1, instance.value2, instance.value3);
                    if (ImGui.DragFloat3(label, ref value3)) {
                        UndoRedo.RecordCallbackSetter(context, instance, instance.Range, value3, (o, v) => o.Range = v, $"{instance.GetHashCode()}");
                    }
                    break;
                }
            default:
            case EfxExpressionParameterType.Color: {
                    var col = instance.Color.ToVector4();
                    if (ImGui.ColorEdit4(label, ref col)) {
                        UndoRedo.RecordCallbackSetter(context, instance, instance.Color, Color.FromVector4(col), (o, v) => o.Color = v, $"{instance.GetHashCode()}");
                    }
                    break;
                }
        }
    }
}
