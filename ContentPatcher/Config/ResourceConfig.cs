using ReeLib;

namespace ContentPatcher;

public class ResourceConfig(string type)
{
    public string Type { get; } = type;
    public long[]? CustomIDRange { get; init; }
    public RszClass? RszClass { get; set; }
    public IDGenerator? IDGenerator { get; set; }
    public IDGenerator? SubIDGenerator { get; set; }
    public ResourceHandler Patcher { get; set; } = null!;
    public ResourceConfig? ParentResource { get; set; }
    public List<ResourceConfig> SubResources { get; set; } = [];

    public Dictionary<string, ResourceConfig>? Subtypes { get; set; }

    public IDGenerator IDGeneratorRequired => IDGenerator ?? throw new NotImplementedException($"Missing ID for multi file resource {Type}");
    public RszClass RszClassRequired => RszClass ?? throw new NotImplementedException($"Missing required classname for resource {Type}");

    public override string ToString() => Type;
}
