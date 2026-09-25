using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public interface IContentResource
{
    /// <summary>
    /// The unique type identifier of this resource. For most resources this would be a constant type value or empty.
    /// </summary>
    ResourceConfig ResourceType { get; }
    /// <summary>
    /// Path to the file containing this resource. Can be null in case it's a resource without a file (e.g. arbitrary entity strings).
    /// </summary>
    string? FileResourcePath { get; }
    IContentResource Clone();
    JsonNode ToJson(Workspace env);
}

public interface IAddressableContentResource : IContentResource
{
    public long ID { get; }
}

public interface IPropertyContainer
{
    public object? Get(string path);
    public void Set(string path, object? value);
}
