using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public interface IContentResource
{
    /// <summary>
    /// The unique type identifier of this resource. For most resources this would be a constant type value or empty.
    /// </summary>
    string ResourceTypeID { get; }
    /// <summary>
    /// Path to the file containing this resource. Can be null in case it's a resource without a file (e.g. arbitrary entity strings).
    /// </summary>
    string? FileResourcePath { get; }
    string Label => ToString() ?? $"{ResourceTypeID} [{FileResourcePath ?? "/"}]";
    IContentResource Clone();
    JsonNode ToJson(Workspace env);
}

public interface IAddressableContentResource : IContentResource
{
    public long ID { get; }
}

public interface IResourceValueContainer
{
    public NestableFieldAccessor? GetAccessor(ContentWorkspace workspace, string path);
}
