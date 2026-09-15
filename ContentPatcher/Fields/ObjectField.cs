using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourceField("object")]
public class ObjectField : EntityFieldValueHandler, IMainField, IDiffableField
{
    public bool? forceNested;

    public override void LoadParams(EntityFieldConfig data)
    {
        if (data.TryGetParam<bool>("nested", out bool nested)) {
            forceNested = nested;
        }
    }
}
