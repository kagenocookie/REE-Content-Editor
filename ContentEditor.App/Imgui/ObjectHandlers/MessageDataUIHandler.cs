using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib.Msg;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(MessageData))]
public class MessageDataUIHandler : IObjectUIHandler
{
    private static readonly string[] LanguageNames = Enum.GetNames<Language>();
    private static readonly Language[] LanguageValues = Enum.GetValues<Language>();

    private Language selectedLanguage = (Language)AppConfig.Instance.PreferredLanguage.Get();

    public void OnIMGUI(UIContext context)
    {
        var data = context.Get<MessageData?>();
        var field = context.GetEntityField()!;

        if (data == null) {
            ImGui.Text("No translations for " + context.label);
            ImGui.SameLine();
            if (ImGui.Button("Create message")) {
                var entity = context.GetOwnerEntity()!;
                var newData = context.GetWorkspace()!.ResourceManager.CreateEntityResource(entity, field, ResourceState.Active);
                UndoRedo.RecordSet(context, newData);
            }
        } else {
            var w = ImGui.CalcItemWidth();
            var langWidth = ImGui.CalcTextSize(selectedLanguage.ToString()).X + ImGui.GetStyle().FramePadding.X * 2 + 32;
            var textWidth = w - langWidth - ImGui.GetStyle().FramePadding.X * 2;

            ImGui.PushID(context.label);
            ImGui.SetNextItemWidth(langWidth);

            if (ImguiHelpers.FilterableCSharpEnumCombo<Language>("##language"u8, ref selectedLanguage, ref context.Filter)) {
                context.Filter = "";
            }
            ImGui.SameLine();
            var msg = data.Get(selectedLanguage) ?? "";
            var multiline = (field.ValueHandler as KeyedMessage)?.multiline ?? false;
            if (multiline) {
                if (ImGui.InputTextMultiline(context.label, ref msg, 1024, new System.Numerics.Vector2(textWidth, 100))) {
                    data.Set(selectedLanguage, msg);
                }
            } else {
                ImGui.SetNextItemWidth(textWidth);
                if (ImGui.InputText(context.label, ref msg, 1024)) {
                    data.Set(selectedLanguage, msg);
                }
            }
            ImGui.PopID();
        }
    }
}
