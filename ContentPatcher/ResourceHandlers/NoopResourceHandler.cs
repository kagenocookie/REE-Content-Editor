using System.Diagnostics.CodeAnalysis;

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
}
