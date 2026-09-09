using System.Reflection;
using VYaml.Annotations;

namespace ContentPatcher;

public class CustomTypeConfig
{
    public required EntityField[] Fields { get; init; }
    public required EntityField[] DisplayFieldsOrder { get; init; }
}

[YamlObject]
public partial class CustomTypeConfigSerialized
{
    // public List<EntityFieldConfig> Fields = null!;
    // public Dictionary<string, object>? To_String { get; set; }

    // private static Dictionary<string, Type>? customFieldTypes;

    // public CustomTypeConfig ToRuntimeConfig(ContentWorkspace workspace)
    // {
    //     var fieldlist = new List<EntityField>();
    //     var displaylist = new List<EntityField>();
    //     foreach (var field in Fields) {
    //         var newfield = CustomTypeConfigSerialized.CreateField(name, data, workspace);
    //         fieldlist.Add(newfield);
    //         displaylist.Add(newfield);
    //     }

    //     foreach (var field in Fields) {
    //         var curIndex = fieldlist.FindIndex(f => f.name == name);
    //         var field = fieldlist[curIndex];
    //         if (data.displayAfter != null) {
    //             var otherIndex = displaylist.FindIndex(dl => dl.name == data.displayAfter);
    //             if (otherIndex != -1) {
    //                 if (otherIndex == Fields.Count) {
    //                     displaylist.Add(field);
    //                 } else {
    //                     displaylist.Insert(otherIndex + 1, field);
    //                 }
    //                 displaylist.RemoveAt(curIndex);
    //             }
    //         }
    //     }

    //     var config = new CustomTypeConfig() { Fields = fieldlist.ToArray(), DisplayFieldsOrder = displaylist.ToArray() };
    //     // config.toStringGenerator = TODO

    //     return config;
    // }
}
