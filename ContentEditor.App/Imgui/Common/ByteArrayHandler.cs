using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Il2cpp;
using ReeLib.via;

namespace ContentEditor.App.ImguiHandling;

public class ByteArrayHandler : IObjectUIHandler
{
    private const int cols = 16;

    public unsafe void OnIMGUI(UIContext context)
    {
        var data = context.Get<byte[]>();
        var len = data.Length;
        if (len == 0) {
            ImGui.Text(context.label + ": <empty>");
            return;
        }
        var rows = (int)Math.Ceiling((float)len / cols);
        var show = true;
        if (rows > 1) {
            show = ImguiHelpers.TreeNodeSuffix(context.label, "Byte array");
        } else {
            ImGui.Text(context.label);
            ImGui.SameLine();
        }

        if (show) {
            var maxCharsOffset = BitOperations.Log2((uint)len) / 8 + 3;
            var maxOffset = ImGui.CalcTextSize(new string('0', maxCharsOffset)).X;
            float cellWidth = Math.Max(UI.FontSize + ImGui.GetStyle().FramePadding.X * 2, (ImGui.CalcItemWidth() - maxOffset) / cols);
            for (int i = 0; i < len; ++i) {
                if (i % cols != 0) {
                    ImGui.SameLine();
                } else if (rows > 1) {
                    var x = ImGui.GetCursorPosX();
                    // ImGui.Text("0x" + i.ToString("X"));
                    ImGui.Text(i.ToString());
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(x + maxOffset);
                }

                var val = (int)data[i];
                ImGui.SetNextItemWidth(cellWidth);
                if (ImGui.DragInt($"##{i}", ref val, 0.09f, 0, 255)) {
                    UndoRedo.RecordCallbackSetter(context, (data, i), data[i], (byte)val, (arrIndex, v) => arrIndex.data[arrIndex.i] = v, $"{data.GetHashCode()}{i}");
                }
            }

            if (rows > 1) ImGui.TreePop();
        }
    }
}
