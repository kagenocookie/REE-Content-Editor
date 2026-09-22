using ContentEditor;
using ReeLib;

namespace ContentPatcher;

public interface IResourceCondition
{
    bool IsEnabled(object? resource);

    public static IResourceCondition Deserialize(ResourceConditionData data)
    {
        if (data.property == "classname") {
            return new WhenClassnameCondition(data.property, data.equals as string ?? "");
        } else {
            return new WhenFieldValueCondition(data.property, data.equals);
        }
    }
}

public interface ISettable
{
    void Set(object target);
}

public class WhenAnyCondition(IResourceCondition[] subconditions) : IResourceCondition
{
    public bool IsEnabled(object? resource)
    {
        foreach (var c in subconditions) {
            if (c.IsEnabled(resource)) return true;
        }

        return false;
    }
}

public class WhenAllCondition(IResourceCondition[] subconditions) : IResourceCondition, ISettable
{
    public bool IsEnabled(object? resource)
    {
        foreach (var c in subconditions) {
            if (!c.IsEnabled(resource)) return false;
        }

        return true;
    }

    public void Set(object target)
    {
        foreach (var c in subconditions) {
            if (c is ISettable ss) {
                ss.Set(target);
            }
        }
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
        object? fieldValue;
        if (resource is IPropertyContainer props) {
            fieldValue = props.Get(field);
        } else if (resource is RszInstance rsz) {
            fieldValue = rsz.GetNestedFieldValue(field);
        } else {
            throw new Exception($"Invalid field {field} for field condition - must be an RszInstance or RSZObjectInstance");
        }

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
        if (compareValue == null) {
            // I _think_ we don't want this to happen but let's not exception just yet
            Logger.Error($"Attempted to set null value for {target} field {field}");
        }

        var instance = (target as RSZObjectResource)?.Instance ?? (target as RszInstance);
        if (instance == null) {
            throw new Exception($"Invalid field {field} for field condition - must be an RszInstance or RSZObjectInstance");
        }

        if (target is IPropertyContainer props) {
            props.Set(field, compareValue);
        } else if (target is RszInstance rsz) {
            instance.SetNestedFieldValue(field, compareValue!);
        } else {
            throw new Exception($"Invalid field {field} for field condition - must be an RszInstance or RSZObjectInstance");
        }
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
