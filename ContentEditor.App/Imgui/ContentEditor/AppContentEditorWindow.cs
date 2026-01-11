using ContentEditor.App.DD2;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

public class AppContentEditorWindow : IWindowHandler, IWorkspaceContainer
{
    public bool HasUnsavedChanges => throw new NotImplementedException();
    public string HandlerName => "Content Editor";

    public readonly GameIdentifier game;

    public ContentWorkspace Workspace { get; }
    protected UIContext context = null!;

    private static readonly Dictionary<string, List<KeyValuePair<string, Func<ContentWorkspace, IWindowHandler>>>> DefinedWindows = [];

    public AppContentEditorWindow(ContentWorkspace workspace)
    {
        this.game = workspace.Env.Config.Game;
        Workspace = workspace;
    }

    public static void RegisterWindow(GameIdentifier game, string key, Func<ContentWorkspace, IWindowHandler> factory)
    {
        if (!DefinedWindows.TryGetValue(game.name, out var list)) {
            DefinedWindows[game.name] = list = new ();
        }
        list.Add(new KeyValuePair<string, Func<ContentWorkspace, IWindowHandler>>(key, factory));
    }

    public void Init(UIContext context)
    {
        this.context = context;
    }

    private void SetupEditors()
    {
        foreach (var (typename, v) in Workspace.Config.Entities) {
            RegisterWindow(GameIdentifier.dd2, typename, (ws) => new EntityEditor(ws, typename));
        }
    }

    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        if (Workspace.Config.EntityHierarchy.Count == 0) {
            ImGui.TextColored(Colors.Warning, "No content editor entities defined for " + Workspace.Game);
            return;
        }

        if (!DefinedWindows.ContainsKey(game.name))
            SetupEditors();

        if (Workspace.CurrentBundle == null) {
            ImGui.TextColored(Colors.Warning, "No bundle selected. Changes will not be saveable. Select or create a new bundle first.");
        }
        var data = context.Get<WindowData>();
        var selectedTabIndexes = data.GetOrAddPersistentClass<List<int>>("tabIndex");
        var curLevelList = Workspace.Config.EntityHierarchy;
        string? name = null;
        EntityConfig? type = null;
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
            if (cur.Item1 is EntityConfig conf) {
                type = conf;
                break;
            } else {
                curLevelList = (EntityTypeList)cur.Item1;
            }
            i++;
        }

        if (type == null || name == null) return;
        if (type is EntityConfig cfg) {
            data.Context ??= UIContext.CreateRootContext("ContentEditor", this);
            var tab = data.GetOrAddSubwindow(name);
            if (tab.Handler == null) {
                tab.Handler = new EntityEditor(Workspace, name);
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