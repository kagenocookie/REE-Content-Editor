using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public class RSZObjectResource(ResourceConfig type, RszInstance instance, string file) : IContentResource, IValueProvider
{
    public ResourceConfig ResourceType => type;
    public string FileResourcePath => file;
    public RszInstance Instance { get; set; } = instance;

    public object MainValue => Instance;

    public IContentResource Clone() => new RSZObjectResource(ResourceType, Instance.Clone(), file);

    public JsonNode ToJson(Workspace env) => Instance.ToJson(env);
}
