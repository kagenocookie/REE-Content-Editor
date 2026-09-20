using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;

namespace ContentEditor.App;

public class EntitySelection : IWindowHandler
{
    public string HandlerName => nameof(EntitySelection);
    public bool HasUnsavedChanges => data?.Context?.GetChildByValue<Entity>()?.Changed == true;
    private long initialId = -1;

    public EntitySelection(ContentWorkspace workspace, string entityType)
    {
        this.workspace = workspace;
        this.entityType = entityType;
    }

    public EntitySelection(ContentWorkspace workspace, Entity initialEntity)
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
            SelectedEntityId = initialId;
        }
    }
    private bool currentBundleOnly;
    private bool showCreateSettings;
    private bool showCustomTemplates = true;
    private bool showUserTemplates = true;
    private TemplateItem? selectedCreateTemplate;
    private string templateFilter = "";
    private string newTemplateName = "";

    public long SelectedEntityId {
        get => data.GetOrAddPersistentData<long>("selectedEntity", workspace.ResourceManager.GetEntityZeroId(entityType));
        set => data.SetPersistentData("selectedEntity", value);
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
        var entityConfig = workspace.ResourceManager.GetEntityConfig(entityType);
        var canCreate = workspace.CurrentBundle != null && (entityConfig?.AllowCreateEmpty == true || entityConfig?.AllowTemplates == true); // TODO + verify has custom id range?
        var selectedId = SelectedEntityId;
        var selected = selectedId == -1 ? null : workspace.ResourceManager.GetActiveEntityInstance(entityType, selectedId);

        var pfx = ImguiHelpers.InlinePrefix();
        ImGui.BeginDisabled(workspace.CurrentBundle == null || workspace.CurrentBundle?.Entities.Any(e => e.Type == entityType) != true);
        ImguiHelpers.ToggleButton($"{AppIcons.Star}", ref currentBundleOnly, Colors.IconActive);
        if (currentBundleOnly) {
            instances = instances.Where(ii => ii.Key == selectedId || workspace.CurrentBundle?.ContainsEntity(ii.Value) == true);
        }
        ImguiHelpers.Tooltip("Show only active bundle entities"u8);
        ImGui.SameLine();
        ImGui.EndDisabled();

        ImGui.BeginDisabled(workspace.CurrentBundle == null || selected == null);
        if (ImGui.Button($"{AppIcons.SI_Copy}") && selected != null) {
            selected = workspace.ResourceManager.CreateEntity(selected.Type, selected.ToJson(workspace.Env));
            data.Context.children.Clear();
            SelectedEntityId = selected.Id;
        }
        ImguiHelpers.Tooltip(Lang.Buttons.Duplicate);
        ImGui.EndDisabled();
        ImGui.SameLine();

        bool doCreate = false;
        if (canCreate && entityConfig != null) {
            if (entityConfig.AllowTemplates) {
                ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericAdd}", ref showCreateSettings, Colors.IconActive);
            } else {
                showCreateSettings = false;
                doCreate = ImGui.Button($"{AppIcons.SI_GenericAdd}");
            }
            ImguiHelpers.Tooltip(Lang.Buttons.Create);
            ImGui.SameLine();
        }

        if (ImGui.Button($"{AppIcons.SI_WindowOpenNew}")) {
            if (selected != null) {
                EditorWindow.CurrentWindow!.AddSubwindow(new HandlerEmbedWindow(EntityHandler.Instance, selected));
            }
        }
        ImguiHelpers.Tooltip("Open entity in separate window");
        pfx.Dispose();

        if (ImguiHelpers.FilterableEntityCombo("Entity"u8, instances, ref selectedId, ref data.Context.Filter)) {
            SelectedEntityId = selectedId;
            // note: we can clear children safely, any changes are still stored in the resource manager
            // just gotta figure out how to keep those changes tracked in bundle
            data.Context.ClearChildren();
            selected = selectedId == -1 ? null : workspace.ResourceManager.GetActiveEntityInstance(entityType, selectedId);
        }

        if (selected != null && ImGui.BeginPopupContextItem(entityType)) {
            if (ImGui.Selectable("Change label")) {
                data.Context.AddChild("Rename", selected.Label);
            }
            ImGui.EndPopup();
        }

        if (entityConfig != null && canCreate && showCreateSettings) {
            ImGui.Spacing();
            ImguiHelpers.BeginRect();

            if (entityConfig.AllowTemplates) {
                var prefix = ImguiHelpers.InlinePrefix();
                ImGui.BeginDisabled(selected == null);
                if (ImGui.Button(Lang.Buttons.CreateTemplate) && selected != null && !string.IsNullOrEmpty(newTemplateName)) {
                    if (TemplateManager.Instance.TemplateExists(workspace.Game, entityType, newTemplateName)) {
                        Logger.Error($"Template {newTemplateName} already exists");
                    } else {
                        var json = selected.GetDataJson(workspace.Env);
                        TemplateManager.Instance.AddTemplate(workspace.Game, entityType, newTemplateName, json);
                        newTemplateName = "";
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button($"{AppIcons.SI_FolderLink}")) {
                    FileSystemUtils.ShowFileInExplorer(TemplateManager.GetUserTemplatesFolder(workspace.Game, true));
                }
                ImguiHelpers.Tooltip(Lang.Buttons.OpenTemplateFolder);
                prefix.Dispose();
                ImGui.InputText(Lang.General.NewTemplateName, ref newTemplateName, 100);
                ImGui.EndDisabled();

                prefix = ImguiHelpers.InlinePrefix();
                if (ImGui.Button($"{AppIcons.SI_Update}")) {
                    TemplateManager.Instance.ReloadTemplates(workspace.Game);
                }
                ImguiHelpers.Tooltip(Lang.Buttons.RefreshList);
                prefix.Dispose();

                var templates = !entityConfig.AllowTemplates ? default : TemplateManager.Instance.GetTemplatesForGui(workspace.Game, entityType, showCustomTemplates, showUserTemplates);
                if (templates.labels.Length > 0) {
                    // var names = templates.Select(t => t.Name).Prepend("<blank>").ToArray();
                    ImguiHelpers.FilterableCombo("Template"u8, templates.labels, templates.options, ref selectedCreateTemplate, ref templateFilter);
                } else if (entityConfig.AllowTemplates && !entityConfig.AllowCreateEmpty) {
                    ImGui.TextColored(Colors.Info, "No templates yet defined for this entity type. Duplicate or create a new template from an existing one first.");
                } else {
                    ImGui.Dummy(new Vector2(1, 1));
                }
            }

            if (selectedCreateTemplate == null) {
                using var _ = ImguiHelpers.Disabled(!entityConfig.AllowCreateEmpty);
                doCreate = entityConfig.AllowCreateEmpty && ImGui.Button(Lang.Buttons.CreateWithIcon);
            } else {
                doCreate = entityConfig.AllowTemplates && ImGui.Button(Lang.Buttons.CreateWithIcon);
            }

            ImguiHelpers.EndRect();
            ImGui.Spacing();
        }

        if (doCreate) {
            selected = workspace.ResourceManager.CreateEntity(entityType, selectedCreateTemplate?.Data ?? new System.Text.Json.Nodes.JsonObject());
            SelectedEntityId = selected.Id;
            workspace.CurrentBundle!.RecordEntity(selected);
            data.Context.ClearChildren();
        }

        if (selected == null) {
            if (selectedId != 0) {
                ImGui.TextColored(Colors.Warning, "Selected entity could not be found");
            }
            return;
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
                if (workspace.CurrentBundle != null && workspace.CurrentBundle.RecordEntity(selected) == Bundle.EntityRecordUpdateType.Added) {
                    Logger.Info($"Entity {selected.Label} added to current bundle {workspace.CurrentBundle.Name}");
                }
                data.Context.RemoveChild(renameCtx);
            }
        }

        ImGui.Separator();

        var child = data.Context.GetChildByValue<ResourceEntity>();
        if (child == null) {
            child = data.Context.AddChild("selected", selected);
            WindowHandlerFactory.CreateEntityHandler(child);
        }

        if (child.Changed && workspace.CurrentBundle == null) {
            ImGui.TextColored(Colors.Warning, "No active bundle. Changes can't be saved. Create a bundle please.");
        }
        child.ShowUI();
        if (child.Changed && workspace.CurrentBundle != null) {
            if (workspace.CurrentBundle.RecordEntity(selected) == Bundle.EntityRecordUpdateType.Added) {
                Logger.Info($"Entity {selected.Label} added to current bundle {workspace.CurrentBundle.Name}");
            }
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}