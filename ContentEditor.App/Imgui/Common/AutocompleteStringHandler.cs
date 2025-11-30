using ImGuiNET;

namespace ContentEditor.App.ImguiHandling;

public class AutocompleteStringHandler(bool requireListedChoice, string[]? suggestions = null, int lineCount = 8) : IObjectUIHandler
{
    public bool NoUndoRedo { get; set; }

    public void OnIMGUI(UIContext context)
    {
        var selected = context.Get<string>();
        context.InitFilterDefault(selected);
        var didChange = false;
        if (ImGui.InputText(context.label, ref context.Filter, 1024)) {
            didChange = true;
            if (!requireListedChoice) {
                selected = context.Filter;
                OnSelected(context, selected);
            }
        }
        var filter = context.Filter;
        var currentSuggestions = GetSuggestions(context, filter);
        if (currentSuggestions.Contains(filter)){
            if (requireListedChoice && didChange) {
                OnSelected(context, filter);
            }
            return;
        }
        if (requireListedChoice && !currentSuggestions.Any()) {
            ImGui.TextColored(Colors.Info, "No options available");
            return;
        }

        if (!string.IsNullOrEmpty(filter) && ImGui.BeginListBox($"Suggestions##{context.label}", new System.Numerics.Vector2(ImGui.CalcItemWidth(), lineCount * (UI.FontSize + ImGui.GetStyle().FramePadding.Y * 4)))) {
            var items = currentSuggestions.Where(cs => cs.Contains(filter, StringComparison.OrdinalIgnoreCase));
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
        context.Filter = selection;
        if (!NoUndoRedo) {
            UndoRedo.RecordSet(context, selection);
        } else {
            context.Set(selection);
            context.Changed = false;
        }
    }
}
