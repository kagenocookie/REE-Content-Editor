using System.Collections;
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