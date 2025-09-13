using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib;
using ReeLib.Common;
using SmartFormat;
using SmartFormat.Extensions;

namespace ContentPatcher;

[ResourceField("string")]
public class StringCustomField : CustomField<StringResource>, ICustomResourceField, IDiffableField
{
    public Regex? Regex { get; private set; }
    public string? RegexDescription { get; private set; }
    public string? Tooltip { get; private set; }
    private string? initialFormatString;
    private StringFormatter? initialFormat;
    public override string? ResourceIdentifier => null;

    public override void LoadParams(string fieldName, Dictionary<string, object>? param)
    {
        var pattern = param?.GetValueOrDefault("regex") as string;
        if (pattern != null) Regex = new Regex(pattern);
        RegexDescription = param?.GetValueOrDefault("regexDescription") as string;
        Tooltip = param?.GetValueOrDefault("tooltip") as string;
        initialFormatString = param?.GetValueOrDefault("initial") as string;
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
            entity.Set(name, currentResource = new StringResource(data.GetValue<string>()));
        }
        return currentResource;
    }

    public ClassConfig CreateConfig()
    {
        var cfg = new ClassConfig();
        cfg.IDFields = [0];
        return cfg;
    }

    public (long id, IContentResource resource) CreateResource(ContentWorkspace workspace, ClassConfig config, ResourceEntity entity, JsonNode? initialData)
    {
        if (Regex != null) {
            // assume it's expected to be unique - always start empty maybe?
            return (Random.Shared.NextInt64(), new StringResource(string.Empty));
        } else {
            return (Random.Shared.NextInt64(), new StringResource(initialData?.GetValue<string>() ?? string.Empty));
        }
    }

    public IEnumerable<KeyValuePair<long, IContentResource>> FetchInstances(ResourceManager workspace)
    {
        return ResourceIdentifier == null ? [] : workspace.GetResourceInstances(ResourceIdentifier);
    }

    public override StringResource? FetchResource(ResourceManager workspace, ResourceEntity entity, ResourceState state)
    {
        // if (state == ResourceState.Base) return null;

        var res = entity.Get(name) as StringResource;
        if (res == null) {
            res = new StringResource(initialFormat?.GetString(entity) ?? string.Empty);
        }
        return res;
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
    public string ResourceIdentifier => "string";
    public string? FilePath => null;

    public IContentResource Clone() => new StringResource() { Text = Text };

    public JsonNode ToJson(Workspace env) => JsonValue.Create(Text);

    public override string ToString() => Text;
}
