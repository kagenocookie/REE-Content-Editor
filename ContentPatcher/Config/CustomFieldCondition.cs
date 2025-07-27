namespace ContentPatcher;

public interface CustomFieldCondition
{
    bool IsEnabled(ResourceEntity entity);
}

public class WhenClassnameCondition(string field, string classname) : CustomFieldCondition
{
    public bool IsEnabled(ResourceEntity entity)
    {
        var value = entity.Get(field);
        if (value == null) return false;

        if (value is not RSZObjectResource obj) {
            throw new Exception($"Invalid field {field} for classname condition - must be an RSZObjectInstance");
        }

        return obj.Instance.RszClass.name == classname;
    }
}
