using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourcePatcher("merged-group")]
public class MergedGroupResourceHandler : ResourceHandler, IResourceHandlerStatic
{
    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => Config.Subtypes!.First().Value.resource.Resource!.CreateValueHandler(field);

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        if (resource.Subtypes == null) throw new Exception($"Missing subtypes for group resource {resource}");

        return new MergedGroupResourceHandler() {
            Config = resource,
        };
    }

    public override IContentResource ApplyResourceData(ContentWorkspace workspace, IContentResource? resource, JsonNode? data, ResourceEntity? entity)
    {
        if (resource?.ResourceType != null) {
            // note: the assumption here is that we never swap a resource from one subtype to another
            return resource.ResourceType.Resource.ApplyResourceData(workspace, resource, data, entity);
        }

        var classname = data?.DetermineObjectClassname();
        if (classname != null) {
            foreach (var (subtype, sub) in Config.Subtypes!) {
                if ((classname == subtype || classname == sub.resource.RszClass?.name) && sub.resource.Resource != null) {
                    return sub.resource.Resource.ApplyResourceData(workspace, resource, data, entity);
                }
            }
        }

        if (entity != null) {
            foreach (var (subtype, sub) in Config.Subtypes!) {
                if (sub.condition != null && sub.condition.IsEnabled(entity)) {
                    return sub.resource.Resource.ApplyResourceData(workspace, resource, data, entity);
                }
            }
        }

        throw new NotImplementedException($"Can't create blank new resources of type {Config.Resource} ({Config.Type})");
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        var classname = initialData?.DetermineObjectClassname();
        if (classname != null) {
            foreach (var (subtype, sub) in Config.Subtypes!) {
                if ((classname == subtype || classname == sub.resource.RszClass?.name) && sub.resource.Resource != null) {
                    return sub.resource.Resource.CreateResource(workspace, id, initialData);
                }
            }
        }
        throw new Exception($"Creating blank resources of type {Config} is not supported");
    }

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        foreach (var (type, sub) in Config.Subtypes!) {
            sub.resource.Resource?.ReadResources(workspace, dict);
        }
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        foreach (var (type, sub) in Config.Subtypes!) {
            sub.resource.Resource?.ModifyResources(workspace, resources);
        }
    }
}
