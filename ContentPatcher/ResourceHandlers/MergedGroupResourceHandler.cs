using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourcePatcher("merged-group")]
public class MergedGroupResourceHandler : ResourceHandler, IResourceHandlerStatic
{
    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => Config.Subtypes!.First().Value.Resource!.CreateValueHandler(field);

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new MergedGroupResourceHandler() {
            Config = resource,
        };
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        if (Config.Subtypes == null) throw new Exception($"Missing subtypes for group resource {Config}");

        if (initialData?.AsObject().TryGetPropertyValue("$type", out var typeStr) == true && typeStr?.GetValueKind() == System.Text.Json.JsonValueKind.String) {
            foreach (var (subtype, sub) in Config.Subtypes) {
                var type = typeStr.GetValue<string>();
                if ((type == subtype || type == sub.RszClass?.name) && sub.Resource != null) {
                    return sub.Resource.CreateResource(workspace, id, initialData);
                }
            }
        }
        throw new Exception($"Creating blank resources of type {Config} is not supported");
    }

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        if (Config.Subtypes == null) throw new Exception($"Missing subtypes for group resource {Config}");

        foreach (var (type, sub) in Config.Subtypes) {
            sub.Resource?.ReadResources(workspace, dict);
        }
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        if (Config.Subtypes == null) throw new Exception($"Missing subtypes for group resource {Config}");

        foreach (var (type, sub) in Config.Subtypes) {
            sub.Resource?.ModifyResources(workspace, resources);
        }
    }
}
