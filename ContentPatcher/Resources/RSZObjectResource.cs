using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public class RSZObjectResource(ResourceConfig type, RszInstance instance, string file) : IContentResource, IPropertyContainer
{
    public ResourceConfig ResourceType => type;
    public string FileResourcePath => file;
    public RszInstance Instance { get; set; } = instance;

    public IContentResource Clone() => new RSZObjectResource(ResourceType, Instance.Clone(), file);

    public object? Get(string path)
    {
        return Instance.GetNestedFieldValue(path);
    }

    public void Set(string path, object? value)
    {
        Instance.SetNestedFieldValue(path, value ?? RszInstance.NULL);
    }

    public JsonNode ToJson(Workspace env) => Instance.ToJson(env);
}
