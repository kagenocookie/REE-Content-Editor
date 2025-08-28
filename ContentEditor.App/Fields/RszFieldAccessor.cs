using System.Runtime.CompilerServices;
using ReeLib;

namespace ContentEditor.App;

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

public sealed class RszFieldAccessorLastCallbacks<T>(Func<RszField, bool>[] conditions, [CallerMemberName] string name = "") : RszFieldAccessorBase<T>(name)
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
}
