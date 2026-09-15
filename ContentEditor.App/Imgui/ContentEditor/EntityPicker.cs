using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

public class EntityPicker : IObjectUIHandler
{
    public EntityPicker(ContentWorkspace workspace, string entityType, RszFieldType valueType)
        : this(workspace, entityType, RszInstance.RszFieldTypeToCSharpType(valueType)) {}

    public EntityPicker(ContentWorkspace workspace, string entityType, Type valueType)
    {
        this.workspace = workspace;
        this.entityType = entityType;
        this.valueType = valueType;
    }

    private ContentWorkspace workspace;
    private readonly string entityType;
    private readonly Type valueType;

    public void OnIMGUI(UIContext context)
    {
        var instances = workspace.ResourceManager.GetEntityInstances(entityType);
        var selectedId = Convert.ToInt64(context.GetRaw());

        if (ImguiHelpers.FilterableEntityCombo(context.label, instances, ref selectedId, ref context.Filter)) {
            UndoRedo.RecordSet<object>(context, Convert.ChangeType(selectedId, valueType));
            UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, context);
        }

        if (selectedId == -1) {
            return;
        }

        var selected = workspace.ResourceManager.GetActiveEntityInstance(entityType, selectedId);
        if (selected == null) {
            if (selectedId != 0) {
                ImGui.TextColored(Colors.Warning, "Selected entity could not be found");
            }
            return;
        }

        ImguiHelpers.BeginRect();
        ImGui.PushStyleColor(ImGuiCol.Text, Colors.Info);
        var showContents = ImGui.TreeNode(selected.Label);
        ImGui.PopStyleColor();
        if (showContents) {
            if (context.children.Count == 0) {
                context.AddChild("Entity", selected).AddDefaultHandler();
            }
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
        ImguiHelpers.EndRect();
        ImGui.Spacing();
    }
}