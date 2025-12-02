using System.Reflection;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Clip;
using ReeLib.Tml;

namespace ContentEditor.App.ImguiHandling;

public class TmlEditor<TClipFileType> : FileEditor, IWorkspaceContainer, IObjectUIHandler where TClipFileType : TmlFile
{
    public ContentWorkspace Workspace { get; }
    public TClipFileType File { get; private set; }

    public override string HandlerName => typeof(TClipFileType) == typeof(ClipFile) ? "Clip" : "Timeline";

    static TmlEditor()
    {
        // ensure property/key constructors are set up
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MotlistEditor).TypeHandle);
        WindowHandlerFactory.DefineInstantiator<TimelineTrack>((ctx) => new TimelineTrack(ctx.FindValueInParentValues<TmlEditor<TClipFileType>>()?.File.Header.version ?? ClipVersion.MHWilds));
    }

    public TmlEditor(ContentWorkspace env, FileHandle file) : base(file)
    {
        Workspace = env;
        File = file.GetFile<TClipFileType>();
    }

    protected override void OnFileReverted()
    {
        context.ClearChildren();
        base.OnFileReverted();
        File = Handle.GetFile<TClipFileType>();
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild("GUID", File, TmlFileObjectHandler.Instance);
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext context)
    {
        OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(TmlFile), Stateless = true)]
public class TmlFileObjectHandler : IObjectUIHandler
{
    public static readonly TmlFileObjectHandler Instance = new();
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<TmlFile>();
            context.AddChild<TmlFile, Guid>("GUID", file, getter: (m) => m!.Header.guid, setter: (m, n) => m.Header.guid = n).AddDefaultHandler();
            context.AddChild<TmlFile, List<TimelineTrack>>("Tracks", file, new ListHandler(typeof(TimelineTrack), typeof(List<TimelineTrack>)), (m) => m!.Tracks);
            context.AddChild<TmlFile, List<TmlNodeGroup>>("Node Groups", file, new ListHandler(typeof(TmlNodeGroup), typeof(List<TmlNodeGroup>)), (m) => m!.NodeGroups);

            context.AddChild<TmlFile, List<SpeedPointData>>("Speed Point Data", file, new ListHandler(typeof(SpeedPointData), typeof(List<SpeedPointData>)), (m) => m!.SpeedPointData);
            context.AddChild<TmlFile, List<HermiteInterpolationData>>("Hermite Interpolation Data", file, new ListHandler(typeof(HermiteInterpolationData), typeof(List<HermiteInterpolationData>)), (m) => m!.HermiteData);
            context.AddChild<TmlFile, List<Bezier3DKeys>>("Bezier3D Interpolation Data", file, new ListHandler(typeof(Bezier3DKeys), typeof(List<Bezier3DKeys>)), (m) => m!.Bezier3DData);
            context.AddChild<TmlFile, List<ClipInfoStruct>>("Clip Info", file, new ListHandler(typeof(ClipInfoStruct), typeof(List<ClipInfoStruct>)), (m) => m!.ClipInfo);

            // context.AddChild<TmlFile, List<TimelineNode>>("Nodes", file, new ListHandler(typeof(TimelineNode), typeof(List<TimelineNode>)), (m) => m!.RootNodes);
            // context.AddChild<TmlFile, List<Property>>("Properties", file, new ListHandler(typeof(Property), typeof(List<Property>)), (m) => m!.Properties);
            // context.AddChild<TmlFile, List<Key>>("Keys", file, new ListHandler(typeof(Key), typeof(List<Key>)), (m) => m!.Keys);
        }
        context.ShowChildrenUI();
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
