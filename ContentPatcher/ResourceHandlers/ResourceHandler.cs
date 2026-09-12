using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;

namespace ContentPatcher;

public abstract class ResourceHandler
{
    public required ResourceConfig Config { get; init; }
    public List<string> Files { get; init; } = new();

    public abstract EntityFieldValueHandler CreateValueHandler(EntityField field);

    private static readonly Dictionary<string, Func<ResourceConfig, ResourceConfigSerialized, ContentWorkspace, ResourceHandler>> patchers = new();
    private static readonly Dictionary<string, (string[]? gameWhitelist, Type? handlerType, Func<EntityFieldValueHandler> func)> fieldTypes = new();
    static ResourceHandler()
    {
        var pTypes = typeof(ResourceHandler).Assembly.GetTypes();

        foreach (var t in pTypes) {
            if (t.IsAssignableTo(typeof(ResourceHandler)) && !t.IsAbstract && t.GetCustomAttribute<ResourcePatcherAttribute>() != null) {
                var attr = t.GetCustomAttribute<ResourcePatcherAttribute>()!;
                var statics = t.GetInterfaceMap(typeof(IResourceHandlerStatic));
                var method = statics.TargetMethods[0];
                patchers.Add(attr.PatcherType, (resourceKey, data, ws) => (ResourceHandler)method.Invoke(null, [resourceKey, data, ws])!);
            }
            else if (t.IsAssignableTo(typeof(EntityFieldValueHandler)) && !t.IsAbstract && t.GetCustomAttribute<ResourceFieldAttribute>() != null) {
                var attr = t.GetCustomAttribute<ResourceFieldAttribute>()!;

                fieldTypes.Add(attr.FieldTypeName, (attr.SupportedGames, attr.HandlerType, () => (EntityFieldValueHandler)Activator.CreateInstance(t)!));
            }

        }
    }

    public static ResourceHandler CreateInstance(ResourceConfig resource, ResourceConfigSerialized config, ContentWorkspace workspace)
    {
        if (string.IsNullOrEmpty(config.Type)) throw new ArgumentException("Patcher must have a type field", nameof(config));

        if (patchers.TryGetValue(config.Type, out var func)) {
            return func.Invoke(resource, config, workspace);
        }

        if (fieldTypes.TryGetValue(config.Type, out var custom)) {
            if (custom.gameWhitelist?.Length > 0 && !custom.gameWhitelist.Contains(workspace.Game.name)) {
                throw new ArgumentException($"Field type {config.Type} not allowed for game {workspace.Game}");
            }

            if (custom.handlerType != null) {
                // var attr = custom.handlerType.GetCustomAttribute<ResourcePatcherAttribute>();
                // if (attr != null && patchers.TryGetValue(attr.PatcherType, out func)) {
                //     return func.Invoke(res, config, workspace);
                // }
                var attr = custom.handlerType.GetInterfaceMap(typeof(IResourceHandlerStatic));
                return (ResourceHandler)attr.TargetMethods[0].Invoke(null, [resource, config, workspace])!;
            }
        }

        throw new ArgumentException($"Unknown patcher type {config.Type}");
    }

    public static EntityFieldValueHandler CreateValueHandler(string type, ContentWorkspace workspace)
    {
        if (fieldTypes.TryGetValue(type, out var custom)) {
            if (custom.gameWhitelist?.Length > 0 && !custom.gameWhitelist.Contains(workspace.Game.name)) {
                throw new ArgumentException($"Field type {type} not allowed for game {workspace.Game}");
            }

            return custom.func.Invoke();
        }

        throw new ArgumentException($"Unknown field type {type}");
    }

    /// <summary>
    /// Read all available resource files.
    /// </summary>
    public abstract void ReadResources(ContentWorkspace env, Dictionary<long, IContentResource> dict);

    /// <summary>
    /// Apply all resource changes to files based on current resource data.
    /// </summary>
    public abstract void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources);

    public virtual IContentResource CreateResource(ContentWorkspace workspace, long id, JsonNode? initialData)
        => throw new NotImplementedException($"Can't create new resources of type {Config.Type}");
}

public interface IResourceHandlerStatic
{
    abstract static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace);
}
