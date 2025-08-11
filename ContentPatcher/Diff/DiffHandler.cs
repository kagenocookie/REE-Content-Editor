namespace ContentPatcher;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReeLib;

public class DiffHandler(Workspace env)
{
    private readonly DiffMaker maker = new();
    private readonly DiffPatcher patcher = new();

    public DiffMaker Maker => maker;
    public DiffPatcher Patcher => patcher;

    public JsonNode? GetHierarchicalDataDiff(JsonNode target, JsonNode source)
    {
        return maker.GetMinimalDiff(target, source);
    }

    public void ApplyDiff(RszInstance instance, JsonNode? diff)
    {
        if (diff == null) return;
        var newInstance = instance;
        patcher.ApplyRSZObjectDiff(ref newInstance, diff, env);
        if (newInstance != instance) {
            instance.CopyValuesFrom(newInstance);
        }
    }

    public void ApplyDiff(List<RszInstance> instances, JsonNode? diff, string? elementClassname)
    {
        if (diff == null) return;
        patcher.ApplyRszArrayDiff(instances, diff, elementClassname, env);
    }

    public void ApplyDiff(MessageData instance, JsonNode? diff)
    {
        if (diff is JsonObject obj) {
            if (obj.TryGetPropertyValue("MessageKey", out var key) && key != null) {
                instance.MessageKey = key.GetValue<string>();
            }

            if (obj.TryGetPropertyValue("Guid", out var guid) && key != null && guid != null && Guid.TryParse(guid.GetValue<string>(), out var guidObj)) {
                instance.Guid = guidObj;
            }

            if (obj.TryGetPropertyValue("Messages", out var messages) && messages != null) {
                foreach (var (lang, msg) in (JsonObject)messages) {
                    if (msg != null) {
                        instance.Set(lang, msg.GetValue<string>());
                    }
                }
            }
        }
    }

    public static string GetDiffTree(JsonNode diff)
    {
        var fullString = new StringBuilder();
        var lineBuilder = new StringBuilder();
        GetDiffTreeInternal(diff, fullString, lineBuilder);
        return fullString.ToString();
    }

    private static void GetDiffTreeInternal(JsonNode? diff, StringBuilder full, StringBuilder line)
    {
        // depth first, then backtrack
        /*
        SomeObject._Param.0.sub._Params._Attack: 100
                                       ._Defense: 49
                         .1.sub._Skill: 1
        // instead of array indices, use IDs where possible
        */
        if (diff == null || diff.GetValueKind() == JsonValueKind.Null) {
            full.Append(line).AppendLine(": null");
            return;
        }
        if (diff.GetValueKind() is JsonValueKind.True or JsonValueKind.False or JsonValueKind.String or JsonValueKind.Number) {
            full.Append(line).Append(": ").Append(diff.ToString()).AppendLine();
            return;
        }

        if (diff.GetValueKind() == JsonValueKind.Object) {
            var odiff = (JsonObject)diff;
            if (odiff["$array"] != null) {
                GetDiffTreeInternal(odiff["items"], full, line);
                return;
            }
            var len = line.Length;
            foreach (var prop in odiff) {
                if (prop.Key == "$t") {
                    var type = ((DiffPatchActionTypes)prop.Value!.GetValue<string>()[0]);
                    full.Append(line).Append(" -> ").AppendLine(type.ToString());
                    continue;
                }
                if (prop.Key == "$index") {
                    continue;
                }
                if (prop.Key == "$type") {
                    full.Append(line).Append(" <").Append(prop.Value!.GetValue<string>()).AppendLine(">");
                    continue;
                }
                line.Append('.').Append(prop.Key);
                GetDiffTreeInternal(prop.Value, full, line);
                line.Length = len;
            }
            return;
        }
        if (diff.GetValueKind() == JsonValueKind.Array) {
            var len = line.Length;
            var list = (JsonArray)diff;
            for (var i = 0; i < list.Count; i++) {
                JsonNode? item = list[i];
                if (item is not JsonObject jobj) {
                    line.Append('.').Append(i);
                    GetDiffTreeInternal(item, full, line);
                } else {
                    if (jobj["$index"] != null) {
                        line.Append('.').Append(jobj["$index"]!.GetValue<int>());
                    } else if (jobj["$t"] != null && jobj["$t"]!.GetValue<string>() == "a") {
                        line.Append('.').Append('*');
                    } else {
                        line.Append('.').Append(i);
                    }
                    if (jobj["$item"] != null) {
                        GetDiffTreeInternal(jobj["$item"], full, line);
                    } else {
                        GetDiffTreeInternal(item, full, line);
                    }
                }
                line.Length = len;
            }
        }
    }
}

internal enum DiffPatchActionTypes { Added = 'a', Changed = 'c', Inserted = 'i', Removed = 'r', FullArrayReplace = 'f' }
