using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

/// <summary>
/// Base fake resource handler to be used with display-only entity fields.
/// </summary>
public class FieldOnlyEntityField<T> : CustomEntityFieldHandler where T : FieldOnlyEntityField<T>
{
    public override string? ResourceType => null;

    public override ResourceHandler? CreateResourceHandler(ResourceConfig config) => new NoopResourceHandler<FieldOnlyEntityField<T>>(config);

    public override IContentResource? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        return new PlaceholderResource<T>(Field.Config);
    }

    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        return currentResource ?? new PlaceholderResource<T>(Field.Config);
    }
}
