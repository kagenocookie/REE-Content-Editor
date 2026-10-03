using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

[ResourceField("objectArray")]
public class ObjectArray : EntityFieldValueHandler, IMainField, IDiffableField
{
    private string? elementClassname;
    public override string ResourceType => Field.Config.Type;
    public string? Classname => elementClassname ?? Field.Config.RszClass?.name;

    public override void LoadParams(EntityFieldConfig param)
    {
        elementClassname = param.resource?.Classname;
    }

    public override IContentResource? FetchResource(ContentWorkspace workspace, ResourceEntity entity, long resourceId, ResourceState state)
    {
        var instance = workspace.ResourceManager.GetResourceInstance(Field.Config.Type, resourceId, state);
        elementClassname ??= (instance as RSZObjectListResource)?.Instances.FirstOrDefault()?.RszClass.name;
        return instance;
    }
}
