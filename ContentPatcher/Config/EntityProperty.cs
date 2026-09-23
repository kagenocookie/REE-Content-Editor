using VYaml.Annotations;

namespace ContentPatcher;

[YamlObject]
public partial record EntityProperty(string field, string path)
{
    public ContentWorkspace Workspace { get; set; } = null!;

    public static EntityProperty Deserialize(ContentWorkspace workspace, Dictionary<object, object> data)
    {
        if (!data.TryGetValue("field", out var fieldRaw) || fieldRaw == null) {
            throw new Exception("Missing field for EntityProperty");
        }
        if (!data.TryGetValue("path", out var pathRaw) || pathRaw == null) {
            throw new Exception("Missing path for EntityProperty");
        }
        return new EntityProperty((string)fieldRaw, (string)pathRaw) { Workspace = workspace };
    }

    public object? Get(object instance)
    {
        if (instance is not ResourceEntity entity) {
            throw new NotImplementedException();
        }
        var value = entity.Get(field);
        if (value is IPropertyContainer pc) {
            return pc.Get(path);
        }
        if (value == null) return null;
        throw new NotImplementedException();
    }

    public void Set(object instance, object value)
    {
        if (instance is not ResourceEntity entity) {
            throw new NotImplementedException();
        }
        if (entity.Get(field) is IPropertyContainer pc) {
            pc.Set(path, value);
        }
        throw new NotImplementedException();
    }
}

public interface IEntityCondition
{
    bool IsEnabled(ResourceEntity entity);

    public static IEntityCondition Deserialize(EntityFieldConditionData data, string fallbackField)
    {
        if (data.notEquals != null) {
            return new InvertEntityCondition(EntityPropertyValueEquals.Create(data, data.notEquals, fallbackField));
        }

        return EntityPropertyValueEquals.Create(data, data.equals, fallbackField);
    }
}

public class EntityPropertyValueEquals(string field, string path, object? compareValue) : IEntityCondition
{
    public static EntityPropertyValueEquals Create(EntityFieldConditionData data, object? compareValue, string fieldFallback)
        => new EntityPropertyValueEquals(data.field ?? fieldFallback, data.property, compareValue);

    public EntityProperty Property { get; set; } = new EntityProperty(field, path);

    public bool IsEnabled(ResourceEntity entity)
    {
        var value = Property.Get(entity);
        if (value == compareValue) return true;
        if (value == null || compareValue == null) return false;

        var fieldType = value.GetType();
        if (fieldType == compareValue.GetType()) {
            return value.Equals(compareValue);
        } else {
            return Convert.ChangeType(compareValue, fieldType).Equals(value);
        }
    }
}

public class EntityPropertyAnyCondition(IEntityCondition[] conditions) : IEntityCondition
{
    public static EntityPropertyAnyCondition Create(EntityFieldConditionData[] data, string fieldFallback)
        => new EntityPropertyAnyCondition(data.Select(d => IEntityCondition.Deserialize(d, fieldFallback)).ToArray());

    public bool IsEnabled(ResourceEntity entity)
    {
        foreach (var c in conditions) {
            if (c.IsEnabled(entity)) return true;
        }
        return false;
    }
}

public class InvertEntityCondition(IEntityCondition condition) : IEntityCondition
{
    public static EntityPropertyAnyCondition Create(EntityFieldConditionData[] data, string fieldFallback)
        => new EntityPropertyAnyCondition(data.Select(d => new EntityPropertyValueEquals(d.field ?? fieldFallback, d.property, d.equals)).ToArray());

    public bool IsEnabled(ResourceEntity entity) => !condition.IsEnabled(entity);
}