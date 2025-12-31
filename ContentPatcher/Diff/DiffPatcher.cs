namespace ContentPatcher;

using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReeLib;

public class DiffPatcher
{
    private static DiffPatchActionTypes ReadPatchAction(JsonObject obj)
        => obj.ContainsKey("$t") ? (DiffPatchActionTypes)(obj["$t"]!.GetValue<string>()?[0] ?? 0) : 0;

    // private sealed record DiffContext(RszParser parser);

    public void ApplyRSZObjectDiff(ref RszInstance instance, JsonNode diff, Workspace env)
    {
        if (diff.GetValueKind() == JsonValueKind.Null) return;
        if (diff.GetValueKind() != JsonValueKind.Object) throw new ArgumentException("Object diff must be an object", nameof(diff));

        var diffObj = (JsonObject)diff;
        var newClassname = diffObj["$type"];
        if (newClassname != null) {
            if (instance.RszClass.name != newClassname.GetValue<string>()) {
                var newClass = env.RszParser.GetRSZClass(newClassname.GetValue<string>())
                    ?? throw new Exception("Invalid RSZ class " + newClassname.GetValue<string>());

                instance = new RszInstance(newClass);
            }
        }
        var cls = instance.RszClass;
        foreach (var diffprop in diffObj) {
            var fieldIndex = cls.IndexOfField(diffprop.Key);
            if (fieldIndex == -1) continue;

            var field = cls.fields[fieldIndex];
            var value = instance.Values[fieldIndex];
            if (field.array) {
                instance.Values[fieldIndex] = ApplyRSZArrayDiff(value, diffprop.Value, field, env)
                    ?? RszInstance.CreateArrayItem(env.RszParser, field)!;
            } else if (field.type is RszFieldType.Object or RszFieldType.Struct) {
                instance.Values[fieldIndex] = CreateOrApplyRSZDiff(value as RszInstance, diffprop.Value, field.original_type, env);
            } else if (field.type is RszFieldType.String or RszFieldType.RuntimeType or RszFieldType.Resource) {
                instance.Values[fieldIndex] = diffprop.Value?.GetValue<string>() ?? instance.Values[fieldIndex] ?? string.Empty;
            } else {
                var csType = RszInstance.RszFieldTypeToCSharpType(field.type);
                instance.Values[fieldIndex] = diffprop.Value.Deserialize(csType, env.JsonOptions) ?? Activator.CreateInstance(csType)!;
            }
        }
    }

    public void ApplyRszArrayDiff(List<RszInstance> instances, JsonNode diff, string? elementClassname, Workspace env)
    {
        ApplyRSZArrayDiff(instances, diff, RszFieldType.Object, elementClassname, env);
    }

    private RszInstance CreateOrApplyRSZDiff(RszInstance? instance, JsonNode? diff, string? classname, Workspace env)
    {
        if (instance == null) {
            var cls = env.RszParser.GetRSZClass(classname!)
                ?? throw new Exception("Failed to create instance");
            instance = RszInstance.CreateInstance(env.RszParser, cls);
        }
        if (diff == null) return instance;

        ApplyRSZObjectDiff(ref instance, diff, env);

        return instance;
    }

    private object? ApplyRSZArrayDiff(object? value, JsonNode? diff, RszField field, Workspace env) => ApplyRSZArrayDiff(value, diff, field.type, field.original_type, env);
    private object? ApplyRSZArrayDiff(object? value, JsonNode? diff, RszFieldType type, string? elementClassname, Workspace env)
    {
        if (diff == null) return new List<object>(0);

        var csType = RszInstance.RszFieldTypeToRuntimeCSharpType(type);
        IList list = value as IList ?? new List<object>();

        if (diff.GetValueKind() == JsonValueKind.Object) {
            var jdiff = (JsonObject)diff;
            if (jdiff["$array"] != null) {
                diff = jdiff["items"];
                if (diff == null) return value;
                if (diff is JsonArray arrrr && arrrr.Count > 0 && arrrr[0] != null && arrrr[0]!.GetValueKind() == JsonValueKind.Object && ReadPatchAction(diff[0]!.AsObject()) == 0) {
                    // if the first item isn't a patch-annotated object, none of them will be
                    // in other words, this is a fully serialized, non-diffed array
                    // clear whatever we have in the current list and recreate the items from the json data
                    list.Clear();
                    foreach (var item in arrrr) {
                        list.Add(item?.Deserialize(csType, env.JsonOptions));
                    }
                    return list;
                }
            } else {
                // TODO ID-enabled array, treat it basically like an object
                return value;
            }
        }

        // TODO userdata
        var removeIndices = new List<int>();
        var arr = (JsonArray)diff;
        if (arr.Count > 0 && arr[0]?.GetValueKind() is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number or JsonValueKind.String) {
            // not a diffable array type, apply it as a whole
            list.Clear();
            switch (arr[0]!.GetValueKind()) {
                case JsonValueKind.True:
                case JsonValueKind.False:
                    foreach (var item in arr) list.Add(item?.GetValue<bool>() ?? false);
                    return list;
                case JsonValueKind.Number:
                    // in this case we need to match the target value type
                    foreach (var item in arr) {
                        if (type == RszFieldType.U64) {
                            list.Add(Convert.ChangeType(item!.GetValue<ulong>(), csType));
                        } else {
                            list.Add(Convert.ChangeType(item!.GetValue<long>(), csType));
                        }
                    }
                    return list;
                case JsonValueKind.String:
                    foreach (var item in arr) list.Add(item?.GetValue<string>() ?? "");
                    return list;
                default:
                    throw new NotImplementedException("Unsupported diff array element type " + arr[0]!.GetValueKind());
            }
        }
        var indexOffset = 0;
        // constraints: array item indices must be in ascending order
        for (var i = 0; i < arr.Count; i++) {
            var item = arr[i];
            if (item is not JsonObject itemObj) {
                throw new Exception("Array element diff must be objects");
            }

            var actionType = ReadPatchAction(itemObj);
            int index;
            switch (actionType) {
                case DiffPatchActionTypes.Added:
                    list.Add(item["$item"].Deserialize(csType, env.JsonOptions)!);
                    break;
                case DiffPatchActionTypes.Changed:
                    index = indexOffset + item["$index"]!.GetValue<int>();
                    if (type is RszFieldType.Object or RszFieldType.Struct) {
                        var instance = list[index] as RszInstance;
                        list[index] = CreateOrApplyRSZDiff(instance, item["$item"], elementClassname, env);
                    } else {
                        list[index] = item["$item"].Deserialize(csType, env.JsonOptions)!;
                    }
                    break;
                case DiffPatchActionTypes.Inserted:
                    index = indexOffset + item["$index"]!.GetValue<int>();
                    list.Insert(index, item["$item"].Deserialize(csType, env.JsonOptions)!);
                    indexOffset++;
                    break;
                case DiffPatchActionTypes.Removed:
                    index = indexOffset + item["$index"]!.GetValue<int>();
                    list.RemoveAt(index);
                    indexOffset--;
                    break;
            }
        }

        return list;
    }
}