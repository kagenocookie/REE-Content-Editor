using System.Numerics;
using System.Text.Json.Nodes;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Widgets;
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

    private class CreateData(EntityConfig config, JsonObject data, string type, UIContext parentContext)
    {
        public DynamicInputHandler? handler = WindowHandlerFactory.CreateNewEntityDynamicInputs(config, data, parentContext);
        public JsonObject data = data;
        public string type = type;
    }

    private CreateData? createData;

    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        if (workspace == null || data.Context == null) {
            // this shouldn't really happen, but just as a sanity check
            ImGui.TextColored(Colors.Warning, Lang.Errors.MissingWorkspace);
            return;
        }

        var instances = workspace.ResourceManager.GetEntityInstances(entityType);
        var entityConfig = workspace.ResourceManager.GetEntityConfig(entityType);
        var canCreate = workspace.CurrentBundle != null && (entityConfig?.AllowCreateEmpty == true || entityConfig?.AllowTemplates == true); // TODO + verify has custom id range?
        var selectedId = SelectedEntityId;
        var selected = selectedId == -1 ? null : workspace.ResourceManager.GetActiveEntityInstance(entityType, selectedId);
        if (selectedId == -1 && instances.Count() == 1 && selected == null) {
            selectedId = instances.First().Key;
            selected = workspace.ResourceManager.GetActiveEntityInstance(entityType, selectedId);
        }

        var pfx = ImguiHelpers.InlinePrefix();
        ImGui.BeginDisabled(workspace.CurrentBundle == null || workspace.CurrentBundle?.Entities.Any(e => e.Type == entityType) != true);
        ImguiHelpers.ToggleButton($"{AppIcons.Star}", ref currentBundleOnly, Colors.IconActive);
        if (currentBundleOnly) {
            instances = instances.Where(ii => ii.Key == selectedId || workspace.CurrentBundle?.ContainsEntity(ii.Value) == true);
        }
        ImguiHelpers.Tooltip(Lang.Entities.ShowActiveBundleOnly);
        ImGui.SameLine();
        ImGui.EndDisabled();

        if (canCreate) {
            ImGui.BeginDisabled(workspace.CurrentBundle == null || selected == null);
            if (ImGui.Button($"{AppIcons.SI_Copy}") && selected != null) {
                var baseJson = selected.GetDataJson(workspace.Env);
                // var handler = WindowHandlerFactory.CreateNewEntityDynamicInputs(selected.Config, baseJson);
                createData = new CreateData(selected.Config, baseJson, selected.Type, data.Context);
            }
            ImguiHelpers.Tooltip(Lang.Buttons.Duplicate);
            ImGui.EndDisabled();
            ImGui.SameLine();

            if (entityConfig != null) {
                if (entityConfig.AllowTemplates) {
                    ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericAdd}", ref showCreateSettings, Colors.IconActive);
                } else {
                    showCreateSettings = false;
                    if (ImGui.Button($"{AppIcons.SI_GenericAdd}")) {
                        createData = new CreateData(entityConfig, new JsonObject(), entityType, data.Context);
                    }
                }
                ImguiHelpers.Tooltip(Lang.Buttons.Create);
                ImGui.SameLine();
            }
        }

        if (ImGui.Button($"{AppIcons.SI_WindowOpenNew}")) {
            if (selected != null) {
                EditorWindow.CurrentWindow!.AddSubwindow(new HandlerEmbedWindow(EntityHandler.Instance, selected));
            }
        }
        ImguiHelpers.Tooltip(Lang.Entities.OpenInNewWindow);
        pfx.Dispose();

        if (ImguiHelpers.FilterableEntityCombo(Lang.Entities.Entity, instances, ref selectedId, ref data.Context.Filter)) {
            SelectedEntityId = selectedId;
            // note: we can clear children safely, any changes are still stored in the resource manager
            data.Context.ClearChildren();
            selected = selectedId == -1 ? null : workspace.ResourceManager.GetActiveEntityInstance(entityType, selectedId);
        }

        if (selected != null && ImGui.BeginPopupContextItem(entityType)) {
            if (ImGui.Selectable(Lang.Entities.ChangeLabel)) {
                data.Context.AddChild(Lang.Buttons.Rename, selected.Label);
            }
            ImGui.EndPopup();
        }

        if (entityConfig != null && canCreate && showCreateSettings) {
            ImGui.Spacing();
            ImguiHelpers.BeginRect();

            if (entityConfig.AllowTemplates) {
                var prefix = ImguiHelpers.InlinePrefix();
                ImGui.BeginDisabled(selected == null);
                if (ImGui.Button(Lang.Buttons.CreateTemplate)) {
                    if (selected == null || string.IsNullOrEmpty(newTemplateName)) {
                        Logger.Error("Enter a name for the new template!");
                    } else if (TemplateManager.Instance.TemplateExists(workspace.Game, entityType, newTemplateName)) {
                        Logger.Error($"Template {newTemplateName} already exists");
                    } else {
                        var json = selected.GetDataJson(workspace.Env);
                        selectedCreateTemplate = TemplateManager.Instance.AddTemplate(workspace.Game, entityType, newTemplateName, json);
                        newTemplateName = "";
                    }
                }
                prefix.Dispose();
                ImGui.InputText(Lang.General.NewTemplateName, ref newTemplateName, 100);
                ImGui.EndDisabled();

                prefix = ImguiHelpers.InlinePrefix();
                if (ImGui.Button($"{AppIcons.SI_FolderLink}")) {
                    FileSystemUtils.ShowFileInExplorer(TemplateManager.GetUserTemplatesFolder(workspace.Game, true));
                }
                ImguiHelpers.Tooltip(Lang.Buttons.OpenTemplateFolder);
                ImGui.SameLine();
                if (ImGui.Button($"{AppIcons.SI_Update}")) {
                    TemplateManager.Instance.ReloadTemplates(workspace.Game);
                }
                ImguiHelpers.Tooltip(Lang.Buttons.RefreshList);
                prefix.Dispose();

                var templates = !entityConfig.AllowTemplates ? default : TemplateManager.Instance.GetTemplatesForGui(workspace.Game, entityType, showCustomTemplates, showUserTemplates);
                if (templates.labels.Length > 0) {
                    // var names = templates.Select(t => t.Name).Prepend("<blank>").ToArray();
                    ImguiHelpers.FilterableCombo(Lang.Entities.Template, templates.labels, templates.options, ref selectedCreateTemplate, ref templateFilter);
                } else if (entityConfig.AllowTemplates && !entityConfig.AllowCreateEmpty) {
                    ImGui.TextColored(Colors.Info, Lang.Entities.EntityCreateNoTemplates);
                } else {
                    ImGui.Dummy(new Vector2(1, 1));
                }
            }

            if (selectedCreateTemplate == null) {
                using var _ = ImguiHelpers.Disabled(!entityConfig.AllowCreateEmpty);
                if (ImGui.Button(Lang.Buttons.CreateWithIcon) && entityConfig.AllowCreateEmpty) {
                    createData = new CreateData(entityConfig, new JsonObject(), entityType, data.Context);
                }
                if (!entityConfig.AllowCreateEmpty && entityConfig.AllowTemplates) {
                    ImGui.SameLine();
                    ImGui.TextColored(Colors.Note, Lang.Entities.EntityCreateBlankDisallowed);
                }
            } else {
                if (entityConfig.AllowTemplates && ImGui.Button(Lang.Buttons.CreateWithIcon)) {
                    createData = new CreateData(entityConfig, selectedCreateTemplate.Data, entityType, data.Context);
                }
            }

            ImguiHelpers.EndRect();
            ImGui.Spacing();
        }

        if (createData != null) {
            var dlgResult = createData.handler?.ShowDialog();
            if (dlgResult == null || dlgResult == DialogBase.DialogResult.Confirm) {
                try {
                    selected = workspace.ResourceManager.CreateEntity(createData.type, createData.data);
                    data.Context.ClearChildren();
                    SelectedEntityId = selected.Id;
                    showCreateSettings = false;
                    if (data.Context.GetChildByValue<string>() == null) {
                        data.Context.AddChild(Lang.Buttons.Rename, selected.Label);
                    }
                } catch (Exception e) {
                    Logger.Error(e, "Failed to create new entity");
                    createData = null;
                }
            }
            if (dlgResult == null || dlgResult != DialogBase.DialogResult.None) {
                createData = null;
            }
        }

        if (selected == null) {
            if (selectedId != 0) {
                ImGui.TextColored(Colors.Warning, Lang.Entities.EntityNotFound);
            }
            return;
        }

        var renameCtx = data.Context.GetChildByValue<string>();
        if (renameCtx?.Get<string>() != null) {
            ImGui.Indent(16);
            var newName = renameCtx.Get<string>();
            pfx = ImguiHelpers.InlinePrefix();
            ImGui.BeginDisabled(newName == selected.Label || string.IsNullOrEmpty(newName));
            if (ImGui.Button($"{AppIcons.SI_Save}")) {
                selected.Label = newName;
                data.Context.Changed = true;
                selected.Config.UpdateEnums(workspace, selected);
                if (workspace.CurrentBundle != null && workspace.CurrentBundle.RecordEntity(selected) == Bundle.EntityRecordUpdateType.Added) {
                    Logger.Info($"Entity {selected.Label} added to current bundle {workspace.CurrentBundle.Name}");
                }
                data.Context.RemoveChild(renameCtx);
            }
            ImGui.EndDisabled();
            ImguiHelpers.SameLine();
            if (ImGui.Button($"{AppIcons.SI_GenericClose}")) {
                data.Context.RemoveChild(renameCtx);
            }
            pfx.Dispose();
            if (ImGui.InputText(Lang.Entities.NewLabel, ref newName, 200)) {
                data.Context.GetChildByValue<string>()!.target = newName;
            }
            ImGui.Unindent(16);
        }

        ImGui.Separator();

        var child = data.Context.GetChildByValue<ResourceEntity>();
        if (child == null) {
            child = data.Context.AddChild("selected", selected);
            WindowHandlerFactory.CreateEntityHandler(child);
        }

        if (child.Changed && workspace.CurrentBundle == null) {
            ImGui.TextColored(Colors.Warning, Lang.Bundles.NeedBundleToSave);
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