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

    public bool Optional { get; set; }

    public bool Exists(RszClass cls) => GetIndex(cls) != -1;

    public TypeCacheOverride? Override { get; set; }
    public string Name { get; } = name;

    public abstract int GetIndex(RszClass instanceClass);
    public RszField? GetField(RszClass instanceClass)
    {
        var index = GetIndex(instanceClass);
        return index == -1 ? null : instanceClass.fields[index];
    }

    protected int Fail(RszClass instanceClass)
    {
        if (!hasLoggedError) {
            hasLoggedError = true;
            if (!Optional) {
                Logger.Error($"Failed to resolve field {Name} for class {instanceClass}");
            }
        }

        return -1;
    }

    public override string ToString() => Name;
}
public abstract class RszFieldAccessorBase<T>(string name) : RszFieldAccessorBase(name)
{
    public T Get(RszInstance instance)
    {
        var index = GetIndex(instance.RszClass);
        return index == -1 ? default! : (T)instance.Values[index];
    }

    public T GetOrDefault(RszInstance instance, T defaultValue)
    {
        var index = GetIndex(instance.RszClass);
        return index == -1 ? defaultValue : (T)instance.Values[index];
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
    public string? name;
}

public sealed class RszFieldAccessorFixedIndex<T>(int index, [CallerMemberName] string name = "") : RszFieldAccessorBase<T>(name)
{
    public override int GetIndex(RszClass instanceClass) => index;
}
public sealed class RszFieldAccessorFixedFunc<T>(Func<RszClass, int> func, [CallerMemberName] string name = "") : RszFieldAccessorBase<T>(name)
{
    public override int GetIndex(RszClass instanceClass) => func.Invoke(instanceClass);
}
public sealed class RszFieldAccessorName<T>(string fieldName, [CallerMemberName] string name = "") : RszFieldAccessorBase<T>(name)
{
    public override int GetIndex(RszClass instanceClass) => instanceClass.IndexOfField(fieldName);
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

    public static TAcc Enum<TAcc>(this TAcc accessor, RszFieldType type, string originalType)
        where TAcc : RszFieldAccessorBase
    {
        accessor.Override ??= new();
        accessor.Override.originalType = originalType;
        accessor.Override.fieldType = type;
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

    public static TAcc Rename<TAcc>(this TAcc accessor, string? name = null)
        where TAcc : RszFieldAccessorBase
    {
        accessor.Override ??= new();
        accessor.Override.name = name ?? accessor.Name;
        return accessor;
    }

    public static TAcc Optional<TAcc>(this TAcc accessor)
        where TAcc : RszFieldAccessorBase
    {
        accessor.Optional = true;
        return accessor;
    }

    private static RszFieldAccessorFixedIndex<T> Index<T>(int index, [CallerMemberName] string name = "")
        => new RszFieldAccessorFixedIndex<T>(index, name);

    private static RszFieldAccessorFixedFunc<T> Func<T>(Func<RszClass, int> func, [CallerMemberName] string name = "")
        => new RszFieldAccessorFixedFunc<T>(func, name);

    private static RszFieldAccessorName<T> Name<T>([CallerMemberName] string fieldName = "", [CallerMemberName] string name = "")
        => new RszFieldAccessorName<T>(fieldName, name);

    private static RszFieldAccessorFirst<T> First<T>(Func<RszField, bool> condition, [CallerMemberName] string name = "")
        => new RszFieldAccessorFirst<T>(condition, name);

    private static RszFieldAccessorFirstFallbacks<T> First<T>(Func<RszField, bool>[] conditions, [CallerMemberName] string name = "")
        => new RszFieldAccessorFirstFallbacks<T>(conditions, name);

    private static RszFieldAccessorLast<T> Last<T>(Func<RszField, bool> condition, [CallerMemberName] string name = "")
        => new RszFieldAccessorLast<T>(condition, name);

    private static RszFieldAccessorLastFallbacks<T> Last<T>(Func<RszField, bool>[] conditions, [CallerMemberName] string name = "")
        => new RszFieldAccessorLastFallbacks<T>(conditions, name);

    private static RszFieldAccessorFirst<List<object>> Array([CallerMemberName] string name = "")
        => new RszFieldAccessorFirst<List<object>>(static (field) => field.array, name);

    private static RszFieldAccessorFieldList<T> FromList<T>(Func<IEnumerable<(RszField field, int index)>, int> conditions, [CallerMemberName] string name = "")
        => new RszFieldAccessorFieldList<T>(conditions, name);

    private static readonly Dictionary<string, string[]> CompositeNames = typeof(RszFieldCache).GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)
        .Where(field => field.FieldType.IsArray && field.FieldType.GetElementType() == typeof(string))
        .ToDictionary(field => field.Name, field => (string[])field.GetValue(null)!)!;

    public static T Get<T>(this RszInstance instance, RszFieldAccessorBase<T> accessor)
        => accessor.Get(instance);

    public static void InitializeFieldOverrides(string gameName, RszParser parser)
    {
        InitializeFieldOverrides(gameName, parser, typeof(RszFieldCache));
    }

    private static void InitializeFieldOverrides(string gameName, RszParser parser, Type type)
    {
        var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var classAttr = type.GetCustomAttributes<RszAccessorAttribute>().FirstOrDefault();
        if (classAttr != null) {
            if (classAttr.Games.Length != 0) {
                var found = false;
                foreach (var game in classAttr.Games) {
                    if (CompositeNames.TryGetValue(game, out var realList)) {
                        if (realList.Contains(gameName)) {
                            found = true;
                            break;
                        }
                    } else {
                        if (game == gameName) {
                            found = true;
                            break;
                        }
                    }
                }
                if (found == classAttr.GamesExclude) return;
            }
        }

        foreach (var field in fields) {
            if (!field.FieldType.IsAssignableTo(typeof(RszFieldAccessorBase))) continue;

            var targets = field.GetCustomAttributes<RszAccessorAttribute>().Append(classAttr);
            if (!targets.Any()) continue;

            var accessor = (RszFieldAccessorBase)field.GetValue(null)!;
            if (accessor.Override == null) continue;

            foreach (var attr in targets) {
                if (attr == null) continue;

                if (attr.Games.Length != 0) {
                    var found = false;
                    foreach (var game in attr.Games) {
                        if (CompositeNames.TryGetValue(game, out var realList)) {
                            if (realList.Contains(gameName)) {
                                found = true;
                                break;
                            }
                        } else {
                            if (game == gameName) {
                                found = true;
                                break;
                            }
                        }
                    }
                    if (found == attr.GamesExclude) continue;
                }

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
                if (accessor.Override.name != null) {
                    rszField.name = accessor.Override.name;
                }
            }
        }

        var nested = type.GetNestedTypes(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var sub in nested) {
            if (sub.Name.EndsWith("<>c")) continue;
            InitializeFieldOverrides(gameName, parser, sub);
        }
    }
}
