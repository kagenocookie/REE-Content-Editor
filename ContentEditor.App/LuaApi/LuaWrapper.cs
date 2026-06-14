using System.Text;
using ContentEditor.App.Windowing;
using ContentPatcher;
using Lua;
using Lua.IO;
using Lua.Platforms;
using Lua.Standard;
using ReeLib;

namespace ContentEditor.App.Lua;

public class LuaWrapper
{
    private LuaState state = LuaState.Create(new LuaPlatform(new FileSystem(), new SystemOsEnvironment(), new LuaIO(), System.TimeProvider.System));

    public ContentWorkspace Workspace { get; }

    private LuaWrapper(ContentWorkspace workspace)
    {
        Workspace = workspace;
    }

    public static LuaWrapper Create(ContentWorkspace workspace, EditorWindow? window)
    {
        var wrapper = new LuaWrapper(workspace);
        wrapper.state.OpenBasicLibrary();
        wrapper.state.OpenBitwiseLibrary();
        wrapper.state.OpenMathLibrary();
        // wrapper.state.OpenModuleLibrary(); // enable modules once we implement proper resolution of user lua path files
        wrapper.state.OpenStringLibrary();
        wrapper.state.OpenTableLibrary();
        wrapper.state.Environment["json"] = new LuaJson();
        wrapper.state.Environment["print"] = new LuaLog(workspace.Env.JsonOptions).LogFunction;
        wrapper.state.Environment["env"] = new LuaWorkspaceWrapper(workspace);
        if (window != null) {
            wrapper.state.Environment["window"] = new LuaWindowWrapper(window);
        }
        return wrapper;
    }

    public void Run(string script)
    {
        var tokenSource = new CancellationTokenSource();
        var result = state.DoStringAsync(script, null, tokenSource.Token);
        if (result.IsCompletedSuccessfully) {
            if (result.Result.Length == 0) {
                Logger.Info("Script finished");
            } else if (result.Result.Length == 1) {
                Logger.Info("Script result: " + LuaJson.LuaToString(result.Result[0], Workspace.Env.JsonOptions));
            } else {
                Logger.Info("Results:");
                foreach (var res in result.Result) {
                    Logger.Info(LuaJson.LuaToString(res, Workspace.Env.JsonOptions));
                }
            }
        } else if (result.IsFaulted) {
            var asTask = result.AsTask();
            if (asTask.Exception != null) {
                Logger.Error(asTask.Exception, "Script failed");
            } else {
                Logger.Error("Script failed for unknown reasons");
            }
        } else if (!result.IsCompleted) {
            // async is not supported for now, force cancel it
            tokenSource.Cancel();
            Logger.Warn("The script did not finish immediately, async is not yet supported");
        } else {
            Logger.Info("No idea what happened there, sorry.");
        }
    }

    public static LuaValue ToLua(object? obj)
    {
        if (obj == null) return LuaValue.Nil;
        var type = obj.GetType();
        if (type.IsArray) {
            return ToLuaTable((Array)obj);
        }
        if (obj is RszInstance rsz) {
            return new LuaRszInstance(rsz);
        }
        if (type.IsEnum) {
            return type.GetEnumUnderlyingType() == typeof(ulong) ? LuaValue.FromObject((ulong)obj) : LuaValue.FromObject(Convert.ChangeType(obj, typeof(long)));
        }
        var defaultConvert = LuaValue.FromObject(obj);
        if (defaultConvert.Type is not LuaValueType.Nil and not LuaValueType.LightUserData) {
            return defaultConvert;
        }

        if (type.IsClass) {
            return new LuaReflectionObject(obj);
        }

        return LuaValue.Nil;
    }

    public static LuaTable ToLuaTable<T>(T[] array)
    {
        var result = new LuaTable(array.Length, 0);
        int i = 1;
        foreach (var item in array) {
            result[new LuaValue(i++)] = ToLua(item);
        }
        return result;
    }

