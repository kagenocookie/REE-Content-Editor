using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib.Common;

namespace ContentPatcher;

[ResourceField("keyed_message", typeof(MsgFileResourceHandler))]
public class KeyedMessage : EntityFieldValueHandler<MessageData>, IDiffableField
{
    private StringFormatter keyFormat = null!;
    public bool multiline;

    public override void LoadParams(EntityFieldConfig data)
    {
        multiline = data.GetParam<bool>("multiline", false);
    }

    public override void EntitySetup(EntityConfig entityConfig, ContentWorkspace workspace)
    {
        var format = Field.config.RequireParam<string>("key_format");
        keyFormat = new StringFormatter(format, FormatterSettings.CreateFullEntityFormatter(entityConfig, workspace));
    }

    public override IContentResource? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        var str = keyFormat.GetString(entity);
        var key = MurMur3HashUtils.GetHash(str);
        return workspace.ResourceManager.GetResourceInstance(Field.Config.Type, key, state);
    }

    public override MessageData? ApplyValue(ContentWorkspace workspace, MessageData? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            // TODO delete object (how?)
            return null;
        }
        if (currentResource == null) {
            string entityKey = keyFormat.GetString(entity);
            var messageId = MurMur3HashUtils.GetHash(entityKey);
            var inst = workspace.ResourceManager.CreateEntityResource<MessageData>(entity, Field, state, Field.Config.Type);
            workspace.Diff.ApplyDiff(inst, data);
            return inst;
        }
        if (currentResource is MessageData instance) {
            workspace.Diff.ApplyDiff(instance, data);
            return instance;
        }
        throw new NotImplementedException();
    }
}
