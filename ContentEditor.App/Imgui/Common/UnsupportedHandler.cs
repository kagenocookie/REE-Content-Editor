using System.Reflection;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public class UnsupportedHandler : IObjectUIHandler
{
    public UnsupportedHandler()
    {
        FieldType = "unknown";
    }

    public UnsupportedHandler(RszField field)
    {
        FieldType = field.type.ToString();
    }

    public UnsupportedHandler(Type? type)
    {
        FieldType = type?.Name ?? "unknown";
    }

    public UnsupportedHandler(FieldInfo field) : this(field.FieldType) { }

    public UnsupportedHandler(MemberInfo field)
    {
        FieldType = ((field as FieldInfo)?.FieldType)?.Name ?? (field as PropertyInfo)?.PropertyType?.Name ?? "unknown";
    }

    public string FieldType { get; }

    public void OnIMGUI(UIContext context)
    {
        ImGui.TextColored(Colors.Error, $"{context.label} (unsupported {FieldType} value {context.GetRaw() ?? "NULL"})");
    }
}
