using System.Numerics;
using System.Text.RegularExpressions;
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
            config.StringFormatter = new StringFormatter(To_String, FormatterSettings.CreateFullEntityFormatter(config, workspace));
        }
        if (config.Enums != null) {
            foreach (var ee in config.Enums) {
                ee.Init(workspace, config);
            }
        }

        foreach (var field in config.Fields) {
            field.EntitySetup(config, workspace);
        }

        return config;
    }
}

[YamlObject]
public partial class EntityEnumInfo
{
    public string name = string.Empty;
    public string? format;
    public bool primary;

    [GeneratedRegex("[^0-9a-zA-Z_]")]
    private static partial Regex NonAlphanumericRegex();

    [YamlIgnore]
    private StringFormatter? formatter;

    internal void Init(ContentWorkspace workspace, EntityConfig config)
    {
        formatter = format == null ? null : new StringFormatter(format, FormatterSettings.CreateFullEntityFormatter(config, workspace));
    }

    public void UpdateEnum<T>(ContentWorkspace workspace, T value, string label) where T : IBinaryInteger<T>
    {
        var desc = workspace.Env.TypeCache.GetEnumDescriptor(name);
        desc.AddValue(value, label);
    }

    public void UpdateEnum(ContentWorkspace workspace, ResourceEntity entity)
    {
        // NOTE: we don't currently have a way of resetting custom enum entries. Probably not worth the effort to fix
        // May cause issues if the user swaps bundles or if we ever support changing IDs in runtime.

        var desc = workspace.Env.TypeCache.GetEnumDescriptor(name);
        desc.AddValue(entity.Id, formatter?.GetString(entity) ?? NonAlphanumericRegex().Replace(entity.Label, ""), entity.Label);
    }
}