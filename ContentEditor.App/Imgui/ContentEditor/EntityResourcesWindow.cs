using ContentEditor.App.DD2;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

public class EntityResourcesWindow : IWindowHandler, IWorkspaceContainer
{
    public bool HasUnsavedChanges => throw new NotImplementedException();
    public string HandlerName => "Resource Editor";

    public readonly GameIdentifier game;

    public ContentWorkspace Workspace { get; }
    protected UIContext context = null!;

    public EntityResourcesWindow(ContentWorkspace workspace)
    {
        this.game = workspace.Env.Config.Game;
        Workspace = workspace;
    }

    public void Init(UIContext context)
    {
        this.context = context;
    }

    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        if (Workspace.Config.ResourceHierarchy.Count == 0) {
            ImGui.TextColored(Colors.Warning, "No entity resources defined for " + Workspace.Game);
            return;
        }

        if (Workspace.CurrentBundle == null) {
            ImGui.TextColored(Colors.Warning, "No bundle selected. Changes will not be saveable. Select or create a new bundle first.");
        }
        var data = context.Get<WindowData>();
        var selectedTabIndexes = data.GetOrAddPersistentClass<List<int>>("tabIndex");
        var curLevelList = Workspace.Config.ResourceHierarchy;
        string? name = null;
        ResourceConfig? type = null;
        int i = 0;
        while (curLevelList != null) {
            int index = i >= selectedTabIndexes.Count ? -1 : selectedTabIndexes[i];
            if (ImguiHelpers.Tabs(curLevelList.FriendlyNames, ref index)) {
                selectedTabIndexes.RemoveAtAfter(i);
                if (i >= selectedTabIndexes.Count) {
                    selectedTabIndexes.Add(index);
                } else {
                    selectedTabIndexes[i] = index;
                }
                data.SetPersistentData("tabIndex", selectedTabIndexes);
            }
            if (index == -1) {
                selectedTabIndexes.RemoveAtAfter(i);
                break;
            }
            var cur = curLevelList.Get(index);
            name = cur.name;
            if (cur.Item1 is ResourceConfig conf) {
                type = conf;
                break;
            } else {
                curLevelList = (EntityTypeList<ResourceConfig>)cur.Item1;
            }
            i++;
        }

        if (type == null || name == null) return;
        if (type is ResourceConfig cfg) {
            data.Context ??= UIContext.CreateRootContext("EntityResources", this);
            var tab = data.GetOrAddSubwindow(name, true);
            if (tab.Handler == null) {
                tab.Handler = new ResourceEditor(Workspace, name);
                tab.Handler.Init(tab.Context!);
            }

            ImGui.Spacing();
            ImGui.Indent(2);
            tab.Handler.OnIMGUI();
            ImGui.Unindent(2);
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}