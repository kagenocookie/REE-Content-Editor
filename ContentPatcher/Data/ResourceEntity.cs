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

    public IContentResource? Get(string name)
    {
        return FieldValues.GetValueOrDefault(name);
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

            if (field is not IDiffableField diffable || !diffable.EnableDiff) {
                resultDiff ??= new();
                resultDiff[name] = value?.ToJson(workspace.Env);
                continue;
            }

            var baseValue = field.FetchResource(workspace.ResourceManager, this, ResourceState.Base);
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

            var newValue = field.ApplyValue(workspace, currentValue, data, this, state);
            Set(name, newValue);
        }
    }

    /// <summary>
    /// Fetches all referenced resources with the given state. If Active state, resources will be copied from the base state data if found.
    /// </summary>
    public void LoadResources(ResourceManager resources, ResourceState state)
    {
        foreach (var field in Config.Fields) {
            if (field.Condition?.IsEnabled(this) == false) {
                continue;
            }

            var value = Get(field.name);
            if (value != null) {
                // note: if multiple active entities reference the same resource, they'll all get the same instance
                // that's fine, since it's not like we can have multiple variants of a single resource anyway
                FieldValues[field.name] = field.FetchResource(resources, this, state);
            }
        }
    }
}
