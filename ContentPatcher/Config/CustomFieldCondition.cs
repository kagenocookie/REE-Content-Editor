namespace ContentPatcher;

public interface ResourceCondition
{
    bool IsEnabled(IContentResource resource);
}

public sealed record EntityFieldCondition(string field, ResourceCondition condition)
{
    public bool IsEnabled(ResourceEntity entity)
    {
        var value = entity.Get(field);
        if (value == null) return false;

        return condition.IsEnabled(value);
    }
}

public class WhenClassnameCondition(string field, string classname) : ResourceCondition
{
    public bool IsEnabled(IContentResource resource)
    {
        if (resource is not RSZObjectResource obj) {
            throw new Exception($"Invalid field {field} for classname condition - must be an RSZObjectInstance");
        }

        return obj.Instance.RszClass.name == classname;
    }
}

public class WhenFieldValueCondition(string field, object? compareValue) : ResourceCondition
{
    public bool IsEnabled(IContentResource resource)
    {
        if (resource is not RSZObjectResource obj) {
            throw new Exception($"Invalid field {field} for classname condition - must be an RSZObjectInstance");
        }

        var fieldValue = obj.Instance.GetFieldValue(field);
        return fieldValue == null && compareValue == null || fieldValue?.Equals(compareValue) == true;
    }
}
