using System.Diagnostics.CodeAnalysis;
using ReeLib;
using VYaml.Annotations;

namespace ContentPatcher;

public class ResourceConfig(string type)
{
    public static readonly ResourceConfig Placeholder = new ("");
    public string Type { get; } = type;
    public string DisplayName { get; init; } = type;
    public ResourceConfigSerialized? OriginalConfig { get; set; }
    public long[]? CustomIDRange { get; set; }
    public RszClass? RszClass { get; set; }
    public IDGenerator? IDGenerator { get; set; }
    public IDGenerator? SubIDGenerator { get; set; }
    public ResourceHandler Resource { get; set; } = null!;
    public IResourceCondition? Filter { get; set; }

    public Dictionary<string, SubresourceConfig>? Subtypes { get; set; }

    public IDGenerator IDGeneratorRequired => IDGenerator ?? throw new NotImplementedException($"Missing ID setting for resource {Type}");
    public RszClass RszClassRequired => RszClass ?? throw new NotImplementedException($"Missing required classname for resource {Type}");

    private static readonly Dictionary<string, ResourceConfig> _namedPlaceholders = new();

    public static ResourceConfig NamedPlaceholder(string name)
        => _namedPlaceholders.TryGetValue(name, out var p) ? p : (_namedPlaceholders[name] = p = new ResourceConfig(name));

    public override string ToString() => Type;
}

public record SubresourceConfig(ResourceConfig resource, IEntityCondition? condition)
{
    public static implicit operator ResourceConfig(SubresourceConfig c) => c.resource;
}

[YamlObject(NamingConvention.SnakeCase)]
public partial class ResourceConfigSerialized
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

    public Dictionary<string, SubResourceConfigSerialized>? Subtypes { get; set; }

    public ResourceConditionData[]? filter;

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

    [return: NotNullIfNotNull(nameof(defaultValue))]
    public T GetParam<T>(string key, T defaultValue = default!)
    {
        return TryGetParam<T>(key, out var vv) ? vv : defaultValue;
    }

    public bool TryGetParam<T>(string key, [MaybeNullWhen(false)] out T value)
    {
        if (Params?.TryGetValue(key, out var val) == true) {
            if (val is T vv) {
                value = vv;
                return true;
            }

            throw new Exception($"Resource type {Type} parameter {key} must be {typeof(T)}");
        }
        value = default;
        return false;
    }

    public T RequireParam<T>(string key)
        => Params?.GetValueOrDefault(key) is T vvv ? vvv : throw new Exception($"Resource {Type} requires {typeof(T)} parameter {key}");
}

[YamlObject(NamingConvention.SnakeCase)]
public partial class SubResourceConfigSerialized : ResourceConfigSerialized
{
    public EntityFieldConditionData[]? when;
}

[YamlObject(NamingConvention.SnakeCase)]
public partial class ResourceConditionData
{
    public string property = string.Empty;
    public object? equals;
    public object? notEquals;
}
