using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;

namespace ContentEditor.App.DD2;

public class EntityEditor : IWindowHandler
{
    public string HandlerName => nameof(EntityEditor);
    public bool HasUnsavedChanges => data?.Context?.GetChildByValue<ResourceEntity>()?.Changed == true;
    private long initialId = -1;

    public EntityEditor(ContentWorkspace workspace, string entityType)
    {
        this.workspace = workspace;
        this.entityType = entityType;
    }

    public EntityEditor(ContentWorkspace workspace, Entity initialEntity)
    {
        this.workspace = workspace;
        this.entityType = initialEntity.Type;
        initialId = initialEntity.Id;
    }

    private ContentWorkspace workspace;
    private readonly string entityType;
    private WindowData data = null!;
    protected UIContext context = null!;

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
        if (initialId != -1) {
            data.SetPersistentData("selectedEntity", initialId);
        }
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

        if (ImguiHelpers.FilterableEntityCombo("Entity", instances, ref selectedId, ref data.Context.Filter)) {
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

        if (ImGui.BeginPopupContextItem(entityType)) {
            if (ImGui.Button("Change label")) {
                data.Context.AddChild("Rename", selected.Label);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Button("Reopen in new window")) {
                EditorWindow.CurrentWindow?.AddSubwindow(new EntityEditor(workspace, selected));
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        var renameCtx = data.Context.GetChildByValue<string>();
        if (renameCtx?.Get<string>() != null) {
            ImGui.Indent(16);
            var newName = renameCtx.Get<string>();
            if (ImGui.InputText("New label", ref newName, 200)) {
                data.Context.GetChildByValue<string>()!.target = newName;
            }
            ImGui.Unindent(16);
            if (ImGui.Button("Cancel rename")) {
                data.Context.RemoveChild(renameCtx);
            }
            if (newName != selected.Label && ImguiHelpers.SameLine() && ImGui.Button("Confirm rename")) {
                selected.Label = newName;
                data.Context.Changed = true;
                selected.Config.PrimaryEnum?.UpdateEnum(workspace, selected);
                if (workspace.CurrentBundle != null && workspace.CurrentBundle.RecordEntity(selected) == Bundle.EntityRecordUpdateType.Addded) {
                    Logger.Info($"Entity {selected.Label} added to current bundle {workspace.CurrentBundle.Name}");
                }
                data.Context.RemoveChild(renameCtx);
            }
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