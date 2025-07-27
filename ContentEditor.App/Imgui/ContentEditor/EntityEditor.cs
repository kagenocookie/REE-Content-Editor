using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;

namespace ContentEditor.App.DD2;

public class EntityEditor : IWindowHandler
{
    public string HandlerName => nameof(EntityEditor);
    public bool HasUnsavedChanges => data?.Context?.GetChildByValue<ResourceEntity>()?.Changed == true;

    public EntityEditor(ContentWorkspace workspace, string entityType)
    {
        this.workspace = workspace;
        this.entityType = entityType;
    }

    private ContentWorkspace workspace;
    private readonly string entityType;
    private WindowData data = null!;
    protected UIContext context = null!;

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        if (workspace == null) {
            ImGui.TextColored(Colors.Warning, "Couldn't get game configuration");
            return;
        }

        if (data.Context == null) {
            ImGui.TextColored(Colors.Error, "Missing UI container");
            return;
        }

        var instances = workspace.ResourceManager.GetEntityInstances(entityType);
        var selectedId = data.GetOrAddPersistentData<long>("selectedEntity", -1);

        if (ImguiHelpers.FilterableEntityCombo("Entity", instances, ref selectedId, ref data.Context.state)) {
            data.SetPersistentData("selectedEntity", selectedId);
            // note: we can clear children safely, any changes are still stored in the resource manager
            // just gotta figure out how to keep those changes tracked in bundle
            data.Context.ClearChildren();
        }

        if (selectedId == -1) {
            return;
        }

        var selected = workspace.ResourceManager.GetActiveEntityInstance(entityType, selectedId);
        if (selected == null) {
            ImGui.TextColored(Colors.Warning, "Selected object could not be found");
            return;
        }

        if (ImGui.TreeNode("Change label")) {
            var newName = data.Context.GetChildValue<string>();
            if (newName == null) {
                data.Context.AddChild("Rename", newName = selected.Label);
            }
            if (ImGui.InputText("New label", ref newName, 200)) {
                data.Context.GetChildByValue<string>()!.target = newName;
            }
            if (newName != selected.Label && ImGui.Button("Confirm rename")) {
                selected.Label = newName;
                data.Context.Changed = true;
                if (workspace.CurrentBundle != null && workspace.CurrentBundle.RecordEntity(selected) == Bundle.EntityRecordUpdateType.Addded) {
                    Logger.Info($"Entity {selected.Label} added to current bundle {workspace.CurrentBundle.Name}");
                }
            }
            ImGui.TreePop();
        }

        ImGui.Separator();
        if (ImGui.Button("Duplicate")) {
            selected = workspace.ResourceManager.CreateEntity(selected.Type, selected.Id);
            data.Context.children.Clear();
            data.SetPersistentData("selectedEntity", selected.Id);
        }

        var child = data.Context.GetChildByValue<ResourceEntity>();
        if (child == null) {
            child = data.Context.AddChild("selected", selected);
            WindowHandlerFactory.CreateResourceEntityHandler(child);
        }

        if (child.Changed && workspace.CurrentBundle == null) {
            ImGui.TextColored(Colors.Warning, "No active bundle. Changes can't be saved. Create a bundle please.");
        }
        child.ShowUI();
        if (child.Changed && workspace.CurrentBundle != null) {
            if (workspace.CurrentBundle.RecordEntity(selected) == Bundle.EntityRecordUpdateType.Addded) {
                Logger.Info($"Entity {selected.Label} added to current bundle {workspace.CurrentBundle.Name}");
            }
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}