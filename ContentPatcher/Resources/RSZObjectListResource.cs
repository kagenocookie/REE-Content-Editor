using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public class RSZObjectListResource : IContentResource, IResourceValueContainer
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


    public NestableFieldAccessor? GetAccessor(ContentWorkspace workspace, string path)
    {
        if (int.TryParse(path, out var index)) {
            return new NestableFieldAccessor.Custom<RSZObjectListResource>(RszFieldType.Object, l => l.Instances[index]!, (l, v) => {
                l.Instances[index] = (RszInstance)v!;
            });
        }
        var clsAcc = NestableFieldAccessor.CreateForClass(workspace.Env.RszParser, ResourceType.RszClass, path);
        return new NestableFieldAccessor.Custom<RSZObjectListResource>(clsAcc.Field, l => clsAcc.Get(l.Instances[0])!, (l, v) => {
            foreach (var inst in l.Instances) {
                clsAcc.Set(inst, v!);
            }
        });
    }
}
