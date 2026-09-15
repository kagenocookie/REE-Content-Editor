using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourcePatcher("merged-group")]
public class MergedGroupResourceHandler : ResourceHandler, IResourceHandlerStatic
{
    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => Config.Subtypes!.First().Value.Resource!.CreateValueHandler(field);

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        if (resource.Subtypes == null) throw new Exception($"Missing subtypes for group resource {resource}");

        return new MergedGroupResourceHandler() {
            Config = resource,
        };
    }

    public override IContentResource ApplyResourceData(ContentWorkspace workspace, IContentResource? resource, JsonNode? data)
    {
        if (resource?.ResourceType != null) {
            // note: the assumption here is that we never swap a resource from one subtype to another
            return resource.ResourceType.Resource.ApplyResourceData(workspace, resource, data);
        }

        // otherwise try and match a classname or subtype name from the data JSON
        if (data?.AsObject().TryGetPropertyValue("$type", out var typeStr) == true && typeStr?.GetValueKind() == System.Text.Json.JsonValueKind.String) {
            foreach (var (subtype, sub) in Config.Subtypes!) {
                var type = typeStr.GetValue<string>();
                if ((type == subtype || type == sub.RszClass?.name) && sub.Resource != null) {
                    return sub.Resource.ApplyResourceData(workspace, resource, data);
                }
            }
        }

        throw new NotImplementedException($"Can't create blank new resources of type {Config.Resource} ({Config.Type})");
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        if (initialData?.AsObject().TryGetPropertyValue("$type", out var typeStr) == true && typeStr?.GetValueKind() == System.Text.Json.JsonValueKind.String) {
            foreach (var (subtype, sub) in Config.Subtypes!) {
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
        foreach (var (type, sub) in Config.Subtypes!) {
            sub.Resource?.ReadResources(workspace, dict);
        }
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        foreach (var (type, sub) in Config.Subtypes!) {
            sub.Resource?.ModifyResources(workspace, resources);
        }
    }
}
