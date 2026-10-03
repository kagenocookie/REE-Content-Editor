using System.Text.Json.Nodes;

namespace ContentPatcher;

[ResourceField("group")]
public class GroupedField : EntityFieldValueHandler, IMainField, IDiffableField
{
}
