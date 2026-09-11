namespace ContentPatcher;

using System.Text.Json;
using ContentEditor;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher.StringFormatting;
using ReeLib;
using VYaml.Annotations;
using VYaml.Serialization;

public class PatchConfigContainer(string filepath)
{
    private readonly Dictionary<string, ClassConfig> classes = new();
    private readonly Dictionary<string, ResourceConfig> resources = new();
    private readonly Dictionary<string, EntityConfig> entities = new();
    private readonly Dictionary<string, CustomTypeConfig> customTypes = new();
    private readonly List<IEntitySetup> entitySetups = new();

    private string DefinitionFilepath { get; } = Path.Combine(filepath, "definitions");
    private string EnumFilepath { get; } = Path.Combine(filepath, "enums");

    public IReadOnlyDictionary<string, ClassConfig> Classes => classes;
    public IReadOnlyDictionary<string, EntityConfig> Entities => entities;
    public IReadOnlyDictionary<string, ResourceConfig> Resources => resources;
    public IReadOnlyDictionary<string, CustomTypeConfig> CustomTypes => customTypes;

    public bool IsLoaded { get; private set; }

    public HierarchyTypeList<EntityConfig> EntityHierarchy { get; } = new("");
    public HierarchyTypeList<ResourceConfig> ResourceHierarchy { get; } = new("");

    public ClassConfig? GetClassConfig(string classname) => classes.GetValueOrDefault(classname);
    public FieldConfig? GetClassFieldConfig(string classname, string fieldName) => classes.GetValueOrDefault(classname)?.Fields?.GetValueOrDefault(fieldName);
    public EntityConfig? GetEntityConfig(string entityType) => entities.GetValueOrDefault(entityType);

