using System.Text.Json.Nodes;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib.Common;

namespace ContentPatcher;

[ResourceField("single-msg", typeof(MsgFileResourceHandler))]
public class SingleMsgCustomField : CustomEntityFieldHandler<MessageData>, IDiffableField
{
    public string keyFormat = null!;
    public StringFormatter? formatter;
    public string file = null!;
    public bool multiline;

    bool IDiffableField.EnableDiff => true;
    public override string ResourceTypeId => file;

    public override void LoadParams(EntityFieldConfig data)
    {
        keyFormat = data.RequireResourceSettings.Key ?? "";
        file = data.RequireResourceSettings.SingleFile;
        multiline = data.GetParam<bool>("multiline", false);
    }

    public override MessageData? ApplyValue(ContentWorkspace workspace, MessageData? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            // TODO remove resource
            return null;
        }
        if (currentResource == null) {
            string entityKey = FormatMessageKey(entity);
            var messageId = MurMur3HashUtils.GetHash(entityKey);
            currentResource = new MessageData() { ResourceTypeID = file, FileResourcePath = file!, MessageKey = entityKey, Guid = Guid.NewGuid() };
            workspace.ResourceManager.AddResource(file, messageId, currentResource, state);
        }
        workspace.Diff.ApplyDiff(currentResource, data);
        return currentResource;
    }

    public override (long id, IContentResource resource) CreateValue(ContentWorkspace workspace, ResourceEntity entity, JsonNode? initialData)
    {
        string entityKey = FormatMessageKey(entity);
        var messageId = MurMur3HashUtils.GetHash(entityKey);
        var data = new MessageData() { FileResourcePath = file!, Messages = new(), ResourceTypeID = file!, MessageKey = entityKey, Guid = Guid.NewGuid() };
        if (initialData != null) {
            workspace.Diff.ApplyDiff(data, initialData!);
            data.MessageKey = entityKey;
        }
        return (messageId, data);
    }

    public override IAddressableContentResource? LoadValue(ContentWorkspace workspace, ResourceEntity entity, ResourceState state)
    {
        string entityKey = FormatMessageKey(entity);
        var messageId = MurMur3HashUtils.GetHash(entityKey);
        var data = workspace.ResourceManager.GetResourceInstance(file, messageId, state) as MessageData;
        if (data != null) {
            data.MessageKey = entityKey;
            return data;
        }

        return null;
    }

    public override MessageData? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        string entityKey = FormatMessageKey(entity);
        var messageId = MurMur3HashUtils.GetHash(entityKey);
        var data = workspace.ResourceManager.GetResourceInstance(file, messageId, state) as MessageData;
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
}
