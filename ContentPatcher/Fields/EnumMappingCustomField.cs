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
public class EnumMappingCustomField : CustomEntityFieldHandler<EnumMappingResource>, IResourceValueContainer
{
    public Regex? ParseRegex { get; private set; }
    private string virtualEnumName = "";
    private StringFormatter newLabelFormat = null!;
    private NestableFieldAccessor idGetter = null!;
    public override string? ResourceTypeId => virtualEnumName;
    private long fallbackId;

    public override void LoadParams(EntityFieldConfig data)
    {
        var pattern = data.RequireParam<string>("label_id_parse");
        ParseRegex = new Regex(pattern);
        virtualEnumName = data.RequireParam<string>("virtual_enum_name");
        fallbackId = data.GetParam<long>("fallback_id", 0);
    }

    public override void EntitySetup(EntityConfig entityConfig, ContentWorkspace workspace)
    {
        var format = Field.config.RequireParam<string>("new_label_format");
        newLabelFormat = new StringFormatter(format, FormatterSettings.CreateFullEntityFormatter(entityConfig, workspace));

        var valueFrom = Field.config.RequireParam<Dictionary<object, object>>("value_from");
        idGetter = NestableFieldAccessor.CreateForEntity(workspace, entityConfig, valueFrom);
    }

    public override EnumMappingResource? ApplyValue(ContentWorkspace workspace, EnumMappingResource? currentResource, JsonNode? data, ResourceEntity entity, ResourceState state)
    {
        if (data == null) {
            return null;
        }
        var newStr = data.GetValue<string>();
        if (currentResource?.Label != newStr) {
            entity.Set(Field.name, currentResource = new EnumMappingResource(data.GetValue<string>()));
        }
        return currentResource;
    }

    public override IContentResource LoadValue(ContentWorkspace workspace, ResourceEntity entity, ResourceState state)
    {
        // note: do we want proper ResourceState handling for this one too?
        return DetermineEnumResource(workspace, entity);
    }

    public override EnumMappingResource? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        // note: do we want proper ResourceState handling for this one too?
        if (entity.Get(Field.name) is EnumMappingResource res) {
            return res;
        }

        return DetermineEnumResource(workspace, entity);
    }

    private EnumMappingResource DetermineEnumResource(ContentWorkspace workspace, ResourceEntity entity)
    {
        var enumdesc = workspace.Env.TypeCache.GetEnumDescriptor(Field.Config.RszClassRequired.name, RszFieldType.U32);
        var value = Convert.ChangeType(idGetter.Get(entity), enumdesc.BackingType);
        if (value == null) {
            Logger.Error($"Failed to determine enum value for entity {entity}");
            return new EnumMappingResource("", -1) { ID = -1 };
        }

        var label = enumdesc.GetLabel(value);
        var id = GetIDFromLabel(label);
        if (id == -1) {
            id = fallbackId;
        }
        var virtualEnum = workspace.Env.TypeCache.CreateEnum(virtualEnumName, "System.UInt32");
        virtualEnum?.AddValue(id, label);

        return new EnumMappingResource(label, Convert.ToInt64(value)) { ID = id };
    }

    public override (long id, IContentResource resource) CreateValue(ContentWorkspace workspace, ResourceEntity entity, JsonNode? initialData)
    {
        var label = newLabelFormat.GetString(entity);
        var value = MurMur3HashUtils.GetHash(label);
        if (Field.Config.RszClass != null) {
            var enumdesc = workspace.Env.TypeCache.GetEnumDescriptor(Field.Config.RszClass.name, RszFieldType.U32);
            enumdesc.AddValue(value, label);
            if (workspace.CurrentBundle != null) {
                var entries = workspace.CurrentBundle.AddEnumData(Field.Config.RszClass.name);
                entries[label] = enumdesc.GetValue(label);
            }
        }
        var id = GetIDFromLabel(label);
        if (id == -1) {
            id = fallbackId;
            Logger.Error("New enum format " + newLabelFormat + " does not match the expected ID regex");
        }
        var virtualEnum = workspace.Env.TypeCache.CreateEnum(virtualEnumName, "System.UInt32");
        virtualEnum?.AddValue(id, label);

        return (id, new EnumMappingResource(label, value) { ID = id });
    }

    private long GetIDFromLabel(string label)
    {
        var result = ParseRegex?.Match(label);
        if (result != null && result.Success && long.TryParse(result.Groups[1].Value, out var id)) {
            return id;
        }

        return -1;
    }

    public NestableFieldAccessor? GetAccessor(ContentWorkspace workspace, string path)
    {
        if (path == "value") {
            var valueType = RszInstance.RszFieldTypeToCSharpType(idGetter.Field.type);
            return new NestableFieldAccessor.Custom<EnumMappingResource>(idGetter.Field.type, m => Convert.ChangeType(m.Value, valueType), (m, v) => m.Value = Convert.ToInt64(v));
        }

        return null;
    }
}

[ResourcePatcher("enum_mapping")]
public class EnumMapResourceHandler : ResourceHandler, IResourceHandlerStatic
{
    public override EntityFieldValueHandler CreateValueHandler(EntityField field) => new EnumMappingCustomField();

    public static ResourceHandler Deserialize(ResourceConfig resource, EntityResourceConfigSerialized data, ContentWorkspace workspace)
    {
        return new EnumMapResourceHandler() { Config = resource };
    }

    public override void ModifyResources(ContentWorkspace workspace, IEnumerable<KeyValuePair<long, IContentResource>> resources)
    {
    }

    public override void ReadResources(ContentWorkspace env, Dictionary<long, IContentResource> dict)
    {
    }
}

public sealed class EnumMappingResource : IAddressableContentResource
{
    public EnumMappingResource() {}
    public EnumMappingResource(string label, long value = -1)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; set; } = string.Empty;
    public long Value { get; set; }
    public string ResourceTypeID => "enum_mapping";
    public string? FileResourcePath => null;

    public long ID { get; set; }

    public IContentResource Clone() => new EnumMappingResource() { Label = Label };

    public JsonNode ToJson(Workspace env) => JsonValue.Create(Label);

    public override string ToString() => Label;
}
