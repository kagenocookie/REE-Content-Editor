using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.RegularExpressions;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using VYaml.Annotations;

namespace ContentPatcher;

public class EntityConfig(string name)
{
    public string Name { get; internal set; } = name;
    public EntityField PrimaryField { get; set; } = null!;
    public EntityField IDField { get; set; } = null!;
    public EntityField[] Fields { get; set; } = [];
    public EntityField[] DisplayFieldsOrder { get; set; } = [];
    public EntityEnumInfo? PrimaryEnum { get; init; }
    public EntityEnumInfo[]? Enums { get; init; }
    public ZeroEntity? ZeroEntity { get; set; }
    public StringFormatter? StringFormatter { get; set; }

    public bool HasField(string name) => GetField(name) != null;
    public EntityField? GetField(string name) => Fields.FirstOrDefault(f => f.name == name);

    public override string ToString() => Name;
}

[YamlObject(NamingConvention.SnakeCase)]
public partial class EntityConfigSerialized
{
    public List<EntityFieldConfig> Fields = null!;
    public string? To_String { get; set; }

    public string? DisplayName { get; set; }
    public EntityEnumInfo[]? Enums { get; set; }

    [YamlMember("id_field")]
    public string? IDField { get; set; }

    public string? PrimaryField { get; set; }

    public ZeroEntity? ZeroEntity { get; set; }

    public RuntimeMappingConfig? RuntimeMapping { get; set; }
}

[YamlObject(NamingConvention.SnakeCase)]
public partial class RuntimeMappingConfig
{
    public string runtimeType = "";
    public Dictionary<string, string> ToRuntime { get; set; } = new();
    public Dictionary<string, string> ToDesktop { get; set; } = new();
    public Dictionary<string, string> ToBoth { get; set; } = new();
}

[YamlObject(NamingConvention.SnakeCase)]
public partial class EntityFieldConfig
{
    public string name = string.Empty;
    public string? label;

    [YamlMember("when")]
    public EntityFieldConditionData? condition;
    [YamlMember("when_any")]
    public EntityFieldConditionData[]? multiConditionsAny;
    [YamlMember("required")]
    public bool isRequired;
    public string? displayAfter;
    [YamlMember("not_standalone")]
    public bool isNotStandalone;

    public EntityProperty? fieldId;

    public string? fieldType;

    public ResourceConfigSerialized? resource;

    [return: NotNullIfNotNull(nameof(defaultValue))]
    public T GetParam<T>(string key, T defaultValue = default!) => resource!.GetParam<T>(key, defaultValue);

    public bool TryGetParam<T>(string key, [MaybeNullWhen(false)] out T value) => resource!.TryGetParam<T>(key, out value) == true;

    public T RequireParam<T>(string key) => resource!.RequireParam<T>(key);
}

[YamlObject]
public partial class EntityFieldConditionData : ResourceConditionData
{
    public string? field;
}

[YamlObject]
public partial class ZeroEntity
{
    public long id;
    public string? label;
}

[YamlObject]
public partial class EntityEnumInfo
{
    public string name = string.Empty;
    public string? format;
    public bool primary;

    [GeneratedRegex("[^0-9a-zA-Z_]")]
    private static partial Regex NonAlphanumericRegex();

    [YamlIgnore]
    private StringFormatter? formatter;

    internal void Init(ContentWorkspace workspace, EntityConfig config)
    {
        formatter = format == null ? null : new StringFormatter(format, FormatterSettings.CreateFullEntityFormatter(config, workspace));
    }

    public void UpdateEnum<T>(ContentWorkspace workspace, T value, string label) where T : IBinaryInteger<T>
    {
        var desc = workspace.Env.TypeCache.GetEnumDescriptor(name);
        desc.AddValue(value, label);
    }

    public void UpdateEnum(ContentWorkspace workspace, ResourceEntity entity)
    {
        // NOTE: we don't currently have a way of resetting custom enum entries. Probably not worth the effort to fix
        // May cause issues if the user swaps bundles or if we ever support changing IDs in runtime.

        var desc = workspace.Env.TypeCache.GetEnumDescriptor(name);
        desc.AddValue(entity.Id, formatter?.GetString(entity) ?? NonAlphanumericRegex().Replace(entity.Label, ""), entity.Label);
    }
}