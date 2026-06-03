using System.Numerics;
using System.Runtime.CompilerServices;
using ContentEditor.Core;

namespace ContentEditor.App.ImguiHandling;

public class BoxedEnumData
{
    public required string[] Labels { get; init; }
    public required object[] Values { get; init; }

    private static readonly Dictionary<Type, BoxedEnumData> Datas = new();

    public static BoxedEnumData GetOrCreate(Type enumType, Dictionary<object, string>? filteredValues = null)
    {
        if (filteredValues != null) {
            var data = new BoxedEnumData() {
                Values = filteredValues.OrderBy(kv => kv.Key).Select(kv => kv.Key).ToArray(),
                Labels = filteredValues.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray(),
            };
            if (AppConfig.Instance.PrettyFieldLabels) data.TranslateLabels();
            return data;
        } else {
            if (!Datas.TryGetValue(enumType, out var data)) {
                var labels = enumType.GetEnumNames();
                Datas[enumType] = data = new BoxedEnumData() {
                    Labels = labels,
                    Values = new object[labels.Length]
                };
                if (AppConfig.Instance.PrettyFieldLabels) data.TranslateLabels();
                var it = enumType.GetEnumValues().GetEnumerator();
                for (int i = 0; i < labels.Length; ++i) {
                    it.MoveNext();
                    data.Values[i] = it.Current;
                }
            }
            return data;
        }
    }

    public void TranslateLabels()
    {
        for (int i = 0; i < Labels.Length; i++) {
            Labels[i] = WindowHandlerFactory.GetFieldLabel(Labels[i]);
        }
    }
}

public sealed class CsharpEnumHandler : IObjectUIHandler
{
    private BoxedEnumData data;

    public bool NoUndoRedo { get; set; }

    public CsharpEnumHandler(Type enumType, Dictionary<object, string>? filteredValues = null)
    {
        data = BoxedEnumData.GetOrCreate(enumType, filteredValues);
    }

    public void OnIMGUI(UIContext context)
    {
        var selected = context.GetRaw();
        if (ImguiHelpers.FilterableCombo(context.label, data.Labels, data.Values, ref selected, ref context.Filter)) {
            if (!NoUndoRedo) {
                UndoRedo.RecordSet(context, selected);
            } else {
                context.Set(selected);
                context.Changed = false;
            }
        }
    }
}

public class CsharpFlagsEnumFieldHandler<T, TUnderlying>() : IObjectUIHandler
    where T : unmanaged, Enum
    where TUnderlying : unmanaged, IBinaryInteger<TUnderlying>, IBitwiseOperators<TUnderlying, TUnderlying, TUnderlying>
{
    private ImGuiDataType scalarType = TypeToImguiDataType(typeof(T).GetEnumUnderlyingType());
    private string[] Names = AppConfig.Instance.PrettyFieldLabels ? Enum.GetNames<T>().Select(WindowHandlerFactory.GetFieldLabel).ToArray() : Enum.GetNames<T>();
    private T[] Values = Enum.GetValues<T>();

    public bool HideNumberInput { get; init; }

    private static ImGuiDataType TypeToImguiDataType(Type type)
    {
        if (type == typeof(short)) return ImGuiDataType.S16;
        if (type == typeof(ushort)) return ImGuiDataType.U16;
        if (type == typeof(int)) return ImGuiDataType.S32;
        if (type == typeof(uint)) return ImGuiDataType.U32;
        if (type == typeof(long)) return ImGuiDataType.S64;
        if (type == typeof(ulong)) return ImGuiDataType.U64;
        if (type == typeof(byte)) return ImGuiDataType.U8;
        if (type == typeof(sbyte)) return ImGuiDataType.S8;
        throw new NotImplementedException($"Unsupported flag enum backing type {type}");
    }

    public unsafe void OnIMGUI(UIContext context)
    {
        var value = context.Get<T>();
        ImguiHelpers.BeginRect();
        if (HideNumberInput) {
            ImGui.Text(context.label + ": ");
            AppImguiHelpers.ShowValueContextMenu<T>(value, context);
        } else if (ImGui.InputScalar(context.label, scalarType, &value)) {
            UndoRedo.RecordSet(context, value);
        }

        var startX = ImGui.GetCursorPosX();
        var endX = ImGui.GetWindowSize().X;
        var totalPadding = startX * 2;
        var w_total = endX - totalPadding;
        if (!HideNumberInput) {
            ImGui.Text("Flags: ");
        }
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

            var hasFlag = (Unsafe.As<T, TUnderlying>(ref value) & Unsafe.As<T, TUnderlying>(ref flagValue)).Equals(Unsafe.As<T, TUnderlying>(ref flagValue));
            if (ImGui.Checkbox(label, ref hasFlag)) {
                var result = hasFlag
                    ? Unsafe.As<T, TUnderlying>(ref value) | Unsafe.As<T, TUnderlying>(ref flagValue)
                    : Unsafe.As<T, TUnderlying>(ref value) & ~Unsafe.As<T, TUnderlying>(ref flagValue);
                UndoRedo.RecordSet(context, Unsafe.As<TUnderlying, T>(ref result), mergeMode: UndoRedoMergeMode.NeverMerge);
            }
        }
        ImGui.PopID();
        ImguiHelpers.EndRect(2);
    }
}


public sealed class CsharpEnumRadioHandler : IObjectUIHandler
{
    private BoxedEnumData data;

    public bool Inline { get; set; } = true;

    public CsharpEnumRadioHandler(Type enumType, Dictionary<object, string>? filteredValues = null)
    {
        data = BoxedEnumData.GetOrCreate(enumType, filteredValues);
    }

    public void OnIMGUI(UIContext context)
    {
        var selected = context.GetRaw();
        ImGui.PushID(context.label);
        ImGui.Text(context.label);
        for (int i = 0; i < data.Labels.Length; i++) {
            if (Inline) ImGui.SameLine();
            var opt = data.Values[i];
            var label = data.Labels[i];
            if (ImGui.RadioButton(label, opt.Equals(selected))) {
                UndoRedo.RecordSet(context, opt);
            }
        }
        ImGui.PopID();
    }
}
