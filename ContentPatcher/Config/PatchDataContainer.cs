namespace ContentPatcher;

using System.Text.Json.Nodes;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib;
using SmartFormat;
using SmartFormat.Extensions;
using VYaml.Annotations;
using VYaml.Serialization;

public class PatchDataContainer(string filepath)
{
    private readonly Dictionary<string, ClassConfig> configs = new();
    private readonly Dictionary<string, EntityConfig> entities = new();
    private readonly Dictionary<string, CustomTypeConfig> customTypes = new();

    public IEnumerable<KeyValuePair<string, ClassConfig>> Classes => configs;
    public IEnumerable<KeyValuePair<string, EntityConfig>> Entities => entities;
    public IEnumerable<KeyValuePair<string, CustomTypeConfig>> CustomTypes => customTypes;

    public EntityTypeList EntityHierarchy { get; } = new("");

    public ClassConfig? Get(string classname) => configs.GetValueOrDefault(classname);
    public FieldConfig? Get(string classname, string fieldName) => configs.GetValueOrDefault(classname)?.Fields?.GetValueOrDefault(fieldName);

    private static readonly Dictionary<string, Func<JsonObject, ResourceHandler>> patchers = new();
    public static void RegisterResourcePatcher(string type, Func<JsonObject, ResourceHandler> factory)
    {
        patchers[type] = factory;
    }

    public void LoadPatchConfigs(RszParser parser)
    {
        configs.Clear();
        if (!Directory.Exists(filepath)) return;
        foreach (var file in Directory.EnumerateFiles(filepath, "*.yaml")) {
            var fs = File.OpenRead(file).ToMemoryStream();
            var memory = fs.GetBuffer().AsMemory(0, (int)fs.Length);
            var newDict = YamlSerializer.Deserialize<SerializedPatchConfigRoot>(memory, yamlOptions);
            if (newDict == null) continue;

            if (newDict.Types != null) foreach (var (name, customType) in newDict.Types) {
                if (customType.Fields == null || customType.Fields.Count == 0) {
                    throw new Exception($"Unsupported user-defined object config {name}. Must have at least one field");
                }

                var config = customType.ToRuntimeConfig();
                customTypes.Add(name, config);
            }

            if (newDict.Entities != null) foreach (var (name, entity) in newDict.Entities) {
                if (entity.Fields == null || entity.Fields.Count == 0) {
                    throw new Exception($"Unsupported user-defined object config {name}. Must have at least one field");
                }

                var config = entity.ToRuntimeConfig();
                var shortname = EntityHierarchy.Add(name, config);
                entities.Add(shortname, config);
            }

            if (newDict.Classes != null) foreach (var (cls, config) in newDict.Classes) {
                var rszClass = parser.GetRSZClass(cls);
                if (rszClass == null) {
                    throw new Exception($"Unknown RSZ class {cls}");
                }

                if (!configs.TryGetValue(cls, out var runtimeConfig)) {
                    configs[cls] = runtimeConfig = new();
                }

                config.MergeIntoRuntimeConfig(rszClass, runtimeConfig);

                if (config.To_String != null) {
                    var fmt = new SmartFormatter(FormatterSettings.DefaultSettings);
                    fmt.AddExtensions(new RszFieldStringFormatterSource(), new RszFieldArrayStringFormatterSource());
                    fmt.AddExtensions(new DefaultFormatter());
                    runtimeConfig.StringFormatter = new StringFormatter(config.To_String, fmt);
                }

                if (config.Subclasses != null) {
                    foreach (var (subcls, sub) in config.Subclasses) {
                        if (!configs.TryGetValue(subcls, out var subConfig)) {
                            configs[subcls] = subConfig = new();
                        }
                        runtimeConfig.MergeIntoSubclass(subcls, subConfig);
                        subConfig.StringFormatter ??= runtimeConfig.StringFormatter;
                    }
                }
            }
        }
    }

    private static readonly YamlSerializerOptions yamlOptions = new YamlSerializerOptions {
        NamingConvention = NamingConvention.LowerCamelCase,
    };
}