    public static LuaTable ToLuaTable(Array array)
    {
        var result = new LuaTable(array.Length, 0);
        int i = 1;
        foreach (var item in array) {
            result[new LuaValue(i++)] = ToLua(item);
        }
        return result;
    }

    public static object[] ArrayFromLuaTable(LuaTable table, Type type)
    {
        if (table.ArrayLength == 0) return [];

        var arr = new object[table.ArrayLength];
        int i = 0;
        foreach (var item in table) {
            arr[i++] = FromLua(item.Value, type)!;
        }
        return arr;
    }
    public static T[] ArrayFromLuaTable<T>(LuaTable table)
    {
        if (table.ArrayLength == 0) return [];

        var arr = new T[table.ArrayLength];
        int i = 0;
        foreach (var item in table) {
            arr[i++] = (T)FromLua<T>(item.Value)!;
        }
        return arr;
    }

    public static T? FromLua<T>(LuaValue value) => (T?)FromLua(value, typeof(T));

    public static object? FromLua(LuaValue value, Type expectedType)
    {
        switch (value.Type) {
            case LuaValueType.Nil: return null;
            case LuaValueType.Boolean: return value.ToBoolean();
            case LuaValueType.Function: return null;
            case LuaValueType.LightUserData: return value.Read<object>();
            case LuaValueType.Number:
                if (expectedType == typeof(double) || expectedType == typeof(float))
                    return Convert.ChangeType(value.Read<double>(), expectedType);
                if (expectedType.IsEnum) {
                    var intValue = Convert.ChangeType(value.Read<long>(), expectedType.GetEnumUnderlyingType());
                    return intValue;
                }
                return Convert.ChangeType(value.Read<long>(), expectedType);
            case LuaValueType.String: {
                    if (expectedType.IsEnum) {
                        var str = value.Read<string>();
                        if (Enum.TryParse(expectedType, str, out var val)) {
                            return val;
                        }
                        return Convert.ChangeType(0, expectedType);
                    }
                    return value.Read<string>();
                }
            case LuaValueType.Thread: break;
            case LuaValueType.Table: break;
            case LuaValueType.UserData: {
                    if (value.TryRead<ILuaUserData>(out var uu) && uu is ILuaObjectWrapper obj) {
                        return obj.Object;
                    }
                    Logger.Warn($"Unable to convert lua object {value.Read<object>()} to c# instance");
                    break;
                }
        }

        return null;
    }

    private sealed class LuaIO : ILuaStandardIO
    {
        private ConsoleStandardIO _defaultIO = new ConsoleStandardIO();

        private ConsoleOutput? standardOutput;

        private ConsoleOutput? standardError;

        public ILuaStream Input => _defaultIO.Input;

        public ILuaStream Output => standardOutput ?? (standardOutput = new ConsoleOutput(LogSeverity.Info));

        public ILuaStream Error => standardError ?? (standardError = new ConsoleOutput(LogSeverity.Error));

        private class ConsoleOutput(LogSeverity level) : ILuaStream
        {
            public bool IsOpen => true;

            public LuaFileOpenMode Mode => LuaFileOpenMode.Append;

            public void Dispose()
            {
            }

            private StringBuilder buffer = new();

            ValueTask ILuaStream.WriteAsync(ReadOnlyMemory<char> content, CancellationToken cancellationToken)
            {
                buffer.Append(content);
                return default(ValueTask);
            }

            ValueTask ILuaStream.WriteAsync(string content, CancellationToken cancellationToken)
            {
                buffer.Append(content);
                return default(ValueTask);
            }


            ValueTask ILuaStream.FlushAsync(CancellationToken cancellationToken)
            {
                Logger.Log(level, buffer.ToString());
                buffer.Clear();
                return default(ValueTask);
            }
        }
    }
}

public interface ILuaObjectWrapper
{
    object Object { get; }
}
