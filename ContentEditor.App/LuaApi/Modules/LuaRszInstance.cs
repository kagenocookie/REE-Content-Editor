using Lua;
using Lua.Runtime;
using ReeLib;

namespace ContentEditor.App.Lua;

public partial class LuaRszInstance(RszInstance instance) : ILuaUserData, ILuaObjectWrapper
{
    public RszInstance Instance { get; } = instance;
    object ILuaObjectWrapper.Object => Instance;

    private static readonly Dictionary<RszClass, LuaTable> _metaTables = new();

    LuaTable? ILuaUserData.Metatable { get; set; } = GetOrCreateMetatable(instance.RszClass);

    protected static LuaTable GetOrCreateMetatable(RszClass type)
    {
        if (_metaTables.TryGetValue(type, out var table)) {
            return table;
        }

        GetOrCreateMetaTableFunctions(type, out var index, out var newindex);
        return _metaTables[type];
    }

    public static void GetOrCreateMetaTableFunctions(RszClass type, out LuaFunction index, out LuaFunction newindex)
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

    private static void CreateMetaTableFunctions(RszClass type, out LuaFunction index, out LuaFunction newindex)
    {
        var getters = new Dictionary<string, Func<RszInstance, LuaValue>>();
        var setters = new Dictionary<string, Action<RszInstance, LuaValue>>();

        for (int i = 0; i < type.fields.Length; i++) {
            var field = type.fields[i];
            int fieldIndex = i;
            getters[field.name] = (self) => LuaWrapper.ToLua(self.Values[fieldIndex]);
            var csType = field.type switch {
                RszFieldType.String or RszFieldType.RuntimeType => typeof(string),
                RszFieldType.Object or RszFieldType.Struct => typeof(RszInstance),
                _ => RszInstance.RszFieldTypeToCSharpType(field.type),
            };
            if (field.array) {
                switch (field.type) {
                    case RszFieldType.Object:
                    case RszFieldType.Struct:
                        setters[field.name] = (self, value) => {
                            var list = new List<object>();
                            var arr = LuaWrapper.ArrayFromLuaTable<RszInstance>(value.Read<LuaTable>());
                            list.AddRange(arr);
                            foreach (var item in list) {
                                if (item is not RszInstance) {
                                    throw new Exception("RSZ list field elements must all be rsz instances!");
                                }
                            }
                            self.Values[fieldIndex] = list;
                        };
                        break;
                    case RszFieldType.UserData:
                        // no setter for now
                        break;
                    case RszFieldType.Data:
                        // don't add setters here, we have no idea what it's actually supposed to be
                        break;
                    default: {
                            // setters[field.name] = (self, value) => self.Values[fieldIndex] = LuaWrapper.FromLua(value, csType) ?? Activator.CreateInstance(csType)!;
                            setters[field.name] = (self, value) => {
                                var list = new List<object>();
                                var arr = LuaWrapper.ArrayFromLuaTable(value.Read<LuaTable>(), csType);
                                list.AddRange(arr);
                                self.Values[fieldIndex] = list;
                            };
                            break;
                        }
                }
            } else {
                switch (field.type) {
                    case RszFieldType.Object:
                    case RszFieldType.Struct:
                        setters[field.name] = (self, value) => {
                            if (value.TryRead<ILuaUserData>(out var ilud) && ilud is LuaRszInstance rsz) {
                                self.Values[fieldIndex] = rsz.Instance;
                            }
                        };
                        break;
                    case RszFieldType.UserData:
                        // no setter for now
                        break;
                    case RszFieldType.Data:
                        // don't add setters here, we have no idea what it's actually supposed to be
                        break;
                    default: {
                            setters[field.name] = (self, value) => self.Values[fieldIndex] = LuaWrapper.FromLua(value, csType) ?? Activator.CreateInstance(csType)!;
                            break;
                        }
                }
            }
        }

        getters["class"] = (_) => new LuaReflectionObject(type);
        getters["classname"] = (_) => type.name;
        getters["short_classname"] = (_) => type.ShortName;

        index = new LuaFunction("index", (context, ct) => {
            var userData = context.GetArgument<object>(0);
            var obj = (userData as LuaRszInstance)?.Instance;
            var key = context.GetArgument<string>(1);
            var result = obj == null ? LuaValue.Nil : getters.GetValueOrDefault(key)?.Invoke(obj) ?? LuaValue.Nil;
            return new ValueTask<int>(context.Return(result));
        });
        newindex = new LuaFunction("newindex", (context, ct) => {
            var userData = context.GetArgument<object>(0);
            var obj = (userData as LuaRszInstance)?.Instance;
            var key = context.GetArgument<string>(1);
            var value = context.GetArgument(2);
            if (obj != null && setters.TryGetValue(key, out var setter)) {
                setter.Invoke(obj, value);
                return new ValueTask<int>(context.Return());
            } else {
                Logger.Error($"Unknown field {key} for RSZ type {type.name}");
                throw new LuaRuntimeException(context.State, $"'{key}' not found.");
            }
        });
    }

    public static implicit operator LuaValue(LuaRszInstance value)
    {
        return LuaValue.FromUserData(value);
    }
}
