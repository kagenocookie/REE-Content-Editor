using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourcePatcher("group", nameof(Deserialize))]
public class GroupResourceHandler : ResourceHandler
{
    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => Config.Subtypes!.First().Value.Patcher!.CreateValueHandler(field);

    public static GroupResourceHandler Deserialize(ResourceConfig resource, EntityResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new GroupResourceHandler() {
            Config = resource,
        };
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
    {
        if (Config.Subtypes == null) throw new Exception($"Missing subtypes for group resource {Config}");

        foreach (var (subtype, sub) in Config.Subtypes) {
            if (initialData?.AsObject().TryGetPropertyValue("$type", out var typeStr) == true && typeStr?.GetValueKind() == System.Text.Json.JsonValueKind.String) {
                var type = typeStr.GetValue<string>();
                if ((type == subtype || type == sub.RszClass?.name) && sub.Patcher != null) {
                    return sub.Patcher.CreateResource(workspace, id, initialData);
                }
            }
        }
        throw new Exception($"Creating blank resources of type {Config} is not supported");
    }

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        if (Config.Subtypes == null) throw new Exception($"Missing subtypes for group resource {Config}");

        foreach (var (type, sub) in Config.Subtypes) {
            sub.Patcher?.ReadResources(workspace, dict);
        }
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        if (Config.Subtypes == null) throw new Exception($"Missing subtypes for group resource {Config}");

        foreach (var (type, sub) in Config.Subtypes) {
            sub.Patcher?.ModifyResources(workspace, resources);
        }
    }
}
