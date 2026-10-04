using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib.Msg;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(MessageData))]
public class MessageDataResourceEditor : IObjectUIHandler
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
        if (!context.HasBoolState) context.StateBool = data.Messages.All(m => m.Value == data.Messages.FirstOrDefault().Value);
        var sync = context.StateBool;
        using var pfx = ImguiHelpers.InlinePrefix();
        if (ImGui.Button($"{AppIcons.SI_Copy}")) {
            EditorWindow.CurrentWindow?.CopyToClipboard(data.Guid.ToString());
        }
        ImguiHelpers.Tooltip(Lang.Buttons.Copy_Guid);
        ImGui.SameLine();
        if (ImguiHelpers.ToggleButton($"{AppIcons.Loop}", ref sync, Colors.IconActive)) {
            context.StateBool = sync;
        }
        ImguiHelpers.Tooltip(Lang.Buttons.SyncText);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(langWidth);

        if (ImguiHelpers.FilterableCSharpEnumCombo<Language>("##language"u8, ref selectedLanguage, ref context.Filter)) {
            context.Filter = "";
        }
        pfx.Dispose();
        var msg = data.Get(selectedLanguage) ?? "";
        var prevMsg = msg;
        var multiline = (field.ValueHandler as KeyedMessage)?.multiline ?? false;
        if (multiline) {
            if (ImGui.InputTextMultiline(context.label, ref msg, 1024, new System.Numerics.Vector2(0, 100))) {
                var lang = selectedLanguage;
                UndoRedo.RecordCallbackSetter(context, data, prevMsg, msg, (d, v) => d.Set(lang, v!, sync), $"{data.GetHashCode()}{lang}");
            }
        } else {
            if (ImGui.InputText(context.label, ref msg, 1024)) {
                var lang = selectedLanguage;
                UndoRedo.RecordCallbackSetter(context, data, prevMsg, msg, (d, v) => d.Set(lang, v!, sync), $"{data.GetHashCode()}{lang}");
            }
        }
        ImGui.PopID();
    }
}
