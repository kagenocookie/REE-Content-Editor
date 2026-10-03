using System.Reflection;
using VYaml.Annotations;

namespace ContentPatcher;

public class CustomTypeConfig
{
    public required EntityField[] Fields { get; init; }
    public required EntityField[] DisplayFieldsOrder { get; init; }
}

[YamlObject]
public partial class CustomTypeConfigSerialized
{
}
