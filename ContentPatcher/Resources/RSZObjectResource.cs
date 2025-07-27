using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public class RSZObjectResource(RszInstance instance, string file) : IContentResource
{
    public string ResourceIdentifier => Instance.RszClass.name;
    public string FilePath => file;
    public RszInstance Instance { get; set; } = instance;

    public IContentResource Clone() => new RSZObjectResource(Instance.Clone(), file);

    public JsonNode ToJson(Workspace env) => Instance.ToJson(env);
}
