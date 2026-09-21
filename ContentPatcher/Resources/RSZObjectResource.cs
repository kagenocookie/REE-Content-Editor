using System.Text.Json.Nodes;
using ReeLib;

namespace ContentPatcher;

public class RSZObjectResource(ResourceConfig type, RszInstance instance, string file) : IContentResource, IPropertyContainer
{
    public ResourceConfig ResourceType => type;
    public string FileResourcePath => file;
    public RszInstance Instance { get; set; } = instance;

    public virtual IContentResource Clone() => new RSZObjectResource(ResourceType, Instance.Clone(), file);

    public object? Get(string path)
    {
        if (path == "classname") {
            return Instance.RszClass.name;
        }
        if (path == "class_shortname") {
            return Instance.RszClass.ShortName;
        }
        return Instance.GetNestedFieldValue(path);
    }

    public void Set(string path, object? value)
    {
        var currentValue = Get(path);
        if (currentValue != null && value?.GetType() != currentValue.GetType()) {
            value = Convert.ChangeType(value, currentValue.GetType());
        }
        Instance.SetNestedFieldValue(path, value ?? RszInstance.NULL);
    }

    public JsonNode ToJson(Workspace env) => Instance.ToJson(env);
}
