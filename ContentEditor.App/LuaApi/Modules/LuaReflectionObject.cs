using System.Collections;
using System.Reflection;
using Lua;
using Lua.Runtime;
using ReeLib;
using ReeLib.Efx;
using ReeLib.Motbank;

namespace ContentEditor.App.Lua;

public class LuaReflectionObject(object obj) : ILuaUserData, ILuaObjectWrapper
{
    public object Object { get; } = obj;

    LuaTable? ILuaUserData.Metatable { get; set; } = GetOrCreateMetatable(obj.GetType());

    private static readonly Dictionary<Type, LuaTable> _metaTables = new();
    private static readonly Dictionary<Type, LuaTable> _mixedMetaTables = new();

    private static readonly HashSet<Type> reflectionIgnoredTypes = [typeof(FileHandler), typeof(RszFileOption), typeof(RszParser), typeof(RszClass)];
    private static readonly HashSet<(Type type, string field)> ignoredFields = [
        (typeof(BaseModel), nameof(BaseModel.Start)),
        (typeof(BaseFile), nameof(BaseFile.Size)),
        (typeof(BaseFile), nameof(BaseFile.Embedded)),
    ];

    protected static LuaTable GetOrCreateMetatable(Type type)
    {
        if (_metaTables.TryGetValue(type, out var table)) {
            return table;
        }

        GetOrCreateMetaTableFunctions(type, out var index, out var newindex);
        return _metaTables[type];
    }

    public static void CreateObjectMixedMetaTable(ILuaUserData userData)
    {
        var wrappedType = (userData as ILuaObjectWrapper)?.Object.GetType();
        var userdataType = userData.GetType();
        var type = wrappedType ?? userdataType;
        if (_mixedMetaTables.TryGetValue(type, out var mixedMetaTable)) {
            userData.Metatable = mixedMetaTable;
            return;
        }

        var metaTable = userData.Metatable;
        var normalIndex = metaTable![Metamethods.Index].Read<LuaFunction>();
        var normalNewIndex = metaTable![Metamethods.NewIndex].Read<LuaFunction>();

        GetOrCreateMetaTableFunctions(type, out var reflectionIndex, out var reflectionNewIndex);
        _mixedMetaTables[type] = mixedMetaTable = new LuaTable();

        var index = new LuaFunction("index", (context, ct) => {
            var result = context.State.CallAsync(normalIndex, context.Arguments, ct);
            if (result.IsCompletedSuccessfully) {
                if (result.Result.Length > 1 || result.Result[0] != LuaValue.Nil) {
                    return new ValueTask<int>(context.Return(result.Result));
                }
            }

            result = context.State.CallAsync(reflectionIndex, context.Arguments, ct);
            return new ValueTask<int>(context.Return(result.Result));
        });

        var newindex = new LuaFunction("newindex", (context, ct) => {
            try {
                var result = context.State.CallAsync(normalNewIndex, context.Arguments, ct);
                return new ValueTask<int>(context.Return(result.Result));
            } catch (LuaRuntimeException ex) {
                if (ex.Message.Contains("not found")) {
                    var result = context.State.CallAsync(reflectionNewIndex, context.Arguments, ct);
                    return new ValueTask<int>(context.Return(result.Result));
                }

                throw;
            }
        });
        mixedMetaTable[global::Lua.Runtime.Metamethods.Index] = index;
        mixedMetaTable[global::Lua.Runtime.Metamethods.NewIndex] = newindex;
        userData.Metatable = mixedMetaTable;
    }

    public static void GetOrCreateMetaTableFunctions(Type type, out LuaFunction index, out LuaFunction newindex)
    {
        if (_metaTables.TryGetValue(type, out var table)) {
            index = table[Metamethods.Index].Read<LuaFunction>();
            newindex = table[Metamethods.NewIndex].Read<LuaFunction>();
            return;
        }

        CreateMetaTableFunctions(type, out index, out newindex);
        _metaTables[type] = table = new();
        table[Metamethods.Index] = index;
        table[Metamethods.NewIndex] = newindex;
    }

