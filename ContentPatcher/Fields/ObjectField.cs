using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourceField("object")]
public class ObjectField : EntityFieldValueHandler, IMainField, IDiffableField
{
}
