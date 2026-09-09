using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public class RSZObjectListResource : IContentResource
{
    private string resourceType;
    private string file;
    public string ResourceTypeID => resourceType;
    public string FilePath => file;

    public RSZObjectListResource(string resourceType, string file)
    {
        this.resourceType = resourceType;
        this.file = file;
        Instances = [];
    }

    public RSZObjectListResource(RszInstance instance, string file)
    {
        Instances = [instance];
        resourceType = instance.RszClass.name;
        this.file = file;
    }

    private RSZObjectListResource(List<RszInstance> instances, string? resourceType, string file)
    {
        Instances = instances;
        this.resourceType = resourceType ?? instances.FirstOrDefault()?.RszClass.name ?? throw new Exception();
        this.file = file;
    }

    public List<RszInstance> Instances { get; }

    public IContentResource Clone() => new RSZObjectListResource(Instances.Select(i => i.Clone()).ToList(), resourceType, file);

    public JsonNode ToJson(Workspace env) => new JsonArray(Instances.Select(i => i.ToJson(env)).ToArray());
}
