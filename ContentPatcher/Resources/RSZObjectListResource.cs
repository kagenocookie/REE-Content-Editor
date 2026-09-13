using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public class RSZObjectListResource : IContentResource, IPropertyContainer
{
    private string file;
    public ResourceConfig ResourceType { get; }
    public string FileResourcePath => file;

    public RSZObjectListResource(ResourceConfig resourceType, string file)
    {
        ResourceType = resourceType;
        this.file = file;
        Instances = [];
    }

    public RSZObjectListResource(ResourceConfig resourceType, List<RszInstance> instances, string file)
    {
        Instances = instances;
        ResourceType = resourceType;
        this.file = file;
    }

    public List<RszInstance> Instances { get; }

    public IContentResource Clone() => new RSZObjectListResource(ResourceType, Instances.Select(i => i.Clone()).ToList(), file);

    public JsonNode ToJson(Workspace env) => new JsonArray(Instances.Select(i => i.ToJson(env)).ToArray());

    public object? Get(string path)
    {
        return Instances.FirstOrDefault()?.GetNestedFieldValue(path);
    }

    public void Set(string path, object? value)
    {
        foreach (var inst in Instances) {
            inst.SetNestedFieldValue(path, value ?? RszInstance.NULL);
        }
    }
}
