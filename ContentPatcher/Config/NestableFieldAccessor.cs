namespace ContentPatcher;

using System.Text.Json;
using ContentEditor;
using ReeLib;

public abstract class NestableFieldAccessor
{
    public abstract RszField Field { get; }
    public abstract object? Get(object instance);
    public abstract void Set(object instance, object value);

    public class PlainReturn(RszField? _field = null) : NestableFieldAccessor
    {
        public static readonly PlainReturn Instance = new();
        public override RszField Field => _field ?? _FakeField;
        private static readonly RszField _FakeField = new RszField() { type = RszFieldType.S32 };

        public override object? Get(object instance) => instance;

        public override void Set(object instance, object value) { }
    }

    public class SimpleField(RszClass ownerClass, int fieldIndex) : NestableFieldAccessor
    {
        public SimpleField(RszClass ownerClass, string fieldName) : this(ownerClass, ownerClass.IndexOfField(fieldName))
        {
        }

        public int Index { get; } = fieldIndex;

        public override RszField Field => ownerClass.fields[Index];

        public override object? Get(object instance) => ((RszInstance)instance).Values[Index];
        public override void Set(object instance, object? value) => ((RszInstance)instance).Values[Index] = value!;
    }

    public class Custom<T>(RszField _field, Func<T, object> getter, Action<T, object?> setter) : NestableFieldAccessor
    {
        public Custom(RszFieldType fieldType, Func<T, object> getter, Action<T, object?> setter)
            : this(new RszField() { type = fieldType }, getter, setter)
        {
        }

        public override RszField Field => _field;

        public override object? Get(object instance) => getter.Invoke((T)instance);
        public override void Set(object instance, object? value) => setter((T)instance, value);
    }

    public class EntitySimpleField(string entityField, NestableFieldAccessor accessor) : NestableFieldAccessor
    {
        public override RszField Field => accessor.Field;

        public override object? Get(object instance)
        {
            var field = ((ResourceEntity)instance).Get(entityField);
            if (field == null) {
                Logger.Error($"Could not get unknown field {entityField} for entity {instance}");
                return null;
            }

            if (field is RSZObjectResource rszi) {
                return accessor.Get(rszi);
            } else {
                return accessor.Get(field);
            }
        }

        public override void Set(object instance, object? value)
        {
            var field = ((ResourceEntity)instance).Get(entityField);
            if (field == null) {
                Logger.Error($"Could not set unknown field {entityField} for entity {instance}");
                return;
            }
            if (field is RSZObjectResource rszo) {
                var rszInstance = (field as RSZObjectResource)?.Instance;
                if (rszInstance == null) return;

                accessor.Set(rszInstance, value!);
            } else {
                accessor.Set(field, value!);
            }
        }
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

    public static NestableFieldAccessor CreateForClass(RszParser parser, RszClass? cls, object obj)
    {
        // TODO: need cleanup of this method
        if (obj is string str) {
            if (cls == null) {
                throw new Exception("Missing required class for pure string accessor");
            }
            return new NestableFieldAccessor.SimpleField(cls, str);
        }

        if (obj is Dictionary<object, object> dict) {
            var path = (string)dict["path"];
            if (dict.TryGetValue("class", out var innerClassRaw) && innerClassRaw is string innerClass) {
                var innerCls = parser.GetRSZClass(innerClass)
                    ?? throw new Exception("Unknown field inner class " + innerClass);

                var accessor = new NestableFieldAccessor.NestedField(innerCls, path);
                var entityField = dict.GetValueOrDefault("field") as string;
                if (!string.IsNullOrEmpty(entityField)) {
                    return new NestableFieldAccessor.EntitySimpleField(entityField, accessor);
                }

                return accessor;
            } else if (dict.TryGetValue("field", out var fieldRaw) && fieldRaw is string field) {

            }

            throw new NotSupportedException("Unsupported field " + path + " with config " + JsonSerializer.Serialize(dict));
        }

        throw new NotSupportedException("Unsupported field ID type " + obj.GetType().FullName + ": " + obj);
    }

    public static NestableFieldAccessor CreateForEntity(ContentWorkspace workspace, EntityConfig entity, Dictionary<object, object> dict)
     => CreateForEntity(workspace, entity, dict.ToDictionary(k => k.Key.ToString()!, k => k.Value));
    public static NestableFieldAccessor CreateForEntity(ContentWorkspace workspace, EntityConfig entity, Dictionary<string, object> dict)
    {
        if (!dict.TryGetValue("field", out var fieldRaw) || fieldRaw is not string fieldName) {
            throw new Exception("We need a field for this type of field path in entity " + entity);
        }

        var field = entity.GetField(fieldName);
        if (field == null) {
            throw new Exception($"Unknown field {fieldName} for entity {entity}");
        }

        if (dict.TryGetValue("path", out var pathRaw) && pathRaw is string path) {
            if (field.ValueHandler is IResourceValueContainer rvc) {
                var acc = rvc.GetAccessor(workspace, path);
                if (acc != null) {
                    return new NestableFieldAccessor.EntitySimpleField(field.name, acc);
                }
            }

            throw new NotSupportedException("Unsupported field " + path + " with config " + JsonSerializer.Serialize(dict));
        }

        throw new NotSupportedException("Unsupported field accessor combination " + fieldName + " for entity " + entity);
    }
}
