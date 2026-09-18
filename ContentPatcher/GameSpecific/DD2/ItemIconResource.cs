using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ContentEditor.Core;
using ReeLib;

namespace ContentPatcher.DD2;

public class ItemIconResource(ResourceConfig config) : IContentResource
{
    public ItemRectData data = new();

    public ResourceConfig ResourceType { get; } = config;
    public string? FileResourcePath => data.IconTexture;

    public IContentResource Clone() => new ItemIconResource(ResourceType) { data = data.Clone() };

    public JsonNode ToJson(Workspace env) => JsonSerializer.SerializeToNode(data, JsonConfig.jsonOptionsIncludeFields)!;
    public static ItemIconResource.ItemRectData FromJson(JsonNode json)
        => json.Deserialize<ItemIconResource.ItemRectData>(JsonConfig.jsonOptionsIncludeFields)!;

    public class ItemRectData
    {
        [JsonPropertyName("icon_path")]
        public string? IconTexture;

        [JsonPropertyName("icon_rect")]
        public ItemRect IconRect = new();

        public ItemRectData Clone() => new ItemRectData() {
            IconTexture = IconTexture,
            IconRect = new ItemRect() { x = IconRect.x, y = IconRect.y, w = IconRect.w, h = IconRect.h }
        };
    }

    public class ItemRect
    {
        public float x;
        public float y;
        public float h = 160;
        public float w = 144;

        public override string ToString() => $"{x} {y} {w} {h}";
    }
}

[ResourceField("DD2_ItemIcon", null, "dd2")]
public class ItemIconField : CustomEntityFieldHandler<ItemIconResource>
{
    public override string? ResourceType => null;

    public override ItemIconResource ApplyValue(ContentWorkspace workspace, ItemIconResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        var parsedData = data == null ? null : ItemIconResource.FromJson(data);
        if (currentResource == null) {
            currentResource = new ItemIconResource(Field.Config) {};
            currentResource.data.IconRect.w = 144;
            currentResource.data.IconRect.h = 160;
        }
        currentResource.data = parsedData ?? currentResource.data;
        return currentResource;
    }

    public override ItemIconResource FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        return entity.Get<ItemIconResource>(Field.name) ?? new ItemIconResource(Field.Config);
    }

    public override ResourceHandler? CreateResourceHandler(ResourceConfig config) => new NoopResourceHandler<ItemIconField>() { Config = config };
}