    private static void CreateMetaTableFunctions(Type type, out LuaFunction index, out LuaFunction newindex)
    {
        var reflectionOptions = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
        // manually build up the class hierarchy so we can enforce correct inherited field ordering (base class fields first)
        var typelist = new List<Type>() { type };
        while (typelist[0].BaseType != null) {
            typelist.Insert(0, typelist[0].BaseType!);
        }
        var getters = new Dictionary<string, Func<object, LuaValue>>();
        var setters = new Dictionary<string, Action<object, LuaValue>>();
        var typeInfo = new LuaTable();

        foreach (var field in typelist.SelectMany(t => t.GetFields(reflectionOptions))) {
            if (reflectionIgnoredTypes.Contains(field.FieldType) || ignoredFields.Contains((field.DeclaringType ?? type, field.Name))) continue;
            if (field.Name.EndsWith(">k__BackingField")) continue;
            if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Span<>)) continue;

            getters[field.Name] = (self) => LuaWrapper.ToLua(field.GetValue(self));
            setters[field.Name] = (self, value) => field.SetValue(self, LuaWrapper.FromLua(value, field.FieldType));
            typeInfo[field.Name] = new LuaValue((field.FieldType.FullName ?? field.FieldType.Name) + " (Field)");
        }

        foreach (var prop in typelist.SelectMany(t => t.GetProperties(reflectionOptions))) {
            if (reflectionIgnoredTypes.Contains(prop.PropertyType) || ignoredFields.Contains((prop.DeclaringType ?? type, prop.Name))) continue;
            if (prop.PropertyType.IsGenericType && prop.PropertyType.GetGenericTypeDefinition() == typeof(Span<>)) continue;

            if (prop.GetMethod != null) {
                getters[prop.Name] = (self) => LuaWrapper.ToLua(prop.GetValue(self));
            }
            if (prop.SetMethod != null) {
                setters[prop.Name] = (self, value) => prop.SetValue(self, LuaWrapper.FromLua(value, prop.PropertyType));
            }
            typeInfo[prop.Name] = new LuaValue((prop.PropertyType.FullName ?? prop.PropertyType.Name) + $" (Property{(prop.GetMethod != null ? " GET" : "")}{(prop.SetMethod != null ? " SET" : "")})");
        }

        foreach (var method in typelist.SelectMany(t => t.GetMethods(reflectionOptions))) {
            if (reflectionIgnoredTypes.Contains(method.ReturnType)) continue;
            if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Span<>)) continue;

            var pp = method.GetParameters();
            LuaFunction? func = null;
            switch (pp.Length) {
                case 0:
                    func = new LuaFunction((context, token) => {
                        var self = (context.GetArgument(0).Read<ILuaUserData>() as ILuaObjectWrapper)?.Object ?? context.GetArgument(0).Read<ILuaUserData>();
                        var result = method.Invoke(self, []);
                        return new ValueTask<int>(context.Return(LuaWrapper.ToLua(result)));
                    });
                    break;
                case 1:
                    func = new LuaFunction((context, token) => {
                        var self = (context.GetArgument(0).Read<ILuaUserData>() as ILuaObjectWrapper)?.Object ?? context.GetArgument(0).Read<ILuaUserData>();
                        var arg1 = LuaWrapper.FromLua(context.GetArgument(1), pp[0].ParameterType);
                        var result = method.Invoke(self, [arg1]);
                        return new ValueTask<int>(context.Return(LuaWrapper.ToLua(result)));
                    });
                    break;
                case 2:
                    func = new LuaFunction((context, token) => {
                        var self = (context.GetArgument(0).Read<ILuaUserData>() as ILuaObjectWrapper)?.Object ?? context.GetArgument(0).Read<ILuaUserData>();
                        var arg1 = LuaWrapper.FromLua(context.GetArgument(1), pp[0].ParameterType);
                        var arg2 = LuaWrapper.FromLua(context.GetArgument(2), pp[1].ParameterType);
                        var result = method.Invoke(self, [arg1, arg2]);
                        return new ValueTask<int>(context.Return(LuaWrapper.ToLua(result)));
                    });
                    break;
                case 3:
                    func = new LuaFunction((context, token) => {
                        var self = (context.GetArgument(0).Read<ILuaUserData>() as ILuaObjectWrapper)?.Object ?? context.GetArgument(0).Read<ILuaUserData>();
                        var arg1 = LuaWrapper.FromLua(context.GetArgument(1), pp[0].ParameterType);
                        var arg2 = LuaWrapper.FromLua(context.GetArgument(2), pp[1].ParameterType);
                        var arg3 = LuaWrapper.FromLua(context.GetArgument(3), pp[2].ParameterType);
                        var result = method.Invoke(self, [arg1, arg2, arg3]);
                        return new ValueTask<int>(context.Return(LuaWrapper.ToLua(result)));
                    });
                    break;
                default:
                    break;
            }

            getters[method.Name] = (self) => func ?? LuaValue.Nil;
            typeInfo[method.Name] = $":{method.Name}({string.Join(", ", pp.Select(p => $"{p.Name}: {p.ParameterType.FullName}"))})";
        }

        var typeNameGetter = new LuaFunction((context, token) => {
            var self = (context.GetArgument(0).Read<ILuaUserData>() as ILuaObjectWrapper)?.Object ?? context.GetArgument(0).Read<ILuaUserData>();
            return new ValueTask<int>(context.Return(self.GetType().FullName ?? self.GetType().Name));
        });
        getters["get_type_name"] = (_) => typeNameGetter;
        getters["type_info"] = (_) => typeInfo;

        index = new LuaFunction("index", (context, ct) => {
            var userData = context.GetArgument<object>(0);
            var obj = (userData as ILuaObjectWrapper)?.Object ?? userData;
            var keyArg = context.GetArgument(1);
            if (keyArg.Type == LuaValueType.String) {
                var key = keyArg.Read<string>();
                var result = getters.GetValueOrDefault(key)?.Invoke(obj) ?? LuaValue.Nil;
                return new ValueTask<int>(context.Return(result));
            } else if (keyArg.Type == LuaValueType.Number) {
                var index = keyArg.Read<int>();
                if (type.IsArray) {
                    return new ValueTask<int>(context.Return(LuaWrapper.ToLua(((Array)obj).GetValue(index))));
                } else if (obj is IEnumerable<object> enn) {
                    return new ValueTask<int>(context.Return(LuaWrapper.ToLua(enn.ElementAtOrDefault(index))));
                } else {
                    Logger.Error("Invalid list access on object of type " + type.GetType().FullName);
                    return new ValueTask<int>(context.Return());
                }
            } else {
                return new ValueTask<int>(context.Return());
            }
        });
        if (type.IsArray) {
            var elemType = type.GetElementType()!;
            newindex = new LuaFunction("newindex", (context, ct) => {
                var userData = context.GetArgument<object>(0);
                var obj = (userData as ILuaObjectWrapper)?.Object ?? userData;
                var index = context.GetArgument<int>(1);
                var value = context.GetArgument(2);
                ((Array)userData).SetValue(LuaWrapper.FromLua(value, elemType), index);
                return new ValueTask<int>(context.Return());
            });
        } else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) {
            var elemType = type.GetGenericArguments()[0]!;
            newindex = new LuaFunction("newindex", (context, ct) => {
                var userData = context.GetArgument<object>(0);
                List<object> obbbb = new();
                var list = ((userData as ILuaObjectWrapper)?.Object ?? userData) as IList;
                var index = context.GetArgument<int>(1);
                var value = context.GetArgument(2);
                list![index] = LuaWrapper.FromLua(value, elemType);
                return new ValueTask<int>(context.Return());
            });
        } else {
            newindex = new LuaFunction("newindex", (context, ct) => {
                var userData = context.GetArgument<object>(0);
                var obj = (userData as ILuaObjectWrapper)?.Object ?? userData;
                var keyArg = context.GetArgument(1);
                var value = context.GetArgument(2);

                var key = context.GetArgument<string>(1);
                if (setters.TryGetValue(key, out var setter)) {
                    setter.Invoke(obj, value);
                } else {
                    Logger.Error($"Unknown field {key} for type {obj.GetType().FullName}");
                    throw new LuaRuntimeException(context.State, $"'{key}' not found.");
                }
                return new ValueTask<int>(context.Return());
            });
        }
    }

    public static implicit operator LuaValue(LuaReflectionObject value)
    {
        return LuaValue.FromUserData(value);
    }
}
