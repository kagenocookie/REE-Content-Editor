using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
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
        var data = context.Get<MessageData>();
        var field = context.GetEntityField()!;

        var w = ImGui.CalcItemWidth();
        var langWidth = ImGui.CalcTextSize(selectedLanguage.ToString()).X + ImGui.GetStyle().FramePadding.X * 2 + 32;

        ImGui.PushID(context.label);
        using var pfx = ImguiHelpers.InlinePrefix();
        if (ImGui.Button($"{AppIcons.SI_Copy}")) {
            EditorWindow.CurrentWindow?.CopyToClipboard(data.Guid.ToString());
        }
        ImguiHelpers.Tooltip(Lang.Buttons.Copy_Guid);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(langWidth);

        if (ImguiHelpers.FilterableCSharpEnumCombo<Language>("##language"u8, ref selectedLanguage, ref context.Filter)) {
            context.Filter = "";
        }
        pfx.Dispose();
        var msg = data.Get(selectedLanguage) ?? "";
        var multiline = (field.ValueHandler as KeyedMessage)?.multiline ?? false;
        if (multiline) {
            if (ImGui.InputTextMultiline(context.label, ref msg, 1024, new System.Numerics.Vector2(0, 100))) {
                data.Set(selectedLanguage, msg);
            }
        } else {
            if (ImGui.InputText(context.label, ref msg, 1024)) {
                data.Set(selectedLanguage, msg);
            }
        }
        ImGui.PopID();
    }
}
