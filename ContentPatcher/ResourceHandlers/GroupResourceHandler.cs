using System.Diagnostics;
using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

[ResourcePatcher("group")]
public class GroupResourceHandler : ResourceHandler, IResourceHandlerStatic
{
    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => new GroupedField();

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        if (resource.Subtypes == null) throw new Exception($"Missing subtypes for group resource {resource}");

        return new GroupResourceHandler() {
            Config = resource,
        };
    }

    public override IContentResource ApplyResourceData(ContentWorkspace workspace, IContentResource? resource, JsonNode? data, ResourceEntity? entity)
    {
        if (resource is not GroupedResource group || group.ResourceType != Config) {
            group = new GroupedResource(Config, Config.Subtypes!.Keys);
        }

        foreach (var (type, subconfig) in Config.Subtypes!) {
            var subdata = data?[type];
            var subvalue = group.Get(type);
            subvalue = subconfig.resource.Resource.ApplyResourceData(workspace, subvalue, subdata, entity);
            group.Set(type, subvalue);
        }

        return group;
    }

    public override IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData, ResourceEntity? entity)
    {
        var group = new GroupedResource(Config, Config.Subtypes!.Keys);

        foreach (var (type, subconfig) in Config.Subtypes) {
            var subdata = initialData?[type];
            var subvalue = subconfig.resource.Resource.CreateResource(workspace, id, subdata, entity);
            group.Set(type, subvalue);
        }

        return group;
    }

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
        var subdict = new Dictionary<long, IContentResource>();
        var keys = Config.Subtypes!.Keys;
        foreach (var (type, sub) in Config.Subtypes) {
            sub.resource.Resource?.ReadResources(workspace, subdict);
            foreach (var (id, res) in subdict) {
                if (!dict.TryGetValue(id, out var existing) || existing is not GroupedResource group) {
                    dict[id] = group = new GroupedResource(Config, keys);
                }
                group.Set(type, res);
            }
            subdict.Clear();
        }
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
        foreach (var (type, sub) in Config.Subtypes!) {
            sub.resource.Resource?.ModifyResources(workspace, resources);
        }
    }
}

public class GroupedResource(ResourceConfig config, IEnumerable<string>? initialKeys = null) : IContentResource, IPropertyContainer
{
    public ResourceConfig ResourceType { get; } = config;

    public string? FileResourcePath => null;

    private Dictionary<string, IContentResource?> subresources = initialKeys?.Any() == true
        ? new (initialKeys.Select(k => new KeyValuePair<string, IContentResource?>(k, null)))
        : new();

    public IReadOnlyDictionary<string, IContentResource?> Resources => subresources;

    public void Set(string key, IContentResource? resource)
    {
        Debug.Assert(subresources.ContainsKey(key));
        subresources[key] = resource;
    }

    public IContentResource? Get(string key)
    {
        return subresources[key];
    }

    public TRes? Get<TRes>(string key) where TRes : IContentResource
    {
        return (TRes?)subresources[key];
    }

    object? IPropertyContainer.Get(string path)
    {
        foreach (var (k, sub) in subresources) {
            if (sub is IPropertyContainer pc) {
                return pc.Get(path);
            }
        }
        return null;
    }

    void IPropertyContainer.Set(string path, object? value)
    {
        foreach (var (k, sub) in subresources) {
            if (sub is IPropertyContainer pc) {
                pc.Set(path, value);
            }
        }
    }

    public IContentResource Clone()
    {
        var dict = subresources.ToDictionary(kv => kv.Key, kv => kv.Value?.Clone());
        return new GroupedResource(ResourceType) {
            subresources = dict
        };
    }

    public JsonNode ToJson(Workspace env)
    {
        var obj = new JsonObject();
        foreach (var (type, sub) in subresources) {
            obj[type] = sub?.ToJson(env);
        }

        return obj;
    }
}