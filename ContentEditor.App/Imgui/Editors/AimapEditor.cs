using ContentPatcher;
using ReeLib;
using ReeLib.Aimp;

namespace ContentEditor.App.ImguiHandling;

public class AimapEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "AI Map";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public AimpFile File => Handle.GetFile<AimpFile>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public AimapEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild<AimpFile, AimpHeader>("Header", File, getter: (f) => f!.Header).AddDefaultHandler();
            context.AddChild<AimpFile, RSZFile>("RSZ Data", File, getter: (f) => f!.RSZ).AddDefaultHandler();
            context.AddChild<AimpFile, ContentGroupContainer>("Main Content", File, getter: (f) => f!.mainContent, setter: (f, v) => f.mainContent = v).AddDefaultHandler<ContentGroupContainer>();
            context.AddChild<AimpFile, ContentGroupContainer>("Secondary Content", File, getter: (f) => f!.secondaryContent, setter: (f, v) => f.secondaryContent = v).AddDefaultHandler<ContentGroupContainer>();
            context.AddChild<AimpFile, MapLayers[]>("Map Layers", File, getter: (f) => f!.layers, setter: (f, v) => f.layers = v).AddDefaultHandler<MapLayers[]>();
            context.AddChild<AimpFile, Guid>("Parent Map ID", File, getter: (f) => f!.parentMapId.guid, setter: (f, v) => f.parentMapId.guid = v).AddDefaultHandler<Guid>();
            context.AddChild<AimpFile, EmbeddedMap[]>("Embeds", File, getter: (f) => f!.embeds, setter: (f, v) => f.embeds = v ?? []).AddDefaultHandler<EmbeddedMap[]>();
        }

        context.ShowChildrenUI();
    }

    protected override void OnModifiedChanged(bool changed)
    {
        base.OnModifiedChanged(changed);
        Reset();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(AimpHeader))]
public class AimpHeaderEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var rset = context.Get<AimpHeader>();
        if (context.children.Count == 0) {
            context.AddChild<AimpHeader, string>("Name", rset, getter: (i) => i!.name, setter: (i, v) => i.name = v).AddDefaultHandler<string>();
            context.AddChild<AimpHeader, string>("Hash", rset, getter: (i) => i!.hash, setter: (i, v) => i.hash = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<AimpHeader, Guid>("Map ID", rset, getter: (i) => i!.mapId.guid, setter: (i, v) => i.mapId.guid = v).AddDefaultHandler<Guid>();
            context.AddChild<AimpHeader, MapType>("Map Type", rset, getter: (i) => i!.mapType, setter: (i, v) => i.mapType = v).AddDefaultHandler<MapType>();
            context.AddChild<AimpHeader, SectionType>("Section Type", rset, getter: (i) => i!.sectionType, setter: (i, v) => i.sectionType = v).AddDefaultHandler<SectionType>();
            context.AddChild<AimpHeader, float>("Agent Rad When Build", rset, getter: (i) => i!.agentRadWhenBuild, setter: (i, v) => i.agentRadWhenBuild = v).AddDefaultHandler<float>();
            context.AddChild<AimpHeader, MapStructure>("Map Structure", rset, getter: (i) => i!.mapStructure, setter: (i, v) => i.mapStructure = v).AddDefaultHandler<MapStructure>();
        }

        context.ShowChildrenUI();
    }
}
[ObjectImguiHandler(typeof(NodeData))]
public class NodeDataEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var instance = context.Get<NodeData>();
            context.AddChild<NodeData, List<NodeInfo>>("Node Info", instance, getter: (i) => i!.Nodes, setter: (i, v) => i.Nodes = v!).AddDefaultHandler<List<NodeInfo>>();
        }

        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(NodeInfo))]
public class NodeInfoHandler : IObjectUIHandler, IUIContextEventHandler
{
    public bool HandleEvent(UIContext context, EditorUIEvent eventData)
    {
        if (eventData.IsChangeFromChild) {
            var parentWindow = context.FindHandlerInParents<ObjectInspector>()?.ParentWindow;
            if (parentWindow is ISceneEditor sceneEditor && sceneEditor.GetScene()?.Root.ActiveEditMode is NavmeshEditMode navmeshEditor) {
                (navmeshEditor.Target as AIMap)?.ResetPreviewGeometry();
            }
        }
        return true;
    }

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var instance = context.Get<NodeInfo>();
            context.AddChild<NodeInfo, int>("Index", instance, getter: (i) => i!.index).AddDefaultHandler();
            context.AddChild<NodeInfo, int>("Group Index", instance, getter: (i) => i!.groupIndex).AddDefaultHandler();
            context.AddChild<NodeInfo, int>("Local Index", instance, getter: (i) => i!.localIndex).AddDefaultHandler();
            context.AddChild<NodeInfo, NodeInfoFlags>("Flags", instance, getter: (i) => i!.flags, setter: (i, v) => i.flags = v).AddDefaultHandler();
            // TODO show exactly the attributes that are actually present in the file - how do we get the file :)?
            context.AddChild<NodeInfo, GenericFlagsU64>("Attributes", instance, getter: (i) => (GenericFlagsU64)i!.attributes, setter: (i, v) => i.attributes = (ulong)v).AddDefaultHandler();

            var ws = context.GetWorkspace();
            if (ws?.Env.TypeCache.GetSubclasses("via.navigation.map.NodeUserData").Any(x => x != "via.navigation.map.NodeUserData") == true) {
                context.AddChild<NodeInfo, RszInstance>(
                    "User Data",
                    instance,
                    new NestedUIHandlerStringSuffixed(new RszClassnamePickerHandler("via.navigation.map.NodeUserData")),
                    (i) => i!.UserData,
                    setter: (i, v) => i.UserData = v);
            }

            context.AddChild<NodeInfo, List<LinkInfo>>("Links", instance, getter: (i) => i!.Links).AddDefaultHandler();
            context.AddChild<NodeInfo, List<NodeInfo?>>("Pair Nodes", instance, getter: (i) => i!.PairNodes).AddDefaultHandler();
            context.AddChild("Info", null, new FixedLabelHandler(Lang.EditMode.Navmesh_NodeEditWarning, Colors.Info));
        }

        if (context.parent?.uiHandler is InspectorComponentLinkHandler) {
            context.ShowChildrenUI();
        } else {
            context.ShowChildrenNestedUI();
        }
    }
}
