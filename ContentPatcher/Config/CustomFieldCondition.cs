using ContentEditor;
using ReeLib;

namespace ContentPatcher;

public interface IResourceCondition
{
    bool IsEnabled(object? resource);
}

public interface ISettable
{
    void Set(object target);
}

public sealed record EntityFieldCondition(string field, IResourceCondition condition)
{
    public bool IsEnabled(ResourceEntity entity)
    {
        var value = entity.Get(field);
        if (value == null) return false;

        return condition.IsEnabled(value);
    }
}

public class WhenClassnameCondition(string field, string classname) : IResourceCondition
{
    public bool IsEnabled(object? resource)
    {
        var instance = (resource as RSZObjectResource)?.Instance ?? (resource as RszInstance);
        if (instance != null) {
            return instance.RszClass.name == classname;
        }

        throw new Exception($"Invalid field {field} for classname condition - must be an RSZObjectInstance");
    }
}

public class WhenFieldValueCondition(string field, object? compareValue) : IResourceCondition, ISettable
{
    public bool IsEnabled(object? resource)
    {
        var instance = (resource as RSZObjectResource)?.Instance ?? (resource as RszInstance);
        if (instance == null) {
            throw new Exception($"Invalid field {field} for field condition - must be an RszInstance or RSZObjectInstance");
        }

        var fieldValue = instance.GetNestedFieldValue(field);
        if (fieldValue == compareValue) return true;
        if (fieldValue == null || compareValue == null) return false;

        if (fieldValue.GetType() == compareValue.GetType()) {
            return fieldValue.Equals(compareValue);
        } else {
            return Convert.ChangeType(compareValue, fieldValue.GetType()).Equals(fieldValue);
        }
    }

    public void Set(object target)
    {
        var instance = (target as RSZObjectResource)?.Instance ?? (target as RszInstance);
        if (instance == null) {
            throw new Exception($"Invalid field {field} for field condition - must be an RszInstance or RSZObjectInstance");
        }
        if (compareValue == null) {
            // I _think_ we don't want this to happen but let's not exception just yet
            Logger.Error($"Attempted to set null value for {instance} field {field}");
        }

        instance.SetNestedFieldValue(field, compareValue!);
    }
}

public class ContextParentFieldValueCondition(string field, object? compareValue) : IResourceCondition
{
    public bool IsEnabled(object? target)
    {
        if (target is not UIContext context) {
            throw new Exception($"Invalid {nameof(ContextParentFieldValueCondition)} condition for {field} - must be a UIContext");
        }

        var resource = context.parent?.GetRaw();
        var instance = (resource as RSZObjectResource)?.Instance ?? (resource as RszInstance);
        if (instance == null) {
            throw new Exception($"Invalid field {field} for field condition - must be an RszInstance or RSZObjectInstance");
        }

        var fieldValue = instance.GetNestedFieldValue(field);
        if (fieldValue == compareValue) return true;
        if (fieldValue == null || compareValue == null) return false;

        var fieldType = fieldValue.GetType();
        if (fieldType == compareValue.GetType()) {
            return fieldValue.Equals(compareValue);
        } else {
            return Convert.ChangeType(compareValue, fieldType).Equals(fieldValue);
        }
    }
}
