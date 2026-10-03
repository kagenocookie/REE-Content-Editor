using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;

namespace ContentEditor.App;

[DialogInputExtension("string-input")]
public class StringNamePreInput : IDialogInputComponent
{
    public string FieldName { get; set; } = "";

    private EntityConfig? config;
    private EntityField? field;
    private Dictionary<string, object> args = null!;

    public void Init(Dictionary<string, object> args, EntityConfig? entity, EntityField? field)
    {
        this.args = args;
        this.config = entity;
        this.field = field;
    }

    public bool IsValid(UIContext context, JsonObject data)
    {
        var text = data[FieldName]?.GetValue<string>();
        if (string.IsNullOrEmpty(text)) return false;

        if (field?.ValueHandler is StringCustomField strfield) {
            if (strfield.Regex != null && !strfield.Regex.IsMatch(text)) {
                return false;
            }
            if (strfield.Field.config.TryGetParam<string>("unique_enum", out var enumStr)) {
                var enumdec = context.GetWorkspace()?.Env.TypeCache.GetEnumDescriptor(enumStr);
                if (enumdec != null && enumdec.GetValue(text).ValueKind == System.Text.Json.JsonValueKind.Number) {
                    return false;
                }
            }
        }

        return true;
    }

    public void OnIMGUI(UIContext context)
    {
        var text = context.Get<JsonValue>()?.GetValue<string>() ?? "";

        if (ImGui.InputText(context.label, ref text, 512)) {
            var newValueJson = JsonValue.Create(text);
            context.Set(newValueJson);
            if (TryGetParam<List<object>>("write_to", out var writeList) == true && context.parent?.TryCast<JsonObject>(out var obj) == true) {
                foreach (var path in writeList.OfType<string>()) {
                    BundleRuntimeMapping.SetNodeByPath(obj, path, newValueJson.DeepClone(), false);
                }
            }
        }

        bool valid = true;
        if (TryGetParam<string>("tooltip", out var tooltip)) {
            ImGui.TextColored(Colors.Info, tooltip);
        }

        if (TryGetParam<string>("regex", out var regex)) {
            var reg = new Regex(regex);
            valid = reg.IsMatch(text);
            if (!valid) {
                ImGui.TextColored(Colors.Error, "Invalid text - it should match the regex pattern: " + regex);
                if (TryGetParam<string>("regex_description", out var regexDesc)) {
                    ImGui.TextColored(Colors.Error, regexDesc);
                }
            }
        }

        if (valid && TryGetParam<string>("unique_enum", out var uniqueEnum) && !string.IsNullOrEmpty(uniqueEnum)) {
            var desc = context.GetWorkspace()?.Env.TypeCache.GetEnumDescriptor(uniqueEnum);
            if (desc != null && desc.GetValue(text).ValueKind == System.Text.Json.JsonValueKind.Number) {
                valid = false;
                ImGui.TextColored(Colors.Error, $"Invalid text - string must be unique across enum {uniqueEnum}");
            }
        }
    }

    private T? GetParam<T>(string name) where T : class
    {
        return field?.config.GetParam<T>(name) ?? args.GetValueOrDefault(name) as T;
    }

    private bool TryGetParam<T>(string name, [MaybeNullWhen(false)] out T value) where T : class
    {
        if (field?.config.TryGetParam<T>(name, out value) == true) {
            return true;
        }

        if (args.TryGetValue(name, out var tval) && tval is T tcast) {
            value = tcast;
            return true;
        }

        value = null;
        return false;
    }
}
