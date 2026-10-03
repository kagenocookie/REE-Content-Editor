using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContentEditor;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using VYaml.Annotations;

namespace ContentPatcher;

public class EntityConfig(string name)
{
    public string Name { get; internal set; } = name;
    public string ShortName { get; internal set; } = name.GetStringAfterLastDelimiter('.').ToString();
    public EntityField PrimaryField { get; set; } = null!;
    public EntityField IDField { get; set; } = null!;
    public EntityField[] Fields { get; set; } = [];
    public EntityField[] DisplayFieldsOrder { get; set; } = [];
    public EntityEnumInfo? PrimaryEnum { get; init; }
    public EntityEnumInfo[]? Enums { get; init; }
    public List<IObjectUIHandler>? BeforeCreate { get; set; }
    public ZeroEntity? ZeroEntity => SourceConfig.ZeroEntity;
    public StringFormatter? StringFormatter { get; set; }

    public required EntityConfigSerialized SourceConfig { get; init; }

    public bool AllowCreateEmpty { get; set; }
    public bool AllowTemplates { get; set; }
    public bool RequireRuntimeBundle { get; set; }

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
    public List<Dictionary<string, object>>? BeforeCreate { get; set; }

    public bool? AllowCreateEmpty { get; set; }
    public bool? AllowTemplates { get; set; }
    public bool RequireRuntimeBundle { get; set; }

    public RuntimeMappingConfig? RuntimeMapping { get; set; }
}

[YamlObject(NamingConvention.SnakeCase)]
public partial class RuntimeMappingConfig
{
    public string runtimeType = "";
    public bool requireRuntimeData = false;
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
    public string? displayType;

    public EntityProperty? fieldId;

    public string? fieldType;

    public ResourceConfigSerialized? resource;

    [return: NotNullIfNotNull(nameof(defaultValue))]
    public T GetParam<T>(string key, T defaultValue = default!) => resource!.GetParam<T>(key, defaultValue);

    public bool TryGetParam<T>(string key, [MaybeNullWhen(false)] out T value) => resource!.TryGetParam<T>(key, out value) == true;

    public T RequireParam<T>(string key) => resource!.RequireParam<T>(key);
}

[YamlObject(NamingConvention.SnakeCase)]
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
    [YamlMember("value")]
    public string? valueFormat;
    public bool primary;

    [GeneratedRegex("[^0-9a-zA-Z_]")]
    private static partial Regex NonAlphanumericRegex();

    [YamlIgnore]
    private StringFormatter? labelFormatter;
    [YamlIgnore]
    private StringFormatter? valueFormatter;

    internal void Init(ContentWorkspace workspace, EntityConfig config)
    {
        labelFormatter = format == null ? null : new StringFormatter(format, FormatterSettings.CreateFullEntityFormatter(config, workspace));
        valueFormatter = valueFormat == null ? null : new StringFormatter(valueFormat, FormatterSettings.CreateFullEntityFormatter(config, workspace));
    }

    public string GetFormattedLabel(ResourceEntity entity) => labelFormatter?.GetString(entity) ?? NonAlphanumericRegex().Replace(entity.Label, "");
    public object GetFormattedValue(ContentWorkspace workspace, ResourceEntity entity)
        => Convert.ChangeType(valueFormatter?.GetString(entity) ?? entity.Id.ToString(), workspace.Env.TypeCache.GetEnumDescriptor(name).BackingType);

    public void UpdateEnum(ContentWorkspace workspace, ResourceEntity entity)
    {
        // NOTE: we don't currently have a way of resetting custom enum entries. Probably not worth the effort to fix
        // May cause issues if the user swaps bundles or if we ever support changing IDs without a full reload.
        var desc = workspace.Env.TypeCache.GetEnumDescriptor(name);
        var value = Convert.ChangeType(valueFormatter?.GetString(entity) ?? entity.Id.ToString(), desc.BackingType);
        var curLabel = desc.GetLabel(value);
        if (string.IsNullOrEmpty(curLabel)) {
            var valueJson = JsonSerializer.SerializeToElement(value);
            curLabel = GetFormattedLabel(entity);
            if (string.IsNullOrEmpty(curLabel)) {
                return;
            }
            desc.AddValue(curLabel, valueJson);
        }
        desc.SetDisplayLabel(curLabel, entity.Label);
    }
}