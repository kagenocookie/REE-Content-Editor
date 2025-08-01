using ImGuiNET;

namespace ContentEditor.App.ImguiHandling;

public class AutocompleteStringHandler(bool requireListedChoice, string[]? suggestions = null, int lineCount = 8) : IObjectUIHandler
{
    public bool NoUndoRedo { get; set; }

    public void OnIMGUI(UIContext context)
    {
        var selected = context.Get<string>();

        context.state ??= selected ?? string.Empty;
        if (ImGui.InputText(context.label, ref context.state, 1024)) {
            if (!requireListedChoice) {
                selected = context.state;
                OnSelected(context, selected);
            }
        }
        var currentSuggestions = GetSuggestions(context, context.state);
        if (currentSuggestions.Contains(context.state)){
            return;
        }

        if (!string.IsNullOrEmpty(context.state) && ImGui.BeginListBox($"Suggestions##{context.label}", new System.Numerics.Vector2(ImGui.CalcItemWidth(), lineCount * (UI.FontSize + ImGui.GetStyle().FramePadding.Y * 4)))) {
            var items = currentSuggestions.Where(cs => cs.Contains(context.state, StringComparison.OrdinalIgnoreCase));
            foreach (var suggestion in items) {
                if (ImGui.Button(suggestion)) {
                    OnSelected(context, suggestion);
                }
            }
            ImGui.EndListBox();
        }
    }

    protected virtual IEnumerable<string> GetSuggestions(UIContext context, string filter) => suggestions ?? Array.Empty<string>();

    protected virtual void OnSelected(UIContext context, string selection)
    {
        context.state = selection;
        if (!NoUndoRedo) {
            UndoRedo.RecordSet(context, selection);
        } else {
            context.Set(selection);
            context.Changed = false;
        }
    }
}
