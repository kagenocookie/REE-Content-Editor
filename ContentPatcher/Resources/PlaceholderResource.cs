using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

/// <summary>
/// Generic empty resource to be used with display-only entity fields.
/// </summary>
public sealed class PlaceholderResource<TField> : IContentResource where TField : CustomEntityFieldHandler
{
    public ResourceConfig ResourceType { get; }
    public string? FileResourcePath { get; set; } = null;

    public PlaceholderResource(ResourceConfig config)
    {
        ResourceType = config;
    }

    public PlaceholderResource(ResourceConfig config, string? fileResourcePath)
    {
        ResourceType = config;
        FileResourcePath = fileResourcePath;
    }

    public IContentResource Clone() => new PlaceholderResource<TField>(ResourceType, FileResourcePath);

    public JsonNode ToJson(Workspace env) => new JsonObject();

    public override string ToString() => FileResourcePath ?? typeof(TField).Name;
}
