using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;

namespace ContentPatcher;

public abstract class ResourceHandler
{
    public string ResourceTypeID { get; set; } = string.Empty;
    public List<string> Files { get; init; } = new();

    private static readonly Dictionary<string, Func<string, Dictionary<string, object>, ResourceHandler>> patchers = new();
    static ResourceHandler()
    {
        var pTypes = typeof(ResourceHandler).Assembly.GetTypes().Where(t => t.IsAssignableTo(typeof(ResourceHandler)) && !t.IsAbstract && t.GetCustomAttribute<ResourcePatcherAttribute>() != null);
        foreach (var p in pTypes) {
            var attr = p.GetCustomAttribute<ResourcePatcherAttribute>()!;
            var deserializer = attr.DeserializeMethod;
            var method = p.GetMethod(deserializer, BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!;
            patchers.Add(attr.PatcherType, (resourceKey, data) => (ResourceHandler)method.Invoke(null, [resourceKey, data])!);
        }
    }

    [return: NotNullIfNotNull(nameof(config))]
    public static ResourceHandler? CreateInstance(string resourceTypeId, Dictionary<string, object>? config)
    {
        if (config == null) return null;
        var type = config["type"] as string;
        if (type == null) throw new ArgumentException("Patcher must have a type field", nameof(config));

        if (patchers.TryGetValue(type, out var func)) {
            return func.Invoke(resourceTypeId, config);
        }

        throw new ArgumentException($"Unknown patcher type {type}");
    }

    public static void RegisterResourcePatcher(string type, Func<string, Dictionary<string, object>, ResourceHandler> factory)
    {
        patchers[type] = factory;
    }

    public abstract void ReadResources(ContentWorkspace env, ClassConfig config, Dictionary<long, IContentResource> dict);

    public abstract void ModifyResources(ContentWorkspace workspace, ClassConfig config, IEnumerable<KeyValuePair<long, IContentResource>> resources);

    public virtual (long id, IContentResource resource) CreateResource(ContentWorkspace workspace, ClassConfig config, ResourceEntity entity, JsonNode? initialData)
        => throw new NotImplementedException($"Can't create new resources of type {ResourceTypeID}");
}
