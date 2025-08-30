using System.Reflection;
using System.Runtime.CompilerServices;
using ContentEditor;
using ContentPatcher;
using ReeLib;

namespace ContentPatcher;

public abstract class RszFieldAccessorBase(string name)
{
    protected readonly Dictionary<RszClass, int> resolvedFields = new();
    protected bool hasLoggedError;

    public TypeCacheOverride? Override { get; set; }

    public abstract int GetIndex(RszClass instanceClass);

    protected int Fail(RszClass instanceClass)
    {
        if (!hasLoggedError) {
            hasLoggedError = true;
            Logger.Error($"Failed to resolve field {name} for class {instanceClass}");
        }

        return -1;
    }

    public override string ToString() => name;
}
public abstract class RszFieldAccessorBase<T>(string name) : RszFieldAccessorBase(name)
{
    public T Get(RszInstance instance)
    {
        var index = GetIndex(instance.RszClass);
        return index == -1 ? default! : (T)instance.Values[index];
    }

    public void Set(RszInstance instance, T value)
    {
        var index = GetIndex(instance.RszClass);
        if (index != -1) {
            instance.Values[index] = value!;
        }
    }
}

public sealed class TypeCacheOverride
{
    public RszFieldType? fieldType;
    public string? originalType;
}

public sealed class RszFieldAccessorFieldList<T>(Func<IEnumerable<(RszField field, int index)>, int> condition, [CallerMemberName] string name = "") : RszFieldAccessorBase<T>(name)
{
    public sealed override int GetIndex(RszClass instanceClass)
    {
        if (resolvedFields.TryGetValue(instanceClass, out var index)) {
            return index;
        }

        index = condition.Invoke(instanceClass.fields.Select((f, i) => (f, i)));
        resolvedFields[instanceClass] = index;
        if (index != -1) {
            return index;
        }

        return Fail(instanceClass);
    }
}

public sealed class RszFieldAccessorFirst<T>(Func<RszField, bool> condition, [CallerMemberName] string name = "") : RszFieldAccessorBase<T>(name)
{
    public sealed override int GetIndex(RszClass instanceClass)
    {
        if (resolvedFields.TryGetValue(instanceClass, out var index)) {
            return index;
        }

        for (int i = 0; i < instanceClass.fields.Length; ++i) {
            if (condition.Invoke(instanceClass.fields[i])) {
                return resolvedFields[instanceClass] = i;
            }
        }

        resolvedFields[instanceClass] = -1;
        return Fail(instanceClass);
    }
}

public sealed class RszFieldAccessorLast<T>(Func<RszField, bool> condition, [CallerMemberName] string name = "") : RszFieldAccessorBase<T>(name)
{
    public sealed override int GetIndex(RszClass instanceClass)
    {
        if (resolvedFields.TryGetValue(instanceClass, out var index)) {
            return index;
        }

        for (int i = instanceClass.fields.Length - 1; i >= 0; --i) {
            if (condition.Invoke(instanceClass.fields[i])) {
                return resolvedFields[instanceClass] = i;
            }
        }

        resolvedFields[instanceClass] = -1;
        return Fail(instanceClass);
    }
}

public sealed class RszFieldAccessorFirstFallbacks<T>(Func<RszField, bool>[] conditions, [CallerMemberName] string name = "") : RszFieldAccessorBase<T>(name)
{
    public sealed override int GetIndex(RszClass instanceClass)
    {
        if (resolvedFields.TryGetValue(instanceClass, out var index)) {
            return index;
        }

        foreach (var condition in conditions) {
            for (int i = 0; i < instanceClass.fields.Length; ++i) {
                if (condition.Invoke(instanceClass.fields[i])) {
                    return resolvedFields[instanceClass] = i;
                }
            }
        }

        resolvedFields[instanceClass] = -1;
        return Fail(instanceClass);
    }
}

