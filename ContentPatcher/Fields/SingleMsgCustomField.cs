using System.Text.Json.Nodes;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib.Common;
using SmartFormat;
using SmartFormat.Extensions;

namespace ContentPatcher;

[ResourceField("msg")]
public class SingleMsgCustomField : CustomField<MessageData>, ICustomResourceField, IDiffableField
{
    public string keyFormat = null!;
    public StringFormatter? formatter;
    public string file = null!;
    public bool multiline;
    public override string ResourceIdentifier => file;

    public override MessageData? ApplyValue(ContentWorkspace workspace, MessageData? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            // TODO remove resource
            return null;
        }
        if (currentResource == null) {
            string entityKey = FormatMessageKey(entity);
            var messageId = MurMur3HashUtils.GetHash(entityKey);
            currentResource = new MessageData() { ResourceIdentifier = file, FilePath = file!, MessageKey = entityKey, Guid = Guid.NewGuid() };
            workspace.ResourceManager.AddResource(file, messageId, currentResource, state);
        }
        workspace.Diff.ApplyDiff(currentResource, data);
        return currentResource;
    }

    public ClassConfig CreateConfig()
    {
        var cfg = new ClassConfig();
        cfg.Patcher = new SingleMsgHandler() { file = file, ResourceKey = file };
        cfg.IDFields = [0];
        // cfg.To_String = // msg key + msg value["en"]
        return cfg;
    }

    public (long id, IContentResource resource) CreateResource(ContentWorkspace workspace, ClassConfig config, ResourceEntity entity, JsonNode? initialData)
    {
        string entityKey = FormatMessageKey(entity);
        var messageId = MurMur3HashUtils.GetHash(entityKey);
        var data = new MessageData() { FilePath = file!, Messages = new(), ResourceIdentifier = file!, MessageKey = entityKey, Guid = Guid.NewGuid() };
        if (initialData != null) {
            workspace.Diff.ApplyDiff(data, initialData!);
            data.MessageKey = entityKey;
        }
        return (messageId, data);
    }

    public IEnumerable<KeyValuePair<long, IContentResource>> FetchInstances(ResourceManager workspace)
    {
        return workspace.GetResourceInstances(ResourceIdentifier);
    }

    public override MessageData? FetchResource(ResourceManager workspace, ResourceEntity entity, ResourceState state)
    {
        string entityKey = FormatMessageKey(entity);
        var messageId = MurMur3HashUtils.GetHash(entityKey);
        var data = workspace.GetResourceInstance(file, messageId, state) as MessageData;
        if (data != null) {
            data.MessageKey = entityKey;
        }
        return data;
    }

    private string FormatMessageKey(ResourceEntity entity)
    {
        if (formatter == null) {
            formatter = new StringFormatter(keyFormat, FormatterSettings.CreateFullEntityFormatter(entity.Config));
        }
        return formatter.GetString(entity);
    }

    public override void LoadParams(string fieldName, Dictionary<string, object>? param)
    {
        ArgumentNullException.ThrowIfNull(param);
        keyFormat = (string)param["key"];
        file = (string)param["file"];
        multiline = param.GetValueOrDefault("multiline") is bool b ? b : false;
    }
}
