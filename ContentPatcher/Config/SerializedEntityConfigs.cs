using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.RegularExpressions;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib;
using VYaml.Annotations;

namespace ContentPatcher;

[YamlObject]
public partial class EntityConfigSerialized
{
    public List<EntityFieldConfig> Fields = null!;
    [YamlMember("to_string")]
    public string? To_String { get; set; }
    public EntityEnumInfo[]? Enums { get; set; }

    [YamlMember("id_field")]
    public string? IDField { get; set; }

    [YamlMember("primary_field")]
    public string? PrimaryField { get; set; }
}

[YamlObject(NamingConvention.SnakeCase)]
public partial class EntityFieldConfig
{
    public string name = string.Empty;
    public string type = string.Empty;
    public string? label;
    public EntityFieldConditionData? condition;
    [YamlMember("required")]
    public bool isRequired;
    public string? displayAfter;
    [YamlMember("not_standalone")]
    public bool IsNotStandalone;

    public Dictionary<string, object>? fieldId;

    public string? fieldType;

    public EntityResourceConfigSerialized? resource;

    [YamlIgnore]
    public EntityResourceConfigSerialized RequireResourceSettings => resource ?? throw new Exception($"Resource is required for entity field type {type}");

    [return: NotNullIfNotNull(nameof(defaultValue))]
    public T GetParam<T>(string key, T defaultValue = default!)
    {
        return TryGetParam<T>(key, out var vv) ? vv : defaultValue;
    }

    public bool TryGetParam<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        if (resource?.Params?.TryGetValue(key, out var val) == true) {
            if (val is T vv) {
                value = vv;
                return true;
            }

            throw new Exception($"Resource type {type} parameter {key} must be {typeof(T)}");
        }
        value = default;
        return false;
    }

    public T RequireParam<T>(string key)
        => resource?.Params?.GetValueOrDefault(key) is T vvv ? vvv : throw new Exception($"Resource type {type} requires {typeof(T)} parameter {key}");
}

[YamlObject(NamingConvention.SnakeCase)]
public partial class EntityResourceConfigSerialized
{
    [YamlMember("id")]
    public object[]? ID { get; set; }

    [YamlMember("sub_id")]
    public string[]? SubID { get; set; }

    public string? DisplayName { get; set; }

    [YamlMember("custom_id_range")]
    public long[]? CustomIDRange { get; set; }

    public string? ParentResource { get; set; }

    public string Type { get; set; } = "";
    public string? File { get; set; }
    public string[]? Files { get; set; }
    public string? Classname { get; set; }
    public string? Key { get; set; }

    public List<LinkedResourceData>? Resources { get; set; }
    public Dictionary<string, EntityResourceConfigSerialized>? Subclasses { get; set; }

    [YamlMember("not_standalone")]
    public bool DisallowStandaloneEditing { get; set; }

    public KnownFileFormats ResourceType { get; set; }

    public string? Field { get; set; }
    public Dictionary<string, object>? Params { get; set; }

    [YamlIgnore]
    public string SingleFile => File != null ? File : Files?.Length == 1 ? Files[0] : throw new Exception($"Resource type {Type} requires exactly one file");

    [YamlIgnore]
    public string[] TargetFiles => Files?.Length > 0 ? Files : [File ?? throw new Exception($"Missing file(s) for resource type {Type}")];

    public RszFieldAccessorBase<T> GetDirectFieldAccessor<T>(Func<RszField, bool> func)
    {
        if (!string.IsNullOrEmpty(Field)) {
            if (Field.Contains('.')) {
                throw new Exception($"Nested field access not supported in field {Type}");
            }
            return new RszFieldAccessorName<T>(Field);
        }
        return new RszFieldAccessorFirst<T>(func);
    }
}

[YamlObject]
public partial class LinkedResourceData
{
    public string key = string.Empty;
    public string type = string.Empty;
    public string? name;
    public string? field;
    public ResourceConditionData? when;
}

[YamlObject]
public partial class ResourceConditionData
{
    public string property = string.Empty;
    public object? equals;
}

[YamlObject]
public partial class EntityFieldConditionData : ResourceConditionData
{
    public string? field;
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