    public void Load(ContentWorkspace workspace)
    {
        if (entitySetups.Count == 0) {
            var setupTypes = typeof(IEntitySetup).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && t.IsAssignableTo(typeof(IEntitySetup)));
            entitySetups.AddRange(setupTypes.Select(t => (IEntitySetup)Activator.CreateInstance(t)!)!);
        }
        IsLoaded = true;
        classes.Clear();
        foreach (var setup in entitySetups) {
            if (setup.SupportedGames?.Length > 0 && !setup.SupportedGames.Contains(workspace.Game.name)) {
                continue;
            }

            try {
                setup.Setup(workspace);
            } catch (Exception e) {
                Logger.Error(e, $"Failed to execute setup {setup.GetType().Name}");
            }
        }
        AddDefaultConfigs();
        LoadPatchConfigs(workspace);
        LoadEnums(workspace.Env, EnumFilepath);
    }

    private void AddDefaultConfigs()
    {
        classes["via.GameObject"] = new ClassConfig() {
            StringFormatter = new StringFormatter("{Name}", FormatterSettings.DefaultFormatter)
        };
    }

    private static void LoadEnums(Workspace env, string sourceFolder)
    {
        if (!Directory.Exists(sourceFolder)) return;

        foreach (var file in Directory.EnumerateFiles(sourceFolder, "*.json")) {
            var fs = File.OpenRead(file);
            EnumConfig data;
            try {
                data = JsonSerializer.Deserialize<EnumConfig>(fs, JsonConfig.jsonOptions)!;
                if (data == null || string.IsNullOrEmpty(data.EnumName)) continue;
            } catch (Exception) {
                continue;
            } finally {
                fs.Dispose();
            }

            var desc = env.TypeCache.GetEnumDescriptor(data.EnumName);
            if (desc.IsEmpty) {
                if (data.BackingType == null) continue;

                // custom "virtual" enums
                desc = env.TypeCache.CreateEnum(data.EnumName, data.BackingType);
                if (desc == null) continue;

                desc.IsFlags = data.IsFlags;
            } else {
                if (data.IsFlags) {
                    desc.IsFlags = data.IsFlags;
                }
            }

            if (data.Values?.Count > 0) {
                foreach (var (name, val) in data.Values) {
                    desc.AddValue(name, val);
                }
            }

            desc.SetDisplayLabels(data.DisplayLabels);
        }
    }

    public void LoadPatchConfigs(ContentWorkspace workspace)
    {
        LoadConfigsFromDir(workspace, DefinitionFilepath, false);
        var globalPath = Path.Combine(Path.GetDirectoryName(filepath)!, "global/definitions");
        LoadConfigsFromDir(workspace, globalPath, true);
    }

    private void LoadConfigsFromDir(ContentWorkspace workspace, string directory, bool noWarnings)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*.yaml")) {
            var fs = File.OpenRead(file).ToMemoryStream();
            var memory = fs.GetBuffer().AsMemory(0, (int)fs.Length);
            SerializedPatchConfigRoot newDict;
            try {
                newDict = YamlSerializer.Deserialize<SerializedPatchConfigRoot>(memory, yamlOptions);
            } catch (Exception e) {
                Logger.Error(e, $"Failed to read yaml config {file}");
                continue;
            }
            if (newDict == null) continue;

            try {
                LoadYamlConfig(workspace, newDict, ref noWarnings);
            } catch (Exception e) {
                Logger.Error(e, "Failed to set up entity yaml configs from file " + file);
            }
        }
    }

    private void LoadYamlConfig(ContentWorkspace workspace, SerializedPatchConfigRoot newDict, ref bool noWarnings)
    {
        // if (newDict.Types != null) foreach (var (name, customType) in newDict.Types) {
        //     if (customType.Fields == null || customType.Fields.Count == 0) {
        //         throw new Exception($"Unsupported user-defined object config {name}. Must have at least one field");
        //     }

        //     var config = customType.ToRuntimeConfig(workspace);
        //     customTypes.Add(name, config);
        // }

        if (newDict.Resources != null) {
            foreach (var (resType, resCfg) in newDict.Resources) {
                var cfg = SetupResourceConfig(workspace, resType, resCfg);
                if (!resCfg.DisallowStandaloneEditing && resCfg.ParentResource == null) {
                    var shortname = ResourceHierarchy.Add(resType, cfg, resCfg.DisplayName);
                    resources.Add(shortname, cfg);
                    if (shortname == resType) continue;
                }
                resources.Add(resType, cfg);
            }
        }

        if (newDict.Entities != null) foreach (var (name, entity) in newDict.Entities) {
            if (entity.Fields == null || entity.Fields.Count == 0) {
                throw new Exception($"Unsupported user-defined object config {name}. Must have at least one field");
            }

            var config = SetupEntityConfig(workspace, entity, name);

            var shortname = EntityHierarchy.Add(name, config);
            entities.Add(shortname, config);
        }

        if (newDict.Classes != null) foreach (var (cls, config) in newDict.Classes) {
            var rszClass = workspace.Env.RszParser.GetRSZClass(cls);
            if (rszClass == null) {
                if (!noWarnings) {
                    Logger.Debug($"Unknown RSZ class {cls} for game {workspace.Env.Config.Game}");
                }
                continue;
            }

            if (!classes.TryGetValue(cls, out var runtimeConfig)) {
                classes[cls] = runtimeConfig = new();
            }

            // config.MergeIntoRuntimeConfig(workspace.Env, rszClass, runtimeConfig);

            if (config.To_String != null) {
                var fmt = FormatterSettings.CreateWorkspaceFormatter(workspace);
                runtimeConfig.StringFormatter = new StringFormatter(config.To_String, fmt);
            }

            // if (config.Subclasses != null) {
            //     foreach (var (subcls, sub) in config.Subclasses) {
            //         if (!configs.TryGetValue(subcls, out var subConfig)) {
            //             configs[subcls] = subConfig = new();
            //         }
            //         runtimeConfig.MergeIntoSubclass(subcls, subConfig);
            //         subConfig.StringFormatter ??= runtimeConfig.StringFormatter;
            //     }
            // }
        }
    }

    private ResourceConfig SetupResourceConfig(ContentWorkspace workspace, string resType, EntityResourceConfigSerialized resCfg, bool addResourceHandler = true)
    {
        var cfg = new ResourceConfig(resType) {
            CustomIDRange = resCfg.CustomIDRange,
            DisplayName = resCfg.DisplayName ?? resType.GetStringAfterLastDelimiter('.').ToString(),
        };
        if (resCfg.ParentResource != null) {
            if (resources.TryGetValue(resCfg.ParentResource, out var parent)) {
                cfg.ParentResource = parent;
                cfg.CustomIDRange = parent.CustomIDRange;
                parent.SubResources.Add(cfg);
            } else {
                Logger.Warn($"Resource {resType} parent resource {resCfg.ParentResource} was not found. Make sure the parent resource gets declared before sub resources");
            }
        }

        if (resCfg.Subclasses?.Count > 0) {
            cfg.Subtypes ??= new ();
            foreach (var (subType, subConfig) in resCfg.Subclasses) {
                if (subConfig.CustomIDRange != null) {
                    Logger.Warn($"Resource subtype {resType}->{subType} has a custom ID range defined. ID ranges are only allowed on root resources. Will be ignored.");
                }
                // all subtypes must inherit id range from base resource type
                subConfig.CustomIDRange = resCfg.CustomIDRange;
                // inherit ID settings only if there are no more specific ones
                subConfig.ID ??= resCfg.ID;
                subConfig.SubID ??= resCfg.SubID;

                var sub = SetupResourceConfig(workspace, subType, subConfig, addResourceHandler);
                cfg.Subtypes[subType] = sub;
            }
        }

        if (!string.IsNullOrEmpty(resCfg.Classname)) {
            cfg.RszClass = workspace.Env.RszParser.GetRSZClass(resCfg.Classname);
        } else {
            cfg.RszClass = workspace.Env.RszParser.GetRSZClass(resType);
        }

        if (resCfg.ID != null || resCfg.SubID != null) {
            if (cfg.RszClass == null) {
                Logger.Warn($"Required classname is missing for resource ID settings in type {resType}");
            } else {
                var ids = resCfg.ID?.Select(ff => NestableFieldAccessor.CreateForClass(workspace.Env.RszParser, cfg.RszClass, ff)).ToArray();
                var subids = resCfg.SubID?.Select(ff => NestableFieldAccessor.CreateForClass(workspace.Env.RszParser, cfg.RszClass, ff)).ToArray();
                if (ids?.Length > 0) {
                    cfg.IDGenerator = IDGenerator.DefineGenerator(cfg.RszClass, ids);
                }
                if (subids?.Length > 0) {
                    cfg.SubIDGenerator = IDGenerator.DefineGenerator(cfg.RszClass, subids);
                }
            }
        }
        if (addResourceHandler) {
            cfg.Resource = ResourceHandler.CreateInstance(cfg, resCfg, workspace);
        }
        return cfg;
    }


    private EntityConfig SetupEntityConfig(ContentWorkspace workspace, EntityConfigSerialized entity, string fullname)
    {
        var fieldlist = new List<EntityField>();
        var displaylist = new List<EntityField>();
        foreach (var data in entity.Fields) {
            var newfield = new EntityField() {
                name = data.name,
                config = data,
                label = data.label ?? data.name.PrettyPrint(),
            };
            fieldlist.Add(newfield);
            displaylist.Add(newfield);
        }

        foreach (var data in entity.Fields) {
            var curIndex = fieldlist.FindIndex(f => f.name == data.name);
            var field = fieldlist[curIndex];
            if (data.displayAfter != null && data.displayAfter != data.name) {
                if (data.displayAfter == "start") {
                    displaylist.RemoveAt(curIndex);
                    displaylist.Insert(0, field);
                } else {
                    var otherIndex = displaylist.FindIndex(dl => dl.name == data.displayAfter);
                    if (otherIndex != -1) {
                        displaylist.RemoveAt(curIndex);
                        otherIndex = displaylist.FindIndex(dl => dl.name == data.displayAfter);
                        if (otherIndex == displaylist.Count - 1) {
                            displaylist.Add(field);
                        } else {
                            displaylist.Insert(otherIndex + 1, field);
                        }
                    }
                }
            }
        }

        var config = new EntityConfig(fullname) {
            Fields = fieldlist.ToArray(),
            DisplayFieldsOrder = displaylist.ToArray(),
            PrimaryEnum = entity.Enums?.FirstOrDefault(e => e.primary),
            // Enums = Enums?.Where(e => !e.primary).ToArray(),
            Enums = entity.Enums?.ToArray(),
        };
        if (entity.To_String != null) {
            config.StringFormatter = new StringFormatter(entity.To_String, FormatterSettings.CreateFullEntityFormatter(config, workspace));
        }
        if (config.Enums != null) {
            foreach (var ee in config.Enums) {
                ee.Init(workspace, config);
            }
        }

        foreach (var field in config.Fields) {
            SetupEntityField(workspace, field, config);
            // if (field.resource != null) {
            //     var res = SetupResourceConfig(workspace, field.name, field.resource);
            // }
            // field.EntitySetup(config, workspace);
            // resources.TryAdd(field.Resource.Patcher!.Config.Type, field.Resource);
        }

        // setup field references in separate step so we've initialized all the basic data first
        foreach (var field in config.Fields) {
            field.ValueHandler.EntitySetup(config, workspace);
        }
        foreach (var field in config.Fields) {
            if (field.config.fieldId != null) {
                field.IdField = NestableFieldAccessor.CreateForEntity(workspace, config, field.config.fieldId);
            }
        }
        config.PrimaryField = config.Fields.FirstOrDefault(f => f.name == entity.PrimaryField)!;
        if (config.PrimaryField == null) {
            config.PrimaryField = config.Fields[0];
        }
        config.IDField = config.Fields.FirstOrDefault(f => f.name == entity.IDField)!;
        config.IDField ??= config.PrimaryField;
        return config;
    }

    internal EntityField SetupEntityField(ContentWorkspace workspace, EntityField field, EntityConfig entity)
    {
        var data = field.config;
        if (!string.IsNullOrEmpty(data.resource?.Type)) {
            field.Config = SetupResourceConfig(workspace, entity.Name + "__" + field.name, data.resource);
            field.config.type ??= field.Config.Type;
            resources.TryAdd(field.Config.Type, field.Config);
        } else if (!string.IsNullOrEmpty(data.type) && resources.TryGetValue(data.type, out var globalResource)) {
            field.Config = globalResource;
        } else if (!string.IsNullOrEmpty(data.fieldType)) {
            // handle fields with no resources (entity-only fields)
            field.ValueHandler = ResourceHandler.CreateValueHandler(data.fieldType, workspace);
            var resCfg = new EntityResourceConfigSerialized() { Type = data.fieldType };
            field.config.type ??= data.fieldType;
            field.Config = SetupResourceConfig(workspace, entity.Name + "__" + field.name, resCfg, false);
            field.Config.Resource = (field.ValueHandler as CustomEntityFieldHandler)?.CreateResourceHandler(field.Config)
                ?? throw new Exception($"Field {data.name} declared with field type {data.fieldType} but no resource provided.");
        } else {
            throw new Exception($"Missing resource or type for field {field.name} of {entity}");
        }
        if (field.Config.Resource == null) {
            throw new Exception($"Missing patcher for resource {field.Config}");
        }

        if (field.ValueHandler == null && !string.IsNullOrEmpty(data.fieldType)) {
            field.ValueHandler = ResourceHandler.CreateValueHandler(data.fieldType, workspace);
        }
        field.ValueHandler ??= field.Config.Resource.CreateValueHandler(field);
        field.ValueHandler.Field = field;
        field.ValueHandler.LoadParams(data);
        if (data.condition != null) {
            var conditionField = data.condition.field ?? field.name;
            if (data.condition.property == "classname") {
                field.Condition = new (conditionField, new WhenClassnameCondition(data.condition.property, data.condition.equals as string ?? ""));
            } else {
                field.Condition = new (conditionField, new WhenFieldValueCondition(data.condition.property, data.condition.equals));
            }
        }
        field.IsRequired = data.isRequired;
        field.IsNotStandaloneValue = data.IsNotStandalone;
        return field;
    }

    private static readonly YamlSerializerOptions yamlOptions = new YamlSerializerOptions {
        NamingConvention = NamingConvention.LowerCamelCase,
    };
}
