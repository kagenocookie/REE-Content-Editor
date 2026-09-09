using System.Numerics;
using System.Text.RegularExpressions;
using ContentEditor;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using VYaml.Annotations;

namespace ContentPatcher;

public class EntityConfig(string name)
{
    public string Name { get; internal set; } = name;
    public EntityField PrimaryField { get; set; } = null!;
    public EntityField IDField { get; set; } = null!;
    public EntityField[] Fields { get; set; } = [];
    public EntityField[] DisplayFieldsOrder { get; set; } = [];
    public EntityEnumInfo? PrimaryEnum { get; init; }
    public EntityEnumInfo[]? Enums { get; init; }
    public StringFormatter? StringFormatter { get; set; }

    public bool HasField(string name) => GetField(name) != null;
    public EntityField? GetField(string name) => Fields.FirstOrDefault(f => f.name == name);

    public override string ToString() => Name;
}
