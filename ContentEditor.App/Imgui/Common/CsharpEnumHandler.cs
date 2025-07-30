using System.Numerics;
using System.Runtime.CompilerServices;
using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App.ImguiHandling;

public class CsharpEnumHandler : IObjectUIHandler
{
    private Type enumType;
    private EnumData data;

    private class EnumData
    {
        public required string[] Labels { get; init; }
        public required object[] Values { get; init; }
    }
    private static readonly Dictionary<Type, EnumData> Datas = new();

    public CsharpEnumHandler(Type enumType)
    {
        this.enumType = enumType;
        if (!Datas.TryGetValue(enumType, out data!)) {
            var labels = enumType.GetEnumNames();
            Datas[enumType] = data = new EnumData() {
                Labels = labels,
                Values = new object[labels.Length]
            };
            var it = enumType.GetEnumValues().GetEnumerator();
            for (int i = 0; i < labels.Length; ++i) {
                it.MoveNext();
                data.Values[i] = it.Current;
            }
        }
    }

    public void OnIMGUI(UIContext context)
    {
        var selected = context.GetRaw();
        if (ImguiHelpers.FilterableCombo(context.label, data.Labels, data.Values, ref selected, ref context.state)) {
            UndoRedo.RecordSet(context, selected);
        }
    }
}

public class CsharpFlagsEnumFieldHandler<T, TUnderlying>() : IObjectUIHandler
    where T : unmanaged, Enum
    where TUnderlying : unmanaged, IBinaryInteger<TUnderlying>, IBitwiseOperators<TUnderlying, TUnderlying, TUnderlying>
{
    private ImGuiDataType scalarType = TypeToImguiDataType(typeof(T).GetEnumUnderlyingType());
    private string[] Names = Enum.GetNames<T>();
    private T[] Values = Enum.GetValues<T>();

    private static object[] GetBoxedValues(Type type)
    {
        var values = Enum.GetValues(type);
        var outv = new object[values.Length];
        int i = 0;
        foreach (var v in values) {
            outv[i++] = v;
        }
        return outv;
    }

    private static ImGuiDataType TypeToImguiDataType(Type type)
    {
        if (type == typeof(short)) return ImGuiDataType.S16;
        if (type == typeof(ushort)) return ImGuiDataType.U16;
        if (type == typeof(int)) return ImGuiDataType.S32;
        if (type == typeof(uint)) return ImGuiDataType.U32;
        if (type == typeof(long)) return ImGuiDataType.S64;
        if (type == typeof(ulong)) return ImGuiDataType.U64;
        throw new NotImplementedException($"Unsupported flag enum backing type {type}");
    }

    public unsafe void OnIMGUI(UIContext context)
    {
        var value = context.Get<T>();
        ImguiHelpers.BeginRect();
        if (ImGui.InputScalar(context.label, scalarType, (IntPtr)(&value))) {
            UndoRedo.RecordSet(context, value);
        }

        var startX = ImGui.GetCursorPosX();
        var endX = ImGui.GetWindowSize().X;
        var totalPadding = startX * 2;
        var w_total = endX - totalPadding;
        ImGui.Text("Flags: ");
        ImGui.SameLine();
        var tabMargin = ImGui.GetStyle().FramePadding.X * 2 + 32; // how do we determine checkbox size properly?

        ImGui.PushID(context.label);
        var x = ImGui.CalcTextSize("Flags: ").X + ImGui.GetStyle().FramePadding.X;
        for (int i = 0; i < Names.Length; ++i) {
            var label = Names[i];
            var flagValue = Values[i];
            var tabWidth = ImGui.CalcTextSize(label).X + tabMargin;
            if (i > 0) {
                if (x + tabWidth >= w_total) {
                    x = 0;
                } else {
                    ImGui.SameLine();
                }
            }
            x += tabWidth;

            var hasFlag = !(Unsafe.As<T, TUnderlying>(ref value) & Unsafe.As<T, TUnderlying>(ref flagValue)).Equals(default);
            if (ImGui.Checkbox(label, ref hasFlag)) {
                var result = hasFlag
                    ? Unsafe.As<T, TUnderlying>(ref value) | Unsafe.As<T, TUnderlying>(ref flagValue)
                    : Unsafe.As<T, TUnderlying>(ref value) & ~Unsafe.As<T, TUnderlying>(ref flagValue);
                UndoRedo.RecordSet(context, Unsafe.As<TUnderlying, T>(ref result));
            }
        }
        ImGui.PopID();
        ImguiHelpers.EndRect(2);
    }
}
