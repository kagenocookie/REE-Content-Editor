using System.Reflection;
using System.Text;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using Lua;
using ReeLib;

namespace ContentEditor.App.Lua;

public class LuaTypedefGenerator
{
    private static readonly Dictionary<Type, Type> luaSpecificTypes = new() {
        { typeof(MessageManager), typeof(LuaMessagesManager) },
        { typeof(ResourceManager), typeof(LuaResourceManager) },
        { typeof(EditorWindow), typeof(LuaWindowWrapper) },
        { typeof(Workspace), typeof(LuaWorkspaceWrapper) },
        { typeof(Bundle), typeof(LuaBundleWrapper) },
        { typeof(ContentPatcher.FileHandle), typeof(LuaFileHandleWrapper) },
    };

    private static readonly Dictionary<Type, Dictionary<MemberInfo, Type>> _returnOverrides = new() {
        {
            typeof(LuaFileHandleWrapper),
            new () {
                { typeof(LuaFileHandleWrapper).GetProperty(nameof(LuaFileHandleWrapper.Resource))!, typeof(LuaFileResource<>) }
            }
        }
    };

    private static readonly Dictionary<Type, Type> luaSpecificReverseMap = luaSpecificTypes.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static readonly Dictionary<Type, string> remappedTypes = new() {
        { typeof(System.String), "string" },
        { typeof(System.Single), "number" },
        { typeof(System.Double), "number" },
        { typeof(System.Int32), "integer" },
        { typeof(System.Int16), "integer" },
        { typeof(System.SByte), "integer" },
        { typeof(System.Int64), "integer" },
        { typeof(System.UInt32), "integer" },
        { typeof(System.UInt16), "integer" },
        { typeof(System.Byte), "integer" },
        { typeof(System.UInt64), "integer" },
        { typeof(System.Boolean), "boolean" },
        { typeof(System.Object), "any" },
        { typeof(LuaTable), "table" },
        { typeof(LuaValue), "any" },
        { typeof(void), "nil" },
    };

    private Dictionary<Type, (string text, string typeName)> typesOutput = new();

    private static string TypeName(Type type)
    {
        if (remappedTypes.TryGetValue(type, out var remap)) return remap;

        if (luaSpecificReverseMap.TryGetValue(type, out var luaType)) {
            type = luaType;
        }
        // if (remapped != null) return remapped;

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition().FullName!;

            var sep = definition.IndexOf('`');
            if (sep > 0) {
                definition = definition[..sep];
            }

            var genericArgs = type.GetGenericArguments().Select(TypeName);
            return $"{definition}<{string.Join(", ", genericArgs)}>";
        }

