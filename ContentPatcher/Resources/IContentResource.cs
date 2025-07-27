using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public interface IContentResource
{
    string ResourceIdentifier { get; }
    string FilePath { get; }
    IContentResource Clone();
    JsonNode ToJson(Workspace env);
}
