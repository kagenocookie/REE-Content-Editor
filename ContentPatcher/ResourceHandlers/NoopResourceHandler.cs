namespace ContentPatcher;

public class NoopResourceHandler<T> : ResourceHandler where T : EntityFieldValueHandler, new()
{
    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => new T();

    public static NoopResourceHandler<T> Create(ResourceConfig resource)
    {
        return new NoopResourceHandler<T>() { Config = resource };
    }

    public override void ReadResources(ContentWorkspace workspace, Dictionary<long, IContentResource> dict)
    {
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
    }
}
