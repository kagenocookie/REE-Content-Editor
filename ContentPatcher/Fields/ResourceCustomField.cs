using System.Text.Json.Nodes;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib;
using SmartFormat;

namespace ContentPatcher;

/// <summary>
/// <inheritdoc/><br/>
/// This field type only serves as a UI reference to a resource file based on another field's data.
/// </summary>
[ResourceField("resource")]
public class ResourceCustomField : CustomField
{
    public string resourceType = null!;
    public StringFormatter pathFormat = null!;
    public bool? ForcePreload;
    public override string? ResourceIdentifier => null;
    private string pathFormatString = string.Empty;

    public string GetPath(ResourceEntity entity) => pathFormat.GetString(entity);

    public override void LoadParams(string fieldName, Dictionary<string, object>? param)
    {
        ArgumentNullException.ThrowIfNull(param);
        resourceType = (string)param["type"];
        pathFormatString = (string)param["path"];
    }

    public override void EntitySetup(EntityConfig entityConfig, ContentWorkspace workspace)
    {
        pathFormat = new StringFormatter(pathFormatString, FormatterSettings.CreateFullEntityFormatter(entityConfig, workspace));
    }

    public override IContentResource? FetchResource(ResourceManager resources, ResourceEntity entity, ResourceState state)
    {
        // would we want to force-open the referenced file here?
        // var path = GetPath(entity);
        // if (resource?.Text == null) return null;
        // if (resources.TryGetOrLoadFile(resource.Text, out var file)) {
        // }
        // return new StringResource(path);
        return null;
    }

    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        return null;
    }
}

public sealed class FileContentResource : IContentResource
{
    public string ResourceIdentifier => FilePath;
    public string FilePath { get; set; } = string.Empty;

    public IContentResource Clone()
    {
        throw new NotImplementedException();
    }

    public JsonNode ToJson(Workspace env)
    {
        throw new NotImplementedException();
    }

    public override string ToString() => FilePath;
}
