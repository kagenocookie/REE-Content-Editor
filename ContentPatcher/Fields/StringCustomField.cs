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
            entity.Set(Field.name, currentResource = new StringResource(data.GetValue<string>()));
        }
        return currentResource;
    }

    public override (long id, IContentResource resource) CreateValue(ContentWorkspace workspace, ResourceEntity entity, JsonNode? initialData)
    {
        if (Regex != null) {
            // assume it's expected to be unique - always start empty maybe?
            return (-1, new StringResource(string.Empty));
        } else {
            return (-1, new StringResource(initialData?.GetValue<string>() ?? string.Empty));
        }
    }

    public IEnumerable<KeyValuePair<long, IContentResource>> FetchInstances(ResourceManager workspace)
    {
        return Field.Config.Type == null ? [] : workspace.GetResourceInstances(Field.Config.Type);
    }

    public override StringResource? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        var res = entity.Get(Field.name) as StringResource;
        if (res == null) {
            res = new StringResource(initialFormat?.GetString(entity) ?? string.Empty);
        }
        return res;
    }

    public override IContentResource? LoadValue(ContentWorkspace workspace, ResourceEntity entity, ResourceState state)
    {
        return new StringResource(initialFormat?.GetString(entity) ?? string.Empty);
    }
}

public sealed class StringResource : IContentResource
{
    public StringResource() {}
    public StringResource(string str)
    {
        Text = str;
    }

    public string Text { get; set; } = string.Empty;
    public string ResourceTypeID => "string";
    public string? FileResourcePath => null;

    public IContentResource Clone() => new StringResource() { Text = Text };

    public JsonNode ToJson(Workspace env) => JsonValue.Create(Text);

    public override string ToString() => Text;
}
