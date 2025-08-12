using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using SmartFormat;
using SmartFormat.Extensions;
using VYaml.Annotations;

namespace ContentPatcher;

public class EntityConfig
{
    public required CustomField[] Fields { get; init; }
    public required CustomField[] DisplayFieldsOrder { get; init; }
    public long[]? CustomIDRange { get; init; }
    public EntityEnumInfo? PrimaryEnum { get; init; }
    public EntityEnumInfo[]? Enums { get; init; }
    public StringFormatter? StringFormatter { get; set; }

    public bool HasField(string name) => GetField(name) != null;
    public CustomField? GetField(string name) => Fields.FirstOrDefault(f => f.name == name);
}

[YamlObject]
public partial class EntityConfigSerialized
{
    public Dictionary<string, CustomFieldSerialized> Fields = null!;
    [YamlMember("to_string")]
    public string? To_String { get; set; }
    [YamlMember("custom_id_range")]
    public long[]? CustomIDRange { get; set; }
    public EntityEnumInfo[]? Enums { get; set; }

    public EntityConfig ToRuntimeConfig(ContentWorkspace workspace)
    {
        var fieldlist = new List<CustomField>();
        var displaylist = new List<CustomField>();
        foreach (var (name, data) in Fields) {
            var newfield = CustomTypeConfigSerialized.CreateField(name, data);
            fieldlist.Add(newfield);
            if (data.displayAfter != null) {
                displaylist.Insert(displaylist.FindIndex(dl => dl.name == data.displayAfter) + 1, newfield);
            } else {
                displaylist.Add(newfield);
            }
        }

        var config = new EntityConfig() {
            Fields = fieldlist.ToArray(),
            DisplayFieldsOrder = displaylist.ToArray(),
            CustomIDRange = CustomIDRange,
            PrimaryEnum = Enums?.FirstOrDefault(e => e.primary),
            // Enums = Enums?.Where(e => !e.primary).ToArray(),
            Enums = Enums?.ToArray(),
        };
        if (To_String != null) {
            var fmt = new SmartFormatter(FormatterSettings.DefaultSettings);
            fmt.AddExtensions(new EntityStringFormatterSource(config), new RszFieldStringFormatterSource(), new RszFieldArrayStringFormatterSource());
            fmt.AddExtensions(new DefaultFormatter());
            fmt.AddExtensions(new TranslateGuidformatter(workspace.Messages));
            fmt.AddExtensions(new EnumLabelFormatter(workspace.Env));
            config.StringFormatter = new StringFormatter(To_String, fmt);
        }

        return config;
    }
}

[YamlObject]
public partial class EntityEnumInfo
{
    public string name = string.Empty;
    public string format = string.Empty;
    public bool primary;
}