public sealed class RszFieldAccessorLastFallbacks<T>(Func<RszField, bool>[] conditions, [CallerMemberName] string name = "") : RszFieldAccessorBase<T>(name)
{
    public sealed override int GetIndex(RszClass instanceClass)
    {
        if (resolvedFields.TryGetValue(instanceClass, out var index)) {
            return index;
        }

        foreach (var condition in conditions) {
            for (int i = instanceClass.fields.Length - 1; i >= 0; --i) {
                if (condition.Invoke(instanceClass.fields[i])) {
                    return resolvedFields[instanceClass] = i;
                }
            }
        }

        resolvedFields[instanceClass] = -1;
        return Fail(instanceClass);
    }
}


/// <summary>
/// Container for game-agonstic RSZ field lookup conditions.
/// </summary>
public static partial class RszFieldCache
{
    public static TAcc Type<TAcc>(this TAcc accessor, RszFieldType type)
        where TAcc : RszFieldAccessorBase
    {
        accessor.Override ??= new();
        accessor.Override.fieldType = type;
        return accessor;
    }

    public static TAcc Object<TAcc>(this TAcc accessor, string originalType)
        where TAcc : RszFieldAccessorBase
    {
        accessor.Override ??= new();
        accessor.Override.originalType = originalType;
        accessor.Override.fieldType = RszFieldType.Object;
        return accessor;
    }

    public static TAcc Resource<TAcc>(this TAcc accessor, string resourceHolderType)
        where TAcc : RszFieldAccessorBase
    {
        accessor.Override ??= new();
        accessor.Override.originalType = resourceHolderType;
        accessor.Override.fieldType = RszFieldType.Resource;
        return accessor;
    }

    private static RszFieldAccessorFirst<T> First<T>(Func<RszField, bool> condition, [CallerMemberName] string name = "")
        => new RszFieldAccessorFirst<T>(condition, name);

    private static RszFieldAccessorFirstFallbacks<T> First<T>(Func<RszField, bool>[] conditions, [CallerMemberName] string name = "")
        => new RszFieldAccessorFirstFallbacks<T>(conditions, name);

    private static RszFieldAccessorLast<T> Last<T>(Func<RszField, bool> condition, [CallerMemberName] string name = "")
        => new RszFieldAccessorLast<T>(condition, name);

    private static RszFieldAccessorLastFallbacks<T> Last<T>(Func<RszField, bool>[] conditions, [CallerMemberName] string name = "")
        => new RszFieldAccessorLastFallbacks<T>(conditions, name);

    private static RszFieldAccessorFieldList<T> FromList<T>(Func<IEnumerable<(RszField field, int index)>, int> conditions, [CallerMemberName] string name = "")
        => new RszFieldAccessorFieldList<T>(conditions, name);

    public static void InitializeFieldOverrides(RszParser parser)
    {
        InitializeFieldOverrides(parser, typeof(RszFieldCache));
    }

    private static void InitializeFieldOverrides(RszParser parser, Type type)
    {
        var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var classTargets = type.GetCustomAttributes<RszAccessorAttribute>();
        foreach (var field in fields) {
            if (!field.FieldType.IsAssignableTo(typeof(RszFieldAccessorBase))) continue;

            var targets = classTargets.Concat(field.GetCustomAttributes<RszAccessorAttribute>());
            if (!targets.Any()) continue;

            var accessor = (RszFieldAccessorBase)field.GetValue(null)!;
            if (accessor.Override == null) continue;

            foreach (var attr in targets) {
                var cls = parser.GetRSZClass(attr.Classname);
                if (cls == null) continue;

                var index = accessor.GetIndex(cls);
                if (index == -1) continue;

                var rszField = cls.fields[index];
                if (accessor.Override.fieldType != null) {
                    rszField.type = accessor.Override.fieldType.Value;
                }

                if (accessor.Override.originalType != null) {
                    rszField.original_type = accessor.Override.originalType;
                }
            }
        }

        var nested = type.GetNestedTypes(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var sub in nested) {
            InitializeFieldOverrides(parser, sub);
        }
    }
}
