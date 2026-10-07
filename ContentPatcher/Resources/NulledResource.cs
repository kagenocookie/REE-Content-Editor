using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

/// <summary>
/// Marks any resource as explicitly removed.
/// </summary>
public class NulledResource(ResourceConfig config, string file) : IContentResource
{
    public ResourceConfig ResourceType { get; } = config;
    public string FileResourcePath => file;
    public IContentResource Clone() => new NulledResource(ResourceType, file);
    public JsonNode ToJson(Workspace env) => JsonValue.Create("null");
}
