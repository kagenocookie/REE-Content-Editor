using ContentPatcher;
using ImGuiNET;

namespace ContentEditor.App.ImguiHandling.EntityResources;

[CustomFieldHandler(typeof(StringCustomField))]
public class EntityStringFieldHandler(StringCustomField field) : IObjectUIHandler, IObjectUIInstantiator
{
    public static Func<CustomField, IObjectUIHandler> GetFactory() => (field) => new EntityStringFieldHandler((StringCustomField)field);

    public void OnIMGUI(UIContext context)
    {
        var entity = context.GetOwnerEntity();
        if (entity == null) {
            ImGui.TextColored(Colors.Error, context.label + ": Entity not found");
            return;
        }
        var data = entity.Get(field.name) as StringResource;
        if (data == null) {
            if (!field.IsRequired) {
                ImGui.Text(context.label + ": NULL");
                ImGui.SameLine();
                if (ImGui.Button("Add")) {
                    data = new StringResource("");
                    entity.Set(field.name, data);
                }
                return;
            }

            data = new StringResource("");
            entity.Set(field.name, data);
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
