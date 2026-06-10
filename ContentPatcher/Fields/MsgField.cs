using System.Text.Json.Nodes;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib.Common;

namespace ContentPatcher;

[ResourceField("msg")]
public class MsgField : EntityField<MessageData>, IDiffableField
{
    public string keyFormat = null!;
    public StringFormatter? formatter;
    public string resourceType = null!;
    public bool multiline;

    bool IDiffableField.EnableDiff => true;
    public override string ResourceTypeId => resourceType;

    public override void LoadParams(string fieldName, Dictionary<string, object>? param)
    {
        ArgumentNullException.ThrowIfNull(param, nameof(param));
        keyFormat = (string)param["key"];
        resourceType = (string)param["resource"];
        multiline = param.GetValueOrDefault("multiline") is bool bb ? bb : false;
    }

    public IEnumerable<KeyValuePair<long, IContentResource>> FetchInstances(ResourceManager resources)
    {
        return resources.GetResourceInstances(resourceType);
    }

    public override IContentResource? FetchResource(ResourceManager resources, ResourceEntity entity, ResourceState state)
    {
        string entityKey = FormatMessageKey(entity);
        var messageId = MurMur3HashUtils.GetHash(entityKey);
        return resources.GetResourceInstance(resourceType, messageId, state);
    }

    public override MessageData? ApplyValue(ContentWorkspace workspace, MessageData? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            // TODO delete object (how?)
            return null;
        }
        if (currentResource == null) {
            string entityKey = FormatMessageKey(entity);
            var messageId = MurMur3HashUtils.GetHash(entityKey);
            var inst = workspace.ResourceManager.CreateEntityResource<MessageData>(entity, this, state, resourceType);
            workspace.Diff.ApplyDiff(inst, data);
            return inst;
        }
        if (currentResource is MessageData instance) {
            workspace.Diff.ApplyDiff(instance, data);
            return instance;
        }
        throw new NotImplementedException();
    }

    private string FormatMessageKey(ResourceEntity entity)
    {
        if (formatter == null) {
            formatter = new StringFormatter(keyFormat, FormatterSettings.CreateFullEntityFormatter(entity.Config));
        }
        return formatter.GetString(entity);
    }
}
