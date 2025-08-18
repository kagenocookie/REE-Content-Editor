using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ContentEditor.Core;
using ReeLib;

namespace ContentPatcher.DD2;

public class ItemIconResource : IContentResource
{
    public ItemRectData data = new();

    public string ResourceIdentifier => data.IconTexture + data.IconRect;
    public string? FilePath => data.IconTexture;

    public IContentResource Clone() => new ItemIconResource() { data = data.Clone() };

    public JsonNode ToJson(Workspace env) => JsonSerializer.SerializeToNode(data, JsonConfig.jsonOptionsIncludeFields)!;
    public static ItemIconResource.ItemRectData FromJson(JsonNode json) => json.Deserialize<ItemIconResource.ItemRectData>(JsonConfig.jsonOptionsIncludeFields)!;

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
        public float h;
        public float w;

        public override string ToString() => $"{x} {y} {w} {h}";
    }
}

[ResourceField("DD2_ItemIcon", "dd2")]
public class ItemIconField : CustomField<ItemIconResource>
{
    public override string? ResourceIdentifier => null;

    public override void LoadParams(string fieldName, Dictionary<string, object>? param)
    {
    }

    public override ItemIconResource? ApplyValue(ContentWorkspace workspace, ItemIconResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        var parsedData = data == null ? null : ItemIconResource.FromJson(data);
        if (currentResource == null) {
            currentResource = new ItemIconResource() {};
            currentResource.data.IconRect.w = 144;
            currentResource.data.IconRect.h = 160;
        }
        currentResource.data = parsedData ?? currentResource.data;
        return currentResource;
    }

    public override IContentResource? FetchResource(ResourceManager resources, ResourceEntity entity, ResourceState state)
    {
        return entity.Get(name);
    }
}
