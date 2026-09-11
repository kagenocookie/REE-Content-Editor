using System.Text.Json.Nodes;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib;

namespace ContentPatcher;

/// <summary>
/// <inheritdoc/><br/>
/// This field type only serves as a UI reference to a resource file based on another field's data.
/// </summary>
[ResourceField("resource_link")]
public class ResourceLinkCustomField : CustomEntityFieldHandler
{
    public KnownFileFormats resourceType;
    public StringFormatter pathFormat = null!;
    public bool? ForcePreload;
    public override string? ResourceType => null;
    private string pathFormatString = string.Empty;

    public override ResourceHandler? CreateResourceHandler(ResourceConfig config) => new NoopResourceHandler<ResourceLinkCustomField>(config);

    public string GetPath(ResourceEntity entity) => pathFormat.GetString(entity);

    public override void LoadParams(EntityFieldConfig data)
    {
        resourceType = data.resource?.ResourceType ?? KnownFileFormats.Unknown;
        pathFormatString = data.RequireParam<string>("path");
    }

    public override void EntitySetup(EntityConfig entityConfig, ContentWorkspace workspace)
    {
        pathFormat = new StringFormatter(pathFormatString, FormatterSettings.CreateFullEntityFormatter(entityConfig, workspace));
    }

    public override IContentResource? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        // would we want to force-open the referenced file here?
        var path = GetPath(entity);
        return new FileContentResource(path);
    }

    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        return null;
    }

    public override (long id, IContentResource resource) CreateValue(ContentWorkspace workspace, ResourceEntity entity, JsonNode? initialData)
    {
        return (-1, new FileContentResource());
    }
}

public sealed class FileContentResource : IContentResource
{
    public string ResourceTypeID => FileResourcePath;
    public string FileResourcePath { get; set; } = string.Empty;

    public FileContentResource()
    {
    }

    public FileContentResource(string fileResourcePath)
    {
        FileResourcePath = fileResourcePath;
    }

    public IContentResource Clone() => new FileContentResource(FileResourcePath);

    public JsonNode ToJson(Workspace env) => new JsonObject();

    public override string ToString() => FileResourcePath;
}
