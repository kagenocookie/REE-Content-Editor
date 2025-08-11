namespace ContentPatcher;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReeLib.Common;

public class DiffMaker
{
    public Dictionary<string, Func<JsonObject, int>> IDGenerators = new();

    private record struct DiffContext(string? parentClass, string? fieldName);

    private static JsonObject CreateMarkerDiff(DiffPatchActionTypes type) => new JsonObject([
        new KeyValuePair<string, JsonNode?>("$t", (char)type)]);

    private static JsonObject CreateRemoveMarker() => new JsonObject([
        new KeyValuePair<string, JsonNode?>("$t", ((char)DiffPatchActionTypes.Removed).ToString())]);

    private static JsonObject CreateArrayRemoveMarker(JsonNode index) => new JsonObject([
        new KeyValuePair<string, JsonNode?>("$t", ((char)DiffPatchActionTypes.Removed).ToString()),
        new KeyValuePair<string, JsonNode?>("$index", index)]);

    private static JsonObject CreateArrayInsertMarker(JsonNode node, int index) => new JsonObject([
        new KeyValuePair<string, JsonNode?>("$t", ((char)DiffPatchActionTypes.Inserted).ToString()),
        new KeyValuePair<string, JsonNode?>("$index", index),
        new KeyValuePair<string, JsonNode?>("$item", node)]);

    private static JsonObject CreateArrayAddMarker(JsonNode node) => new JsonObject([
        new KeyValuePair<string, JsonNode?>("$t", ((char)DiffPatchActionTypes.Added).ToString()),
        new KeyValuePair<string, JsonNode?>("$item", node)]);

    private static JsonObject CreateArrayChangedMarker(int index, JsonNode diff) => new JsonObject([
        new KeyValuePair<string, JsonNode?>("$t", ((char)DiffPatchActionTypes.Changed).ToString()),
        new KeyValuePair<string, JsonNode?>("$index", index),
        new KeyValuePair<string, JsonNode?>("$item", diff)]);

    public JsonNode? GetMinimalDiff(JsonNode target, JsonNode source)
    {
        return GetMinimalDiff(target, source, null);
    }

    private JsonNode? GetMinimalDiff(JsonNode target, JsonNode source, DiffContext? differ)
    {
        if (target.GetValueKind() == JsonValueKind.Null) {
            return source.GetValueKind() == JsonValueKind.Null ? null : source.DeepClone();
        }

        if (source.GetValueKind() == JsonValueKind.Null) {
            return CreateRemoveMarker();
        }

        if (target.GetValueKind() != source.GetValueKind()) {
            // TODO should we throw an error here instead?
            return source.DeepClone();
        }

        if (source.GetValueKind() == JsonValueKind.Object) {
            var objT = (JsonObject)target;
            var objS = (JsonObject)source;
            if (TryGetNestedArray(objT, out var arr1, out _) && TryGetNestedArray(objS, out var arr2, out var cls)) {
                return GetArrayDiff(arr1, arr2, cls);
            }
            return GetObjectDiff(objT, objS, false);
        }

        if (source.GetValueKind() == JsonValueKind.Array) {
            return GetArrayDiff((JsonArray)target, (JsonArray)source, null);
        }

        return JsonNode.DeepEquals(source, target) ? null : source.DeepClone();
    }

    private bool TryGetNestedArray(JsonNode obj, [MaybeNullWhen(false)] out JsonArray array, out string? classname)
    {
        if (obj.GetValueKind() == JsonValueKind.Array) {
            array = (JsonArray)obj;
            classname = null;
            return true;
        }
        if (((JsonObject)obj).TryGetPropertyValue("$array", out var arrtype) && ((JsonObject)obj).TryGetPropertyValue("items", out var items)) {
            array = (JsonArray)items!;
            classname = arrtype?.GetValue<string>();
            return true;
        }
        array = null;
        classname = null;
        return false;
    }

    private JsonNode? GetObjectDiff(JsonObject target, JsonObject source, bool handleRemovals)
    {
        var targetCls = (target["$type"] as JsonValue)?.GetValue<string>();
        var sourceCls = (source["$type"] as JsonValue)?.GetValue<string>();
        if (sourceCls != null && targetCls != null && sourceCls != targetCls) {
            return source.DeepClone();
        }

        // fields can change with versions, and the source might be applied from the JSON instead of the raw file itself
        // therefore we need to handle missing/added fields properly as well
        // if target has a field that source doesn't => do nothing, we can just leave it there; EXCEPT in the case that we're doing a dictionary-field array (ID based arrays)
        // if source has a field that target doesn't => always add it
        // if a field changed => add it according to diff rules (nested object handling)

        var diff = new JsonObject();
        foreach (var prop in source) {
            var targetValue = target[prop.Key];
            if (targetValue == null) {
                if (prop.Value != null) {
                    diff.Add(prop);
                }
            } else if (prop.Value == null) {
                // field exists but it's explicitly set to null -> remove the target value
                // diff.Add(prop.Key, CreateMarkerDiff(DiffPatchActionTypes.Removed));
                diff.Add(prop.Key, null);
            } else {
                var subdiff = GetMinimalDiff(targetValue, prop.Value);
                if (subdiff != null) {
                    diff.Add(prop.Key, subdiff);
                }
            }
        }
        return diff.Count > 0 ? diff : null;
    }

