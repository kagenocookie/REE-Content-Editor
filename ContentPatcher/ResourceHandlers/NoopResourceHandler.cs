using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public class NoopResourceHandler<TFieldType> : ResourceHandler, IResourceHandlerStatic
    where TFieldType : EntityFieldValueHandler, new()
{
    public NoopResourceHandler()
    {
    }

    [SetsRequiredMembers]
    public NoopResourceHandler(ResourceConfig config)
    {
        Config = config;
    }

    public class VoidResource(ResourceConfig config) : IContentResource
    {
        public ResourceConfig ResourceType { get; } = config;
        public string? FileResourcePath => null;
        public IContentResource Clone() => new VoidResource(ResourceType);
        public JsonNode ToJson(Workspace env) => new JsonObject();
    }

    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => new TFieldType();

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new NoopResourceHandler<TFieldType>() { Config = resource };
    }

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
    }

    public override IContentResource ApplyResourceData(ContentWorkspace workspace, IContentResource? resource, JsonNode? data)
    {
        return resource ?? new VoidResource(Config);
    }
}
