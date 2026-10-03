using System.Text.Json.Nodes;
using ContentEditor.App.ImguiHandling;
using ContentPatcher;

namespace ContentEditor.App;

[DialogInputExtension("string-input")]
public class StringNamePreInput : IDialogInputComponent
{
    public string FieldName => this.field?.name ?? "";

    private EntityConfig? config;
    private EntityField? field;

    public void Init(Dictionary<string, object> args, EntityConfig? entity, EntityField? field)
    {
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
            context.Set(JsonValue.Create(text));
        }

        bool valid = true;
        if (field?.ValueHandler is StringCustomField strfield) {
            if (strfield.Regex != null) {
                valid = strfield.Regex.IsMatch(text);
                if (!valid) {
                    ImGui.TextColored(Colors.Error, "Invalid text - it should match the regex pattern: " + strfield.Regex);
                    if (strfield.RegexDescription != null) {
                        ImGui.TextColored(Colors.Error, strfield.RegexDescription);
                    }
                }
            }
            if (valid && strfield.Field.config.TryGetParam<string>("unique_enum", out var enumStr)) {
                var desc = context.GetWorkspace()?.Env.TypeCache.GetEnumDescriptor(enumStr);
                if (desc != null && desc.GetValue(text).ValueKind == System.Text.Json.JsonValueKind.Number) {
                    valid = false;
                    ImGui.TextColored(Colors.Error, $"Invalid text - string must be unique across enum {enumStr}");
                }
            }
            if (!string.IsNullOrEmpty(strfield.Tooltip)) {
                ImGui.TextColored(Colors.Info, strfield.Tooltip);
            }
        }
    }
}