        return type.FullName ?? type.Name;
    }

    private const BindingFlags InstanceTypeflags = BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly;

    public void Dump(string outputFolder)
    {
        var types = typeof(MessageManager).Assembly.GetTypes() // ContentPatcher
            .Concat(typeof(FileHandleBase).Assembly.GetTypes()) // ContentEditor.Core
            .Concat(typeof(MeshFile).Assembly.GetTypes()) // REE-Lib
            // .Concat(typeof(EditorWindow).Assembly.GetTypes()) // ContentEditor.App
            .Concat([typeof(EditorWindow), typeof(LuaFileResource<>), typeof(LuaBaseResource)]) // ContentEditor.App whitelist
            .Concat([typeof(List<>), typeof(Dictionary<,>)]) // System types whitelist
            ;
        var sb = new StringBuilder();
        foreach (var sourceType in types) {
            sb.Clear();
            var type = sourceType;
            if (luaSpecificReverseMap.ContainsKey(sourceType)) {
                continue;
            }
            if (luaSpecificTypes.TryGetValue(sourceType, out var newType)) {
                type = newType;
            }
            if (type.Name.StartsWith("<>") || type.Name.StartsWith('<') || type.Name.Contains(">d__") || type.Name.StartsWith("__StaticArrayInit")) {
                continue;
            }

            if (type.IsEnum) {
                sb.Append("---@enum ").Append(type.FullName).Append(' ').AppendLine(remappedTypes[type.GetEnumUnderlyingType()]);
                sb.Append("local ").Append(type.Name).AppendLine(" = {");
                foreach (var value in type.GetEnumValues()) {
                    var name = type.GetEnumName(value);
                    sb.Append('\t').Append(name).Append(" = ").Append(Convert.ChangeType(value, type.GetEnumUnderlyingType())).AppendLine(",");
                }
                sb.AppendLine("}");
            } else {
                // sb.Append("---");
                if (type.IsInterface) {
                    sb.AppendLine("--- interface");
                } else if (type.IsValueType) {
                    sb.AppendLine("--- value type");
                } else if (type.IsAbstract) {
                    sb.AppendLine("--- abstract");
                }
                // sb.AppendLine();
                sb.Append("---@class ").Append(TypeName(sourceType));
                var parents = new List<string>();
                if (sourceType.BaseType != null && sourceType.BaseType != typeof(object) && sourceType.BaseType != typeof(ValueType)) {
                    parents.Add(sourceType.BaseType.FullName ?? sourceType.BaseType.Name);
                }

                var interfaces = sourceType.GetInterfaces();
                foreach (var inty in interfaces) {
                    parents.Add(TypeName(inty));
                }

                if (parents.Count > 0) {
                    sb.Append(" : ").AppendJoin(", ", parents);
                }
                sb.AppendLine();
                var isRawReflection = type.GetCustomAttribute<LuaObjectAttribute>() == null;

                // fields, methods ...
                if (isRawReflection) {
                    sb.AppendLine("---@field type_info table<string,string> Convenience field containing all of this type's fields and methods");
                }
                var backingFields = new HashSet<string>();
                foreach (var field in type.GetFields(InstanceTypeflags)) {
                    var fieldType = field.FieldType;
                    if (_returnOverrides.TryGetValue(type, out var tov) && tov.TryGetValue(field, out var ret)) {
                        fieldType = ret;
                    }
                    if (field.Name.EndsWith("k__BackingField")) {
                        var propName = field.Name[1..field.Name.IndexOf('>')];
                        var prop = type.GetProperty(propName);
                        var access = "";
                        if (prop!.GetMethod == null || prop.SetMethod == null) {
                            access = prop.GetMethod != null ? " (read-only)" : " (SET-only)";
                        }
                        sb.Append("---@field ").Append(propName).Append(' ').Append(TypeName(fieldType)).Append(access).AppendLine();
                        backingFields.Add(propName);
                    } else {
                        sb.Append("---@field ").Append(field.Name).Append(' ').Append(TypeName(fieldType)).AppendLine();
                    }
                }

                foreach (var prop in type.GetProperties(InstanceTypeflags)) {
                    if (backingFields.Contains(prop.Name)) {
                        continue;
                    }

                    var access = "";
                    if (prop!.GetMethod == null || prop.SetMethod == null) {
                        access = prop.GetMethod != null ? " (read-only)" : " (SET)";
                    }
                    var fieldType = prop.PropertyType;
                    if (_returnOverrides.TryGetValue(type, out var tov) && tov.TryGetValue(prop, out var ret)) {
                        fieldType = ret;
                    }
                    var luaAttr = prop.GetCustomAttribute<LuaMemberAttribute>();
                    if (luaAttr != null) {
                        sb.Append("---@field ").Append(luaAttr.Name).Append(' ').Append(TypeName(fieldType)).Append(access).AppendLine();
                    } else {
                        sb.Append("---@field ").Append(prop.Name).Append(' ').Append(TypeName(fieldType)).Append(access).AppendLine();
                    }
                }

                if (isRawReflection) {
                    sb.AppendLine("---@field get_type_name fun(self): string Full type classname of this object");
                }

                foreach (var method in type.GetMethods(InstanceTypeflags)) {
                    if (method.Name.StartsWith("get_") || method.Name.StartsWith("set_") || method.Name.StartsWith("add_") || method.Name.StartsWith("remove_") || method.Name.StartsWith("op_")) {
                        continue;
                    }

                    var luaAttr = method.GetCustomAttribute<LuaMemberAttribute>();

                    if (luaAttr != null) {
                        sb.Append("---@field ").Append(luaAttr.Name).Append(" fun(self");
                    } else {
                        sb.Append("---@field ").Append(method.Name).Append(" fun(self");
                    }
                    foreach (var pp in method.GetParameters()) {
                        sb.Append(", ");
                        sb.Append(pp.Name).Append(": ").Append(TypeName(pp.ParameterType));
                    }

                    sb.Append("): ");
                    if (_returnOverrides.TryGetValue(type, out var tov) && tov.TryGetValue(method, out var ret)) {
                        sb.Append(TypeName(ret));
                    } else {
                        sb.Append(TypeName(method.ReturnType));
                    }
                    sb.AppendLine();
                }
            }
            sb.AppendLine();
            var cleanName = sourceType.Namespace?.Split('.').FirstOrDefault();
            cleanName ??= sourceType.Name;
            if (cleanName.Contains('`')) {
                cleanName = cleanName.Substring(0, cleanName.IndexOf('`'));
            }

            typesOutput[type] = (sb.ToString(), cleanName);
        }

        var writtenFiles = new Dictionary<string, int>();

        Directory.CreateDirectory(outputFolder);
        foreach (var (type, output) in typesOutput) {
            var outfile = Path.Combine(outputFolder, output.typeName + ".lua");
            int writeCount;
            var subindex = 0;
            while (writtenFiles.TryGetValue(outfile, out writeCount) && writeCount >= 150) {
                outfile = Path.Combine(outputFolder, output.typeName + "-" + (++subindex) + ".lua");
            }
            if (writeCount == 0) {
                File.WriteAllText(outfile, output.text);
            } else {
                File.AppendAllText(outfile, output.text);
            }
            writtenFiles[outfile] = writeCount + 1;
        }
        FileSystemUtils.ShowFileInExplorer(outputFolder);
    }
}