using System.Reflection;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Clip;
using ReeLib.Tml;

namespace ContentEditor.App.ImguiHandling;

public class TmlEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public ContentWorkspace Workspace { get; }
    public TmlFile File { get; private set; }

    public override string HandlerName => "Motlist";

    static TmlEditor()
    {
        // ensure property/key constructors are set up
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MotlistEditor).TypeHandle);
        WindowHandlerFactory.DefineInstantiator<TimelineTrack>((ctx) => new TimelineTrack(ctx.FindValueInParentValues<TmlEditor>()?.File.Header.version ?? ClipVersion.MHWilds));
    }

    public TmlEditor(ContentWorkspace env, FileHandle file) : base(file)
    {
        Workspace = env;
        File = file.GetFile<TmlFile>();
    }

    protected override void OnFileReverted()
    {
        base.OnFileReverted();
        context.ClearChildren();
        File = Handle.GetFile<TmlFile>();
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild<TmlFile, Guid>("GUID", File, getter: (m) => m!.Header.guid, setter: (m, n) => m.Header.guid = n).AddDefaultHandler();
            context.AddChild<TmlFile, List<TimelineTrack>>("Tracks", File, new ListHandler(typeof(TimelineTrack), typeof(List<TimelineTrack>)), (m) => m!.Tracks);
            context.AddChild<TmlFile, List<TmlNodeGroup>>("Node Groups", File, new ListHandler(typeof(TmlNodeGroup), typeof(List<TmlNodeGroup>)), (m) => m!.NodeGroups);
            // context.AddChild<TmlFile, List<TimelineNode>>("Nodes", File, new ListHandler(typeof(TimelineNode), typeof(List<TimelineNode>)), (m) => m!.RootNodes);
            // context.AddChild<TmlFile, List<Property>>("Properties", File, new ListHandler(typeof(Property), typeof(List<Property>)), (m) => m!.Properties);
            // context.AddChild<TmlFile, List<Key>>("Keys", File, new ListHandler(typeof(Key), typeof(List<Key>)), (m) => m!.Keys);
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext context)
    {
        OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(TmlNodeGroup))]
public class TmlNodeGroupHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(TmlNodeGroup).GetField(nameof(TmlNodeGroup.Name))!,
        typeof(TmlNodeGroup).GetField(nameof(TmlNodeGroup.ukn))!,
        typeof(TmlNodeGroup).GetField(nameof(TmlNodeGroup.frameCount))!,
        typeof(TmlNodeGroup).GetField(nameof(TmlNodeGroup.ukn2))!,
        typeof(TmlNodeGroup).GetField(nameof(TmlNodeGroup.frameCount2))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(TmlNodeGroup), false, DisplayedFields);
        }

        context.ShowChildrenNestedUI();
    }
}

[ObjectImguiHandler(typeof(TimelineTrack))]
public class TimelineTrackHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(TimelineTrack).GetField(nameof(TimelineTrack.name))!,
        typeof(TimelineTrack).GetField(nameof(TimelineTrack.type))!,
        typeof(TimelineTrack).GetField(nameof(TimelineTrack.ukn))!,
        typeof(TimelineTrack).GetProperty(nameof(TimelineTrack.Nodes))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(TimelineTrack), false, DisplayedFields);
        }

        context.ShowChildrenNestedUI();
    }
}

[ObjectImguiHandler(typeof(TimelineNode))]
public class TimelineNodeHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(TimelineNode).GetField(nameof(TimelineNode.Name))!,
        typeof(TimelineNode).GetField(nameof(TimelineNode.Tag))!,
        typeof(TimelineNode).GetField(nameof(TimelineNode.startFrame))!,
        typeof(TimelineNode).GetField(nameof(TimelineNode.endFrame))!,
        typeof(TimelineNode).GetField(nameof(TimelineNode.guid))!,
        typeof(TimelineNode).GetField(nameof(TimelineNode.guid2))!,
        typeof(TimelineNode).GetField(nameof(TimelineNode.nodeType))!,
        typeof(TimelineNode).GetField(nameof(TimelineNode.uknByte))!,
        typeof(TimelineNode).GetField(nameof(TimelineNode.uknByte2))!,
        typeof(TimelineNode).GetProperty(nameof(TimelineNode.ChildNodes))!,
        typeof(TimelineNode).GetProperty(nameof(TimelineNode.Properties))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(TimelineNode), false, DisplayedFields);
        }

        context.ShowChildrenNestedUI();
    }
}