    private JsonNode? GetArrayDiff(JsonArray target, JsonArray source, string? baseClass)
    {
        if (target.Count == 0) return source.Count == 0 ? null : source.DeepClone();

        if (baseClass == null) {
            // for now, just fully store all non-objects if there's any differences
            if (source.Count > 0 && source[0]!.GetValueKind() != JsonValueKind.Object || target.Count > 0 && target[0]!.GetValueKind() != JsonValueKind.Object) {
                if (source.Count != target.Count) {
                    return source.DeepClone();
                }

                for (int i = 0; i < source.Count; i++) {
                    if (GetMinimalDiff(target[i]!, source[i]!) != null) {
                        return source.DeepClone();
                    }
                }

                return null;
            }

            baseClass = source[0]!["$type"]?.GetValue<string>() ?? target[0]!["$type"]?.GetValue<string>();
        }

        if (baseClass != null && IDGenerators.TryGetValue(baseClass, out var idgen)) {
            // if we have IDs, we ... might still care about order
            // should be configurable per type / parent
            // but usually we dont - we can convert it into a dictionary and diff that instead of using array logic
            // could we solve this at the source ToJson already?
            // we could, BUT, that would mean the instance ID logic would need to be in REE-Lib core already

            // must include source field value data that was used in the ID generation - so the diffs don't rely on any specific hash implementation (since keys == int hashes)

            // var objT = (JsonObject)target;
            // var objS = (JsonObject)source;
            // if (TryGetNestedArray(objT, out var arr1, out _) && TryGetNestedArray(objS, out var arr2, out var cls)) {
            //     return GetArrayDiff(arr1, arr2, cls);
            // }
            // return GetObjectDiff(objT, objS, false);
        }

        // first figure out where we stand at equality on both sides of the arrays
        int head = 0;
        int tail = 0;
        for (; head < target.Count && head < source.Count; ++head) {
            var itemdiff = GetMinimalDiff(source[head]!, target[head]!);
            if (itemdiff != null) break;
        }
        for (; tail < target.Count - head && tail < source.Count - head; ++tail) {
            var itemdiff = GetMinimalDiff(source[source.Count - tail - 1]!, target[target.Count - tail - 1]!);
            if (itemdiff != null) break;
        }

        // all source values are identical within the target array
        if (head + tail == source.Count) {
            // if same length, we're done
            if (source.Count == target.Count) return null;
            // extra items in target that source doesn't have - remove them
            var removedArr = new JsonArray();
            for (int n = head; n < target.Count - tail; ++n) {
                removedArr.Add(CreateArrayRemoveMarker(n));
            }
            return removedArr.Count;
        }

        // all target values are the same as in source
        // new items were added, if they were equal the previous condition would've caught it
        if (head + tail == target.Count) {
            var addedArr = new JsonArray();
            for (int n = head; n < source.Count - tail; ++n) {
                addedArr.Add(tail == 0 ? CreateArrayAddMarker(source[n]!.DeepClone()) : CreateArrayInsertMarker(source[n]!.DeepClone(), n));
            }
            return addedArr;
        }

        // treat everything else as a edit + add/insert/remove
        // assumption here is that _moving_ things isn't gonna happen very commonly

        // in that case, not well-diffed cases:
        // - moving items around
        // - inserting in the middle _and_ at the end
        // - inserts and edits will get treated similarly
        // if such cases are done, the user should mark the resource as a replacer
        // maybe we can let the setting be configurable by classname / path?
        // this could possibly be improved with a myers diff, but it might not matter that much either

        // additional constraint: we always do all update actions first before add/removes, that way we can apply properly later

        var diff = new JsonArray();
        for (; head < Math.Min(source.Count, target.Count); ++head) {
            var itemdiff = GetMinimalDiff(target[head]!, source[head]!);
            if (itemdiff != null) {
                diff.Add(CreateArrayChangedMarker(head, itemdiff));
            }
        }

        if (source.Count < target.Count) {
            // removes
            for (int i = head; i < target.Count - tail; ++i) {
                diff.Add(CreateArrayRemoveMarker(i));
            }
        } else if (source.Count > target.Count) {
            // adds/inserts
            for (int i = head; i < source.Count - tail; ++i) {
                diff.Add(tail == 0 ? CreateArrayAddMarker(source[i]!.DeepClone()) : CreateArrayInsertMarker(source[i]!.DeepClone(), i));
            }
        }

        return diff.Count == 0 ? null : diff;
    }

    private int[,] LcsInternal(List<JsonNode> left, List<JsonNode> right, Func<JsonNode, JsonNode, bool> matcher)
    {
        var arr = new int[left.Count + 1, right.Count + 1];

        for (int i = 1; i <= left.Count; i++) {
            for (int j = 1; j <= right.Count; j++) {
                if (matcher.Invoke(left[i - 1], right[j - 1])) {
                    arr[i, j] = arr[i - 1, j - 1] + 1;
                } else {
                    arr[i, j] = Math.Max(arr[i - 1, j], arr[i, j - 1]);
                }
            }
        }

        return arr;
    }
}