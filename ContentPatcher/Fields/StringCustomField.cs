using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib;

namespace ContentPatcher;

[ResourceField("string", typeof(NoopResourceHandler<StringCustomField>))]
public class StringCustomField : CustomEntityFieldHandler<StringResource>, IDiffableField
{
    public Regex? Regex { get; private set; }
    public string? RegexDescription { get; private set; }
    public string? Tooltip { get; private set; }
    private string? initialFormatString;
    private StringFormatter? initialFormat;
    private bool allowDiff;
    public override string? ResourceType => null;

    bool IDiffableField.EnableDiff => allowDiff;

    public override void LoadParams(EntityFieldConfig data)
    {
        if (data.TryGetParam<string>("regex", out var pattern)) {
            Regex = new Regex(pattern);
        }

        RegexDescription = data.GetParam<string>("regexDescription");
        Tooltip = data.GetParam<string>("tooltip");
        initialFormatString = data.GetParam<string>("initial");
        allowDiff = data.GetParam<bool>("diffable", true);
    }

    public override void EntitySetup(EntityConfig entityConfig, ContentWorkspace workspace)
    {
        if (initialFormatString != null) {
            initialFormat = new StringFormatter(initialFormatString, FormatterSettings.CreateFullEntityFormatter(entityConfig, workspace));
        }
    }

    public override StringResource? ApplyValue(ContentWorkspace workspace, StringResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            return null;
        }
        var newStr = data.GetValue<string>();
        if (currentResource?.Text != newStr) {
            entity.Set(Field.name, currentResource = new StringResource(Field.Config, data.GetValue<string>()));
        }
        return currentResource;
    }

    public override (long id, IContentResource resource) CreateValue(ContentWorkspace workspace, ResourceEntity entity, JsonNode? initialData)
    {
        if (Regex != null) {
            // assume it's expected to be unique - always start empty maybe?
            return (-1, new StringResource(Field.Config, string.Empty));
        } else {
            return (-1, new StringResource(Field.Config, initialData?.GetValue<string>() ?? string.Empty));
        }
    }

    public IEnumerable<KeyValuePair<long, IContentResource>> FetchInstances(ResourceManager workspace)
    {
        return Field.Config.Type == null ? [] : workspace.GetResourceInstances(Field.Config.Type);
    }

    public override StringResource? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        var res = entity.Get<StringResource>(Field.name);
        if (res == null) {
            res = new StringResource(Field.Config, initialFormat?.GetString(entity) ?? string.Empty);
        }
        return res;
    }
}

public sealed class StringResource : IContentResource
{
    public StringResource(ResourceConfig config)
    {
        ResourceType = config;
    }
    public StringResource(ResourceConfig config, string str)
    {
        ResourceType = config;
        Text = str;
    }

    public string Text { get; set; } = string.Empty;
    public ResourceConfig ResourceType { get; }
    public string? FileResourcePath => null;

    public IContentResource Clone() => new StringResource(ResourceType, Text);

    public JsonNode ToJson(Workspace env) => JsonValue.Create(Text);

    public override string ToString() => Text;
}
