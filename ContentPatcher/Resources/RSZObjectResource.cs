using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public class RSZObjectResource(RszInstance instance, string file, string? resourceType = null) : IContentResource
{
    public string ResourceTypeID => resourceType ?? Instance.RszClass.name;
    public string FilePath => file;
    public RszInstance Instance { get; set; } = instance;

    public IContentResource Clone() => new RSZObjectResource(Instance.Clone(), file, resourceType);

    public JsonNode ToJson(Workspace env) => Instance.ToJson(env);
}
