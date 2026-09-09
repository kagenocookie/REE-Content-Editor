namespace ContentPatcher;

using ContentEditor.Core;
using ContentEditor.Editor;
using ReeLib;
using VYaml.Annotations;

public class ClassConfig
{
    public Dictionary<string, FieldConfig>? Fields { get; set; }
    public Dictionary<string, SubclassConfig>? Subclasses { get; set; }
    public StringFormatter? StringFormatter { get; set; }

    public void MergeIntoSubclass(string subclass, ClassConfig subConfig)
    {
        subConfig.Fields = subConfig.Fields ?? Fields;
    }
}

[YamlObject]
public partial class SerializedPatchConfigRoot
{
    public Dictionary<string, EntityConfigSerialized>? Entities { get; set; }
    // public Dictionary<string, CustomTypeConfigSerialized>? Types { get; set; }
    public Dictionary<string, RszClassConfigSerialized>? Classes { get; set; }
    public Dictionary<string, EntityResourceConfigSerialized>? Resources { get; set; }
}

[YamlObject]
public partial class RszClassConfigSerialized
{
    [YamlMember("to_string")]
    public string? To_String { get; set; }
}

[YamlObject]
public partial class ClassConfigSerialized
{
    public string? Group { get; set; }
    public string? Type { get; set; }
    public Dictionary<string, FieldConfig>? Fields { get; set; }
    public Dictionary<string, SubclassConfigSerialized>? Subclasses { get; set; }
    [YamlMember("to_string")]
    public string? To_String { get; set; }

    public void MergeIntoRuntimeConfig(ContentWorkspace workspace, RszClass? cls, ClassConfig target)
    {
        if (Fields != null) {
            target.Fields ??= new();
            foreach (var field in Fields) {
                if (target.Fields.ContainsKey(field.Key)) continue;

                target.Fields.Add(field.Key, field.Value);
            }
        }

        // if (Subclasses != null) {
        //     target.Subclasses ??= new();
        //     foreach (var (subclass, subdata) in Subclasses) {
        //         if (target.Subclasses.ContainsKey(subclass)) continue;

        //         target.Subclasses.Add(subclass, subdata.ToRuntimeConfig(subclass));
        //     }
        // }
    }
}

[YamlObject]
public partial class FieldConfig
{
    public string? Enum { get; set; }
    public string? Type;
    public string? ResourceType;
    public string? Tooltip;
    public string? Label;
    public string? TranslateGuid;
    public string? TranslateFallbackEnum;
    public string? Handler;
    public bool ReadOnly;
    public Dictionary<string, string?>? OneOf { get; set; }
}

[YamlObject]
public partial class SubclassConfig
{
    public Dictionary<string, FieldConfig>? Fields { get; set; }
    public ResourceHandler? Patcher { get; set; }
}

[YamlObject]
public partial class SubclassConfigSerialized
{
    public Dictionary<string, FieldConfig>? Fields { get; set; }
    public Dictionary<string, object>? Patcher { get; set; }

    // public SubclassConfig ToRuntimeConfig(string resourceKey)
    // {
    //     return new SubclassConfig() {
    //         Patcher = ResourceHandler.CreateInstance(resourceKey, Patcher),
    //         Fields = Fields,
    //     };
    // }
}
