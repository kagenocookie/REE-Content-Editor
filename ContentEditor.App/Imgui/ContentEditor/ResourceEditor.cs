using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;

namespace ContentEditor.App;

public class ResourceEditor : IWindowHandler, IObjectUIHandler
{
    public string HandlerName => nameof(ResourceEditor);
    public bool HasUnsavedChanges => data?.Context?.GetChildByValue<IContentResource>()?.Changed == true;
    private long initialId = -1;

    public ResourceEditor(ContentWorkspace workspace, string entityType, long id = -1)
    {
        this.workspace = workspace;
        this.resourceType = entityType;
        initialId = id;
    }

    private ContentWorkspace workspace;
    private readonly string resourceType;
    private WindowData data = new();
    protected UIContext context = null!;

    public long SelectedResourceId {
        get => data.GetOrAddPersistentData<long>("selectedResource", -1);
        set => data.SetPersistentData<long>("selectedResource", value);
    }

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
        if (initialId != -1) {
            data.SetPersistentData("selectedResource", initialId);
        }
    }

    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI() => OnIMGUI(data.Context);
    public void OnIMGUI(UIContext context)
    {
        if (workspace == null) {
            ImGui.TextColored(Colors.Warning, "Couldn't get game configuration");
            return;
        }

        if (context == null) {
            ImGui.TextColored(Colors.Error, "Missing UI container");
            return;
        }

        var instances = workspace.ResourceManager.GetResourceInstances(resourceType);
        var selectedId = SelectedResourceId;

        if (FilterableResourceCombo("Resource"u8, instances, ref selectedId, ref context.Filter)) {
            SelectedResourceId = selectedId;
            // note: we can clear children safely, any changes are still stored in the resource manager
            // just gotta figure out how to keep those changes tracked in bundle
            context.ClearChildren();
        }

        if (selectedId == -1) {
            return;
        }

        var selected = workspace.ResourceManager.GetActiveResourceInstance(resourceType, selectedId);
        if (selected == null) {
            ImGui.TextColored(Colors.Warning, "Selected object could not be found");
            return;
        }

        if (ImGui.BeginPopupContextItem(resourceType)) {
            if (ImGui.Button("Reopen in new window")) {
                EditorWindow.CurrentWindow?.AddSubwindow(new ResourceEditor(workspace, selected.ResourceTypeID, selectedId));
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        ImGui.Separator();
        if (ImGui.Button("Duplicate")) {
            (selectedId, selected) = workspace.ResourceManager.CreateResource(resourceType, ResourceState.Active, selected);
            context.children.Clear();
            SelectedResourceId = selectedId;
        }

        var child = context.GetChildByValue<IContentResource>();
        if (child == null) {
            child = context.AddChild("selected", selected, new ResourceDisplayHandler());
        }

        if (child.Changed && workspace.CurrentBundle == null) {
            ImGui.TextColored(Colors.Warning, "No active bundle. Changes can't be saved. Create a bundle please.");
        }
        child.ShowUI();
    }

    private static bool FilterableResourceCombo<TEntityBaseType>(ReadOnlySpan<byte> label, IEnumerable<KeyValuePair<long, TEntityBaseType>> entities, ref long selected, ref string filter)
        where TEntityBaseType : IContentResource
    {
        var labels = entities.Select(kv => kv.Key + ": " + kv.Value.Label).ToArray();
        var values = entities.Select(kv => kv.Key).ToArray();
        return ImguiHelpers.FilterableCombo(label, labels, values, ref selected, ref filter);
    }

    public bool RequestClose()
    {
        return false;
    }
}