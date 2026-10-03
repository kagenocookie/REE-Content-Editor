using System.Text.Json.Nodes;

namespace ContentEditor.App.Widgets;

public class DynamicInputHandler(UIContext context, JsonObject initialData) : ConfirmCancelDialog(Lang.Entities.NewEntityInputTitle.String)
{
    public JsonObject Data { get; set; } = initialData;

    public bool IsCancelled { get; private set; }

    protected override bool Show()
    {
        for (int i = 0; i < context.children.Count; i++) {
            ImGui.PushID(i);
            context.children[i].ShowUI();
            ImGui.PopID();
        }

        return false;
    }

    protected override bool IsConfirmDisabled()
    {
        var isValid = context.children
            .Select(c => c.uiHandler as IDialogInputComponent!)
            .All(c => c == null || c.IsValid(context, Data));
        return !isValid;
    }
}
