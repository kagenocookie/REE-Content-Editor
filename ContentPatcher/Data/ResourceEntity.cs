using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ContentEditor;
using ContentEditor.Core;

namespace ContentPatcher;

public class ResourceEntity : Entity
{
    public ResourceEntity(long id, string type, EntityConfig config)
    {
        Id = id;
        Type = type;
        Config = config;
    }

    public ResourceEntity(Entity source, EntityConfig config)
    {
        Id = source.Id;
        Type = source.Type;
        Label = source.Label;
        Data = source.Data;
        Config = config;
    }

    public readonly Dictionary<string, IContentResource?> FieldValues = new();

    [JsonIgnore]
    public EntityConfig Config { get; }

    public void Set(string name, IContentResource? instance)
    {
        FieldValues[name] = instance;
    }

    public void Set(EntityFieldValueHandler handler, IContentResource? instance)
    {
        Set(handler.Field.name, instance);
    }

    public IContentResource? Get(string name)
    {
        return FieldValues.GetValueOrDefault(name);
    }

    public IContentResource? Get(EntityFieldValueHandler handler)
    {
        return FieldValues.GetValueOrDefault(handler.Field.name);
    }

    public T? Get<T>(string name) where T : class, IContentResource
    {
        return FieldValues.GetValueOrDefault(name) as T;
    }

    public long GetFieldId(string field)
    {
        var fieldCfg = Config.GetField(field);
        return fieldCfg?.IdField == null ? Id : Convert.ToInt64(fieldCfg.IdField.Get(this));
    }

    public Dictionary<string, JsonNode?>? CalculateDiff(ContentWorkspace workspace)
    {
        var differ = new DiffMaker();
        Dictionary<string, JsonNode?>? resultDiff = null;
        foreach (var (name, value) in FieldValues) {
            var field = Config.GetField(name);
            if (field == null) continue;

            if (field.Condition?.IsEnabled(this) == false) {
                continue;
            }

            if (field.ValueHandler is not IDiffableField diffable || !diffable.EnableDiff) {
                // always store full value for non-diffable fields
                resultDiff ??= new();
                resultDiff[name] = value?.ToJson(workspace.Env);
                continue;
            }

            var resourceId = field.IdField == null ? Id : Convert.ToInt64(field.IdField.Get(this));
            var baseValue = field.ValueHandler.FetchResource(workspace, this, resourceId, ResourceState.Base);
            if (baseValue == null) {
                if (value == null) {
                    continue;
                }

                // no diff needed, apply in full
                resultDiff ??= new();
                resultDiff[name] = value.ToJson(workspace.Env);
                continue;
            }

            if (value == null) {
                // TODO remove resource
                Logger.Error("Resource deletion not yet supported!");
                continue;
            }

            var diff = diffable.GetDiff(workspace, value, baseValue);
            if (diff != null) {
                resultDiff ??= new();
                resultDiff[name] = diff;
            }
        }

        return Data = resultDiff;
    }

    public void ApplyDataValues(ContentWorkspace workspace, ResourceState state)
    {
        if (Data == null) return;

        foreach (var (name, data) in Data) {
            var field = Config.GetField(name);
            if (field == null) {
                Logger.Error($"Unknown field {name} for entity type {Type}. Ignoring.");
                continue;
            }
            var currentValue = Get(name);
            if (currentValue == null && data == null) {
                continue;
            }

            // TODO how should this interact with source entity values? do we check both, only one?
            if (field.Condition?.IsEnabled(this) == false) {
                if (currentValue != null) {
                    Set(name, null);
                }
                continue;
            }

            var newValue = field.ValueHandler.ApplyValue(workspace, currentValue, data, this, state);
            Set(name, newValue);
        }
    }
}
