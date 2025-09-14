using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ImGuiNET;
using ReeLib.Msg;

namespace ContentEditor.App;

[CustomFieldHandler(typeof(SingleMsgCustomField))]
public class MessageDataUIHandler : IObjectUIHandler, IObjectUIInstantiator
{
    public static Func<CustomField, IObjectUIHandler> GetFactory() => (field) => new MessageDataUIHandler((SingleMsgCustomField)field);

    private static readonly string[] LanguageNames = Enum.GetNames<Language>();
    private static readonly Language[] LanguageValues = Enum.GetValues<Language>();
    private SingleMsgCustomField field;

    public MessageDataUIHandler(SingleMsgCustomField field)
    {
        this.field = field;
    }

    public void OnIMGUI(UIContext context)
    {
        var data = context.Get<MessageData?>();

        if (data == null) {
            ImGui.Text("No translations for " + context.label);
            ImGui.SameLine();
            if (ImGui.Button("Create message")) {
                var entity = context.GetOwnerEntity()!;
                var newData = context.GetWorkspace()!.ResourceManager.CreateEntityResource<MessageData>(entity, field, ResourceState.Active, null);
                UndoRedo.RecordSet(context, newData);
            }
        } else {
            // TODO app configurable default language?
            var lang = context.state ?? Language.English.ToString();
            var langIndex = Array.IndexOf(LanguageNames, lang);

            var w = ImGui.CalcItemWidth();
            var langWidth = ImGui.CalcTextSize(lang).X + ImGui.GetStyle().FramePadding.X * 2 + 32;
            var textWidth = w - langWidth - ImGui.GetStyle().FramePadding.X * 2;

            ImGui.PushID(context.label);
            ImGui.SetNextItemWidth(langWidth);
            if (ImGui.Combo("##language", ref langIndex, LanguageNames, LanguageNames.Length)) {
                context.state = lang = LanguageNames[langIndex];
            }
            ImGui.SameLine();
            var msg = data.Get(lang) ?? "";
            if (field.multiline) {
                if (ImGui.InputTextMultiline(context.label, ref msg, 1024, new System.Numerics.Vector2(textWidth, 100))) {
                    data.Set(lang, msg);
                }
            } else {
                ImGui.SetNextItemWidth(textWidth);
                if (ImGui.InputText(context.label, ref msg, 1024)) {
                    data.Set(lang, msg);
                }
            }
            ImGui.PopID();
        }
    }
}
