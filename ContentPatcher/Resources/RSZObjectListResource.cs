using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public class RSZObjectListResource : IContentResource
{
    private string classname;
    private string file;
    public string ResourceIdentifier => classname;
    public string FilePath => file;

    public RSZObjectListResource(string classname, string file)
    {
        this.classname = classname;
        this.file = file;
        Instances = [];
    }

    public RSZObjectListResource(RszInstance instance, string file)
    {
        Instances = [instance];
        classname = instance.RszClass.name;
        this.file = file;
    }

    private RSZObjectListResource(List<RszInstance> instances, string? classname, string file)
    {
        Instances = instances;
        this.classname = classname ?? instances.FirstOrDefault()?.RszClass.name ?? throw new Exception();
        this.file = file;
    }

    public List<RszInstance> Instances { get; }

    public IContentResource Clone() => new RSZObjectListResource(Instances.Select(i => i.Clone()).ToList(), classname, file);

    public JsonNode ToJson(Workspace env) => new JsonArray(Instances.Select(i => i.ToJson(env)).ToArray());
}
