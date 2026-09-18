using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContentEditor;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib;
using ReeLib.Common;

namespace ContentPatcher;

[ResourceField("enum_mapping", typeof(EnumMapResourceHandler))]
public class EnumMappingCustomField : CustomEntityFieldHandler<EnumMappingResource>, ICustomEntityResourceIdMapper
{
    public Regex? ParseRegex { get; private set; }
    private string virtualEnumName = "";
    private StringFormatter newLabelFormat = null!;
    private EntityProperty idProperty = null!;
    public override string? ResourceType => virtualEnumName;
    private long fallbackId;

    public override void LoadParams(EntityFieldConfig data)
    {
        var pattern = data.RequireParam<string>("label_id_parse");
        ParseRegex = new Regex(pattern);
        virtualEnumName = data.RequireParam<string>("virtual_enum_name");
        fallbackId = data.GetParam<long>("fallback_id", 0);
    }

    public long GetID(ResourceEntity entity)
    {
        return entity.Id;
    }

    public override void EntitySetup(EntityConfig entityConfig, ContentWorkspace workspace)
    {
        var format = Field.config.RequireParam<string>("new_label_format");
        newLabelFormat = new StringFormatter(format, FormatterSettings.CreateFullEntityFormatter(entityConfig, workspace));

        var valueFrom = Field.config.RequireParam<Dictionary<object, object>>("value_from");
        idProperty = EntityProperty.Deserialize(workspace, valueFrom);
    }

    public override EnumMappingResource? ApplyValue(ContentWorkspace workspace, EnumMappingResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            return null;
        }
        var newStr = data.GetValue<string>();
        if (currentResource?.Label != newStr) {
            entity.Set(Field.name, currentResource = new EnumMappingResource(Field.Config, data.GetValue<string>()));
        }
        return currentResource;
    }

    public override EnumMappingResource? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        // note: do we want proper ResourceState handling for this one too?
        return entity.Get<EnumMappingResource>(Field.name) ?? DetermineEnumResource(workspace, entity);
    }

    private EnumMappingResource DetermineEnumResource(ContentWorkspace workspace, ResourceEntity entity)
    {
        var enumdesc = workspace.Env.TypeCache.GetEnumDescriptor(Field.Config.RszClassRequired.name, RszFieldType.U32);
        var value = Convert.ChangeType(idProperty.Get(entity) ?? entity.Id, enumdesc.BackingType);
        if (value == null) {
            Logger.Error($"Failed to determine enum value for entity {entity}");
            return new EnumMappingResource(Field.Config, "", -1) { ID = -1 };
        }

        var label = enumdesc.GetLabel(value);
        long id;
        if (string.IsNullOrEmpty(label)) {
            // imported values that lack enum definitions
            // TODO make sure we insert custom bundled enum entries before we do the loading here
            id = entity.Id;
            label = newLabelFormat.GetString(entity);
        } else {
            id = GetIDFromLabel(label);
        }
        if (id == -1) {
            id = fallbackId;
        }
        var virtualEnum = workspace.Env.TypeCache.CreateEnum(virtualEnumName, "System.UInt32");
        virtualEnum?.AddValue(id, label);

        return new EnumMappingResource(Field.Config, label, Convert.ToInt64(value)) { ID = id };
    }

    private long GetIDFromLabel(string label)
    {
        var result = ParseRegex?.Match(label);
        if (result != null && result.Success && long.TryParse(result.Groups[1].Value, out var id)) {
            return id;
        }

        return -1;
    }
}

[ResourcePatcher("enum_mapping")]
public class EnumMapResourceHandler : ResourceHandler, IResourceHandlerStatic
{
    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => new EnumMappingCustomField();

    public static ResourceHandler Deserialize(ResourceConfig resource, ResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new EnumMapResourceHandler() { Config = resource };
    }

    public override IContentResource ApplyResourceData(ContentWorkspace workspace, IContentResource? resource, JsonNode? data)
    {
        throw new Exception($"Creating blank resources of type {Config} is not supported");
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
    }

    public override void ReadResources(ContentWorkspace env, Dictionary<long, IContentResource> dict)
    {
    }
}

public sealed class EnumMappingResource : IAddressableContentResource, IPropertyContainer
{
    public EnumMappingResource(ResourceConfig config)
    {
        ResourceType = config;
    }
    public EnumMappingResource(ResourceConfig config, string label, long value = -1)
    {
        ResourceType = config;
        Label = label;
        Value = value;
    }

    public string Label { get; set; } = string.Empty;
    public long Value { get; set; }
    public ResourceConfig ResourceType { get; }
    public string? FileResourcePath => null;

    public long ID { get; set; }

    public IContentResource Clone() => new EnumMappingResource(ResourceType, Label);

    public JsonNode ToJson(Workspace env) => JsonValue.Create(Label);

    public override string ToString() => Label;

    public object? Get(string path)
    {
        switch (path) {
            case "value":
                return Value;
            case "id":
                return ID;
            case "label":
                return Label;
            default:
                throw new Exception("Unknown enum mapping field " + path);
        }
    }

    public void Set(string path, object? value)
    {
        switch (path) {
            case "value":
                Value = Convert.ToInt64(value);
                break;
            case "id":
                ID = Convert.ToInt64(value);
                break;
            case "label":
                Label = value?.ToString() ?? "";
                break;
            default:
                throw new Exception("Unknown enum mapping field " + path);
        }
    }
}
