using System.Reflection;
using VYaml.Annotations;

namespace ContentPatcher;

public class CustomTypeConfig
{
    public required CustomField[] Fields { get; init; }
    public required CustomField[] DisplayFieldsOrder { get; init; }
}

[YamlObject]
public partial class CustomTypeConfigSerialized
{
    public Dictionary<string, CustomFieldSerialized> Fields = null!;
    public Dictionary<string, object>? To_String { get; set; }

    private static Dictionary<string, Type>? customFieldTypes;

    public CustomTypeConfig ToRuntimeConfig()
    {
        var fieldlist = new List<CustomField>();
        var displaylist = new List<CustomField>();
        foreach (var (name, data) in Fields) {
            var newfield = CustomTypeConfigSerialized.CreateField(name, data);
            fieldlist.Add(newfield);
            displaylist.Add(newfield);
        }

        foreach (var (name, data) in Fields) {
            var curIndex = fieldlist.FindIndex(f => f.name == name);
            var field = fieldlist[curIndex];
            if (data.displayAfter != null) {
                var otherIndex = displaylist.FindIndex(dl => dl.name == data.displayAfter);
                if (otherIndex != -1) {
                    if (otherIndex == Fields.Count) {
                        displaylist.Add(field);
                    } else {
                        displaylist.Insert(otherIndex + 1, field);
                    }
                    displaylist.RemoveAt(curIndex);
                }
            }
        }

        var config = new CustomTypeConfig() { Fields = fieldlist.ToArray(), DisplayFieldsOrder = displaylist.ToArray() };
        // config.toStringGenerator = TODO

        return config;
    }

    internal static CustomField CreateField(string name, CustomFieldSerialized data)
    {
        var field = ResolveFieldInstance(name, data);
        field.LoadParams(name, data.param);
        if (data.whenClassname != null) {
            field.Condition = new WhenClassnameCondition(data.whenClassname.field, data.whenClassname.classname);
        }
        field.IsRequired = data.isRequired;
        field.IsNotStandaloneValue = data.IsNotStandalone;
        return field;
    }

    private static CustomField ResolveFieldInstance(string name, CustomFieldSerialized data)
    {
        if (customFieldTypes == null) {
            customFieldTypes = new();
            foreach (var type in typeof(ObjectCustomField).Assembly.GetTypes()) {
                if (type.IsAbstract || !type.IsAssignableTo(typeof(CustomField))) continue;

                var attr = type.GetCustomAttribute<ResourceFieldAttribute>();
                if (attr == null) continue;

                customFieldTypes[attr.PatcherType] = type;
            }
        }
        if (customFieldTypes.TryGetValue(data.type, out var t)) {
            var inst = (CustomField)Activator.CreateInstance(t)!;
            inst.name = name;
            inst.label = data.label ?? name;
            return inst;
        }
        throw new NotImplementedException($"Unknown field type {data.type} for field {name}");
    }
}
