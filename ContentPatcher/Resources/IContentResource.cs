using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public interface IContentResource
{
    /// <summary>
    /// The unique type identifier of this resource. For most resources this would be a constant type value or empty.
    /// </summary>
    string ResourceIdentifier { get; }
    /// <summary>
    /// Path to the file containing this resource. Can be null in case it's a resource without a file (e.g. arbitrary entity strings).
    /// </summary>
    string? FilePath { get; }
    IContentResource Clone();
    JsonNode ToJson(Workspace env);
}
