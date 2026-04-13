using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ContentPatcher;
using ReeLib;
using ReeLib.Clip;
using ReeLib.Tml;

namespace ContentEditor.App.ImguiHandling;

public class ClipFileEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public ContentWorkspace Workspace { get; }
    public ClipFile File { get; private set; }

    public override string HandlerName => Handle.Format.format switch {
        KnownFileFormats.Timeline => "Timeline",
        KnownFileFormats.UserCurve => "UserCurve",
        _ => "Clip",
    };

    static ClipFileEditor()
    {
        // ensure property/key constructors are set up
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MotlistEditor).TypeHandle);
        WindowHandlerFactory.DefineInstantiator<TimelineTrackGroup>((ctx) => new TimelineTrackGroup(ctx.FindValueInParentValues<ClipFileEditor>()?.File.Header.version ?? ClipVersion.MHWilds));
        WindowHandlerFactory.DefineInstantiator<TimelineTrack>((ctx) => new TimelineTrack(ctx.FindValueInParentValues<ClipFileEditor>()?.File.Header.version ?? ClipVersion.MHWilds));
        WindowHandlerFactory.DefineInstantiator<TimelineClipGroup>((ctx) => new TimelineClipGroup(ctx.FindValueInParentValues<ClipFileEditor>()?.File.Header.version ?? ClipVersion.MHWilds));
    }

    public ClipFileEditor(ContentWorkspace env, FileHandle file) : base(file)
    {
        Workspace = env;
        File = file.GetFile<ClipFile>();
    }

    protected override void OnFileReverted()
    {
        context.ClearChildren();
        base.OnFileReverted();
        File = Handle.GetFile<ClipFile>();
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild("Clip Data", File, TmlFileObjectHandler.Instance);
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext context)
    {
        OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(ClipFile), Stateless = true)]
public class TmlFileObjectHandler : IObjectUIHandler
{
    public static readonly TmlFileObjectHandler Instance = new();
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<ClipFile>();
            context.AddChild<ClipFile, Guid>("GUID", file, getter: (m) => m!.Header.guid, setter: (m, n) => m.Header.guid = n).AddDefaultHandler();
            context.AddChild<ClipFile, List<TimelineTrackGroup>>("Track Groups", file, new ListHandler(typeof(TimelineTrackGroup), typeof(List<TimelineTrackGroup>)), (m) => m!.TrackGroups);

            context.AddChild<ClipFile, List<SpeedPointData>>("Speed Point Data", file, new ListHandler(typeof(SpeedPointData), typeof(List<SpeedPointData>)), (m) => m!.Clip.SpeedPointData);
            context.AddChild<ClipFile, List<Bezier3DKeys>>("Bezier3D Interpolation Data", file, new ListHandler(typeof(Bezier3DKeys), typeof(List<Bezier3DKeys>)), (m) => m!.Clip.Bezier3DData);
            context.AddChild<ClipFile, List<ClipInfoStruct>>("Clip Info", file, new ListHandler(typeof(ClipInfoStruct), typeof(List<ClipInfoStruct>)), (m) => m!.Clip.ClipInfoList);

            // context.AddChild<ClipFile, List<NormalKey>>("Keys", file, getter: (m) => m!.Clip.NormalKeys).AddDefaultHandler();
        }
        context.ShowChildrenUI();
    }
}


[ObjectImguiHandler(typeof(TimelineClipGroup))]
public class TmlNodeGroupHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(TimelineClipGroup).GetField(nameof(TimelineClipGroup.Name))!,
        typeof(TimelineClipGroup).GetProperty(nameof(TimelineClipGroup.Tracks))!,
    ];

    private static MemberInfo[] DisplayedFieldsTrackOnly = [
        typeof(TimelineClipGroup).GetProperty(nameof(TimelineClipGroup.Tracks))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            var version = context.FindHandlerInParents<ClipFileEditor>()?.File.Header.version;
            if (version < ClipVersion.RE8) {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(TimelineClipGroup), false, DisplayedFieldsTrackOnly);
            } else {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(TimelineClipGroup), false, DisplayedFields);
            }
        }

        if (context.children.Count == 1) {
            context.ShowChildrenUI();
        } else {
            context.children[0].ShowUI(); // name
            var group = context.Get<TimelineClipGroup>();
            var vec = new Vector3(group.startFrame, group.endFrame, group.frameCount);
            if (ImGui.DragFloat3("Frame Range"u8, ref Unsafe.As<Vector3, float>(ref vec))) {
                if (vec.Z == group.frameCount) {
                    vec.Z = group.endFrame - group.startFrame;
                }
                UndoRedo.RecordCallbackSetter(context, group, new Vector3(group.startFrame, group.endFrame, group.frameCount), vec, (o, v) => {
                    o.startFrame = v.X;
                    o.endFrame = v.Y;
                    o.frameCount = v.Z;
                }, $"{group.GetHashCode()}_frameRange");
            }
            context.children[1].ShowUI(); // tracks
        }
    }
}

[ObjectImguiHandler(typeof(TimelineTrackGroup))]
public class TimelineTrackHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(TimelineTrackGroup).GetField(nameof(TimelineTrackGroup.name))!,
        typeof(TimelineTrackGroup).GetField(nameof(TimelineTrackGroup.type))!,
        typeof(TimelineTrackGroup).GetField(nameof(TimelineTrackGroup.ukn))!,
        typeof(TimelineTrackGroup).GetProperty(nameof(TimelineTrackGroup.ClipGroups))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(TimelineTrackGroup), false, DisplayedFields);
        }

        context.ShowChildrenNestedUI();
    }
}

[ObjectImguiHandler(typeof(TimelineTrack))]
public class TimelineNodeHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(TimelineTrack).GetProperty(nameof(TimelineTrack.Name))!,
        typeof(TimelineTrack).GetField(nameof(TimelineTrack.Tag))!,
        typeof(TimelineTrack).GetField(nameof(TimelineTrack.startFrame))!,
        typeof(TimelineTrack).GetField(nameof(TimelineTrack.endFrame))!,
        typeof(TimelineTrack).GetField(nameof(TimelineTrack.guid1))!,
        typeof(TimelineTrack).GetField(nameof(TimelineTrack.guid2))!,
        typeof(TimelineTrack).GetField(nameof(TimelineTrack.nodeType))!,
        typeof(TimelineTrack).GetField(nameof(TimelineTrack.uknByte))!,
        typeof(TimelineTrack).GetField(nameof(TimelineTrack.uknByte2))!,
        typeof(TimelineTrack).GetProperty(nameof(TimelineTrack.TimelineChildTracks))!,
        typeof(TimelineTrack).GetProperty(nameof(TimelineTrack.Properties))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(TimelineTrack), false, DisplayedFields);
        }

        if (AppImguiHelpers.CopyableTreeNode<TimelineTrack>(context)) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}
