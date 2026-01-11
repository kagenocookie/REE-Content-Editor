namespace ContentPatcher;

using ContentEditor.Editor;
using ReeLib;
using VYaml.Annotations;

public class ClassConfig
{
    public int[]? IDFields { get; set; }
    public int[]? SubIDFields { get; set; }
    public string? Group { get; set; }
    public string? Type { get; set; }
    public Dictionary<string, FieldConfig>? Fields { get; set; }
    public Dictionary<string, SubclassConfig>? Subclasses { get; set; }
    public ResourceHandler? Patcher { get; set; }
    public StringFormatter? StringFormatter { get; set; }

    public long GetID(RszInstance instance)
    {
        return 0;
    }

    public void MergeIntoSubclass(string subclass, ClassConfig subConfig)
    {
        subConfig.IDFields = subConfig.IDFields ?? IDFields;
        subConfig.SubIDFields = subConfig.SubIDFields ?? SubIDFields;
        subConfig.Group = subConfig.Group ?? Group;
        subConfig.Type = subConfig.Type ?? Type;
        subConfig.Fields = subConfig.Fields ?? Fields;
        subConfig.Patcher = subConfig.Patcher ?? Subclasses?.GetValueOrDefault(subclass)?.Patcher;
    }

    public override string ToString() => $"{Group}/{Type}";
}

[YamlObject]
public partial class SerializedPatchConfigRoot
{
    public Dictionary<string, EntityConfigSerialized>? Entities { get; set; }
    public Dictionary<string, CustomTypeConfigSerialized>? Types { get; set; }
    public Dictionary<string, ClassConfigSerialized>? Classes { get; set; }
}

[YamlObject]
public partial class ClassConfigSerialized
{
    [YamlMember("id")]
    public string[]? ID { get; set; }
    [YamlMember("subId")]
    public string[]? SubID { get; set; }
    public string? Group { get; set; }
    public string? Type { get; set; }
    public Dictionary<string, FieldConfig>? Fields { get; set; }
    public Dictionary<string, SubclassConfigSerialized>? Subclasses { get; set; }
    public Dictionary<string, object>? Patcher { get; set; }
    [YamlMember("to_string")]
    public string? To_String { get; set; }

    public void MergeIntoRuntimeConfig(RszClass? cls, ClassConfig target)
    {
        if (cls == null && (ID?.Length > 0 || SubID?.Length > 0)) {
            throw new ArgumentNullException(nameof(cls), "RSZ class is required when ID or SubID are present");
        }
        target.Type = Type ?? target.Type;
        target.Group = Group ?? target.Group;
        target.Patcher = ResourceHandler.CreateInstance(cls!.name, Patcher) ?? target.Patcher;
        target.IDFields = ID?.Select(fieldname => cls.IndexOfField(fieldname)).ToArray() ?? target.IDFields;
        target.SubIDFields = SubID?.Select(fieldname => cls.IndexOfField(fieldname)).ToArray() ?? target.SubIDFields;

        if (Fields != null) {
            target.Fields ??= new();
            foreach (var field in Fields) {
                if (target.Fields.ContainsKey(field.Key)) continue;

                target.Fields.Add(field.Key, field.Value);
            }
        }

        if (Subclasses != null) {
            target.Subclasses ??= new();
            foreach (var (subclass, subdata) in Subclasses) {
                if (target.Subclasses.ContainsKey(subclass)) continue;

                target.Subclasses.Add(subclass, subdata.ToRuntimeConfig(subclass));
            }
        }
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

    public SubclassConfig ToRuntimeConfig(string resourceKey)
    {
        return new SubclassConfig() {
            Patcher = ResourceHandler.CreateInstance(resourceKey, Patcher),
            Fields = Fields,
        };
    }
}
