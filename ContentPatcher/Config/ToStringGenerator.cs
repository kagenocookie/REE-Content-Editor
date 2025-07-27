using VYaml.Annotations;

namespace ContentPatcher;

public interface ToStringGenerator
{
    string? GetString(CustomTypeConfig target);
}

[YamlObject]
public partial class ToStringHandler
{
    public string? type;
}
