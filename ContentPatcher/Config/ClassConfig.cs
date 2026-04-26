namespace ContentPatcher;

using ContentEditor.Editor;
using ReeLib;
using VYaml.Annotations;

public abstract class NestableFieldAccessor
{
    public abstract RszField Field { get; }
    public abstract object? Get(object instance);
    public abstract void Set(object instance, object value);

    public class PlainReturn : NestableFieldAccessor
    {
        public static readonly PlainReturn Instance = new();
        public override RszField Field => _FakeField;
        private static readonly RszField _FakeField = new RszField() { name = "Placeholder", type = RszFieldType.S32 };

        public override object? Get(object instance) => instance;

        public override void Set(object instance, object value) { }
    }

    public class SimpleField(RszClass ownerClass, string fieldName) : NestableFieldAccessor
    {
        public int Index { get; } = ownerClass.IndexOfField(fieldName);

        public override RszField Field => ownerClass.fields[Index];

        public override object? Get(object instance) => ((RszInstance)instance).Values[Index];
        public override void Set(object instance, object? value) => ((RszInstance)instance).Values[Index] = value!;
    }

    public class NestedField(RszClass idOwnerClass, string path) : NestableFieldAccessor
    {
        public RszField LastPathField { get; } = FindLastPathField(idOwnerClass, path);

        public override RszField Field => LastPathField;

        public override object? Get(object instance) => ((RszInstance)instance).GetNestedFieldValue(path);

        public override void Set(object instance, object value) => ((RszInstance)instance).SetNestedFieldValue(path, value);

        private static RszField FindLastPathField(RszClass cls, string path)
        {
            var i = path.LastIndexOf('.');
            if (i == -1) return cls.GetField(path)!;

            return cls.GetField(path.Substring(i + 1))!;
        }
    }
}

public class ClassConfig
{
    public NestableFieldAccessor[]? IDFields { get; set; }
    public NestableFieldAccessor[]? SubIDFields { get; set; }
    public string? Group { get; set; }
    public string? Type { get; set; }
    public Dictionary<string, FieldConfig>? Fields { get; set; }
    public Dictionary<string, SubclassConfig>? Subclasses { get; set; }
    public ResourceHandler? Patcher { get; set; }
    public StringFormatter? StringFormatter { get; set; }

    public long GetID(RszInstance instance)
    {
        return 0;
    }

    public void MergeIntoSubclass(string subclass, ClassConfig subConfig)
    {
        subConfig.IDFields = subConfig.IDFields ?? IDFields;
        subConfig.SubIDFields = subConfig.SubIDFields ?? SubIDFields;
        subConfig.Group = subConfig.Group ?? Group;
        subConfig.Type = subConfig.Type ?? Type;
        subConfig.Fields = subConfig.Fields ?? Fields;
        subConfig.Patcher = subConfig.Patcher ?? Subclasses?.GetValueOrDefault(subclass)?.Patcher;
    }

    public override string ToString() => $"{Group}/{Type}";
}

[YamlObject]
public partial class SerializedPatchConfigRoot
{
    public Dictionary<string, EntityConfigSerialized>? Entities { get; set; }
    public Dictionary<string, CustomTypeConfigSerialized>? Types { get; set; }
    public Dictionary<string, ClassConfigSerialized>? Classes { get; set; }
}

[YamlObject]
public partial class ClassConfigSerialized
{
    [YamlMember("id")]
    public object[]? ID { get; set; }

    [YamlMember("subId")]
    public string[]? SubID { get; set; }

    public string? Group { get; set; }
    public string? Type { get; set; }
    public Dictionary<string, FieldConfig>? Fields { get; set; }
    public Dictionary<string, SubclassConfigSerialized>? Subclasses { get; set; }
    public Dictionary<string, object>? Patcher { get; set; }
    [YamlMember("to_string")]
    public string? To_String { get; set; }

    public void MergeIntoRuntimeConfig(Workspace env, RszClass? cls, ClassConfig target)
    {
        if (cls == null && (ID?.Length > 0 || SubID?.Length > 0)) {
            throw new ArgumentNullException(nameof(cls), "RSZ class is required when ID or SubID are present");
        }
        target.Type = Type ?? target.Type;
        target.Group = Group ?? target.Group;
        target.Patcher = ResourceHandler.CreateInstance(cls!.name, Patcher) ?? target.Patcher;
        target.IDFields = ID?.Select(ff => CreateFieldGetter(env.RszParser, cls, ff)).ToArray() ?? target.IDFields;
        target.SubIDFields = SubID?.Select(ff => CreateFieldGetter(env.RszParser, cls, ff)).ToArray() ?? target.SubIDFields;

        if (Fields != null) {
            target.Fields ??= new();
            foreach (var field in Fields) {
                if (target.Fields.ContainsKey(field.Key)) continue;

                target.Fields.Add(field.Key, field.Value);
            }
        }

        if (Subclasses != null) {
            target.Subclasses ??= new();
            foreach (var (subclass, subdata) in Subclasses) {
                if (target.Subclasses.ContainsKey(subclass)) continue;

                target.Subclasses.Add(subclass, subdata.ToRuntimeConfig(subclass));
            }
        }
    }

    private static NestableFieldAccessor CreateFieldGetter(RszParser parser, RszClass cls, object obj)
    {
        if (obj is string str) {
            return new NestableFieldAccessor.SimpleField(cls, str);
        }

        if (obj is Dictionary<object, object> dict) {
            var path = (string)dict["path"];
            var innerClass = (string)dict["class"];
            var innerCls = parser.GetRSZClass(innerClass)
                ?? throw new Exception("Unknown field inner class " + innerClass);
            return new NestableFieldAccessor.NestedField(innerCls, path);
        }

        throw new NotSupportedException("Unsupported field ID type " + obj.GetType().FullName + ": " + obj);
    }
}

[YamlObject]
public partial class FieldConfig
{
    public string? Enum { get; set; }
    public string? Type;
    public string? ResourceType;
    public string? Tooltip;
    public string? Label;
    public string? TranslateGuid;
    public string? TranslateFallbackEnum;
    public string? Handler;
    public bool ReadOnly;
    public Dictionary<string, string?>? OneOf { get; set; }
}

[YamlObject]
public partial class SubclassConfig
{
    public Dictionary<string, FieldConfig>? Fields { get; set; }
    public ResourceHandler? Patcher { get; set; }
}

[YamlObject]
public partial class SubclassConfigSerialized
{
    public Dictionary<string, FieldConfig>? Fields { get; set; }
    public Dictionary<string, object>? Patcher { get; set; }

    public SubclassConfig ToRuntimeConfig(string resourceKey)
    {
        return new SubclassConfig() {
            Patcher = ResourceHandler.CreateInstance(resourceKey, Patcher),
            Fields = Fields,
        };
    }
}
