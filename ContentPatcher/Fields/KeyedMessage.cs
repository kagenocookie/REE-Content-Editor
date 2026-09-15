using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib.Common;
using ReeLib.Msg;

namespace ContentPatcher;

[ResourceField("keyed_message", typeof(MsgFileResourceHandler))]
public class KeyedMessage : EntityFieldValueHandler, IDiffableField, ICustomEntityResourceIdMapper
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
        var key = GetID(entity);
        return workspace.ResourceManager.GetResourceInstance(Field.Config.Type, key, state);
    }

    public long GetID(ResourceEntity entity)
    {
        var str = keyFormat.GetString(entity);
        return MurMur3HashUtils.GetHash(str);
    }

    public override IContentResource? ApplyValue(ContentWorkspace workspace, IContentResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data is not JsonObject obj) {
            data = obj = new JsonObject();
        }
        string entityKey = keyFormat.GetString(entity);
        var messageId = MurMur3HashUtils.GetHash(entityKey);
        obj["MessageKey"] = entityKey;
        return base.ApplyValue(workspace, currentResource, data, entity, state);
    }
}
