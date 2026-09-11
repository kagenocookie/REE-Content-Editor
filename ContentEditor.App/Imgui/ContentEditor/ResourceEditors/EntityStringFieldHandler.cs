using ContentPatcher;

namespace ContentEditor.App.ImguiHandling.EntityResources;

[ObjectImguiHandler(typeof(StringResource))]
public class EntityStringFieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var entity = context.GetOwnerEntity();
        var field = context.GetEntityField<StringCustomField>()!;
        if (entity == null) {
            ImGui.TextColored(Colors.Error, context.label + ": Entity not found");
            return;
        }
        var data = entity.Get(field) as StringResource;
        if (data == null) {
            if (!field.Field.IsRequired) {
                ImGui.Text(context.label + ": NULL");
                ImGui.SameLine();
                if (ImGui.Button("Add")) {
                    data = new StringResource("");
                    entity.Set(field, data);
                }
                return;
            }

            data = new StringResource("");
            entity.Set(field, data);
        }

        var text = data.Text;
        if (ImGui.InputText(context.label, ref text, 512)) {
            data.Text = text;
            context.Changed = true;
        }
        if (field.Regex != null) {
            var isValid = field.Regex.IsMatch(text);
            if (!isValid) {
                ImGui.TextColored(Colors.Error, "Invalid text - it should match the regex pattern: " + field.Regex);
                if (field.RegexDescription != null) {
                    ImGui.TextColored(Colors.Error, field.RegexDescription);
                }
            }
        }
        if (!string.IsNullOrEmpty(field.Tooltip) && ImGui.IsItemHovered()) {
            ImGui.SetItemTooltip(field.Tooltip);
        }
    }
}
