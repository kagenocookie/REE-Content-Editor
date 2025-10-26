using System.Collections;
using System.Numerics;
using System.Reflection;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.Widgets;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Clip;
using ReeLib.Common;
using ReeLib.Mot;
using ReeLib.Motlist;
using ReeLib.MotTree;

namespace ContentEditor.App.ImguiHandling;

public class MotlistEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public ContentWorkspace Workspace { get; }
    public MotlistFile File { get; private set; }

    public override string HandlerName => "Motlist";

    static MotlistEditor()
    {
        WindowHandlerFactory.DefineInstantiator<EndClipStruct>((ctx) => new EndClipStruct() { Version = ctx.FindValueInParentValues<ClipEntry>()?.Version ?? ClipVersion.MHWilds });
        WindowHandlerFactory.DefineInstantiator<CTrack>((ctx) => new CTrack(ctx.FindValueInParentValues<ClipEntry>()?.Version ?? ClipVersion.MHWilds));
        WindowHandlerFactory.DefineInstantiator<Property>((ctx) => new Property(ctx.FindValueInParentValues<ClipEntry>()?.Version ?? ClipVersion.MHWilds));
        WindowHandlerFactory.DefineInstantiator<Key>((ctx) => new Key(ctx.FindValueInParentValues<ClipEntry>()!.Header.version));
        WindowHandlerFactory.DefineInstantiator<MotIndex>((ctx) => new MotIndex(
            ctx.FindHandlerInParents<MotlistEditor>()?.File.Header.version
            ?? (ctx.GetWorkspace()?.Env.TryGetFileExtensionVersion("motlist", out var v) == true ? (MotlistVersion)v : MotlistVersion.DD2)));
        WindowHandlerFactory.DefineInstantiator<MotPropertyTrack>((ctx) => {
            var editor = ctx.FindHandlerInParents<MotlistEditor>();
            if (editor == null) {
                Logger.Error("Could not found motlist editor context");
                return new MotPropertyTrack() {
                    Version = (ctx.GetWorkspace()?.Env.TryGetFileExtensionVersion("mot", out var vv) == true ? (MotVersion)vv : MotVersion.MHWILDS)
                };
            }

            return new MotPropertyTrack() { Version = editor.File.Header.version.GetMotVersion() };
        });
    }

    public MotlistEditor(ContentWorkspace env, FileHandle file) : base(file)
    {
        Workspace = env;
        File = file.GetFile<MotlistFile>();
    }

    protected override void OnFileReverted()
    {
        base.OnFileReverted();
        context.ClearChildren();
        File = Handle.GetFile<MotlistFile>();
    }

    protected override void DrawFileControls(WindowData data)
    {
        if (Handle.Resource is CommonMeshResource mesh) {
            if (ImGui.Button("Save As Motlist ...")) {
                Workspace.Env.TryGetFileExtensionVersion("motlist", out var version);
                var ext = ".motlist." + version;
                PlatformUtils.ShowSaveFileDialog((path) => {
                    if (path.EndsWith(ext + ext)) path = path.Replace(ext + ext, ext);
                    mesh.Motlist.WriteTo(path);
                }, Path.GetFileNameWithoutExtension(Handle.Filename.ToString()), $"MOTLIST ({ext})|*{ext}");
            }
            if (Workspace.CurrentBundle != null) {
                ImGui.SameLine();
                if (ImGui.Button("Save Motlist To Bundle ...")) {
                    Workspace.Env.TryGetFileExtensionVersion("motlist", out var version);
                    var ext = ".motlist." + version;
                    ResourcePathPicker.ShowSaveToBundle(new MotListFileLoader(), new BaseFileResource<MotlistFile>(mesh.Motlist), Workspace, Path.ChangeExtension(Handle.Filename.ToString(), ext));
                }
            }
        } else {
            base.DrawFileControls(data);
        }
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild<MotlistFile, string>("Motlist Name", File, getter: (m) => m!.Header.MotListName, setter: (m, n) => m.Header.MotListName = n ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<MotlistFile, string>("Base Motlist Path", File, getter: (m) => m!.Header.BaseMotListPath, setter: (m, n) => m.Header.BaseMotListPath = n ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<MotlistFile, List<MotFileBase>>("Motion Files", File, getter: (m) => m!.MotFiles).AddDefaultHandler<List<MotFileBase>>();
            context.AddChild<MotlistFile, List<MotIndex>>("Motions", File, getter: (m) => m!.Motions).AddDefaultHandler<List<MotIndex>>();
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext context)
    {
        OnIMGUI();
    }

    internal void RefreshUI()
    {
        context.GetChildByValue<List<MotFileBase>>()?.ClearChildren();
    }
}

[ObjectImguiHandler(typeof(MotIndex))]
public class MotIndexImguiHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(MotIndex).GetField(nameof(MotIndex.motNumber))!,
        typeof(MotIndex).GetField(nameof(MotIndex.Switch))!,
        typeof(MotIndex).GetField(nameof(MotIndex.data))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var mot = context.Get<MotIndex>();
        if (context.children.Count == 0) {
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(MotIndex), members: DisplayedFields);
            var motlist = context.FindHandlerInParents<MotlistEditor>()?.File;
            if (motlist != null) {
                context.AddChild("Mot File Instance", (object)mot, getter: (c) => ((MotIndex)c.target!).MotFile, setter: (c, v) => {
                    ((MotIndex)c.target!).MotFile = (MotFileBase)v!;
                    c.parent?.ClearChildren();
                }, handler: new InstancePickerHandler<MotFileBase>(true, (ctx, forceRefresh) => {
                    if (forceRefresh) motlist = ctx.FindHandlerInParents<MotlistEditor>()!.File;
                    return motlist.MotFiles;
                }, (ctx, mf) => {
                    UndoRedo.RecordSet(ctx, mf);
                }));
            }

            context.AddChild("Mot File", mot, getter: (c) => ((MotIndex)c.target!).MotFile, handler: new MotFileBaseHandler(), setter: (ctx, newMot) => {
                var motlist = ctx.FindHandlerInParents<MotlistEditor>()?.File;
                var newInst = (MotFileBase?)newMot;
                if (motlist != null && newMot != null) {
                    if (newInst != null && !motlist.MotFiles.Contains(newInst)) {
                        motlist.MotFiles.Add(newInst);
                    }
                }
                ((MotIndex)ctx.target!).MotFile = newInst;
                ctx.ClearChildren();
            });

            WindowHandlerFactory.SetupObjectUIContext(context, typeof(MotIndex), members: [typeof(MotIndex).GetProperty(nameof(MotIndex.MotClips))!]);
        }

        if (ImguiHelpers.TreeNodeSuffix(context.label, mot.ToString())) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }

    }
}

[ObjectImguiHandler(typeof(MotFileBase))]
public class MotFileBaseHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<MotFileBase>();
        if (context.children.Count == 0) {
            context.AddChild("Motion type", instance, new InstanceTypePickerHandler<MotFileBase>([null, typeof(MotFile), typeof(MotTreeFile)], filterable: false, factory: (ctx, newType) => {
                var motlist = ctx.FindHandlerInParents<MotlistEditor>()?.File;
                var newInstance = (MotFileBase)Activator.CreateInstance(newType, [motlist?.FileHandler ?? new FileHandler()])!;
                return newInstance;
            }), setter: (ctx, newMot) => {
                ctx.parent!.Set(newMot);
                ctx.target = newMot;
                ctx.parent.ClearChildren();
            });
            if (instance != null) {
                var dataChild = context.AddChild(context.label, instance);
                dataChild.AddDefaultHandler();
                dataChild.uiHandler = new MotFileActionHandler(dataChild.uiHandler!);
            }
        }

        ImguiHelpers.BeginRect();
        context.ShowChildrenUI();
        if (instance == null) {
            ImGui.TextColored(Colors.Info, "Motion not defined");
        }
        ImguiHelpers.EndRect(4);
        ImGui.Spacing();
    }
}

[ObjectImguiHandler(typeof(MotFile))]
public class MotFileHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<MotFile>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            context.AddChild<MotFile, string>("Name", instance, getter: (m) => m!.Header.motName, setter: (m, v) => m!.Header.motName = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<MotFile, float>("Frame Count", instance, getter: (m) => m!.Header.frameCount, setter: (m, v) => m!.Header.frameCount = v).AddDefaultHandler<float>();
            context.AddChild<MotFile, ushort>("Frame Rate", instance, getter: (m) => m!.Header.FrameRate, setter: (m, v) => m!.Header.FrameRate = v).AddDefaultHandler<ushort>();
            context.AddChild<MotFile, float>("Blending", instance, getter: (m) => m!.Header.blending, setter: (m, v) => m!.Header.blending = v).AddDefaultHandler<float>();
            context.AddChild<MotFile, string>("Joint Map", instance, getter: (m) => m!.Header.jointMapPath, setter: (m, v) => m!.Header.jointMapPath = v ?? string.Empty, handler: new ResourcePathPicker(ws, KnownFileFormats.JointMap));

            context.AddChild<MotFile, float>("Start Frame", instance, getter: (m) => m!.Header.startFrame, setter: (m, v) => m!.Header.startFrame = v).AddDefaultHandler<float>();
            context.AddChild<MotFile, float>("End Frame", instance, getter: (m) => m!.Header.endFrame, setter: (m, v) => m!.Header.endFrame = v).AddDefaultHandler<float>();

            context.AddChild<MotFile, List<MotBone>>("Bones", instance, getter: (m) => m!.RootBones).AddDefaultHandler();
            context.AddChild<MotFile, List<BoneMotionClip>>("Animation Clips", instance, getter: (m) => m!.BoneClips).AddDefaultHandler();
            context.AddChild<MotFile, List<MotClip>>("Behavior Clips", instance, getter: (m) => m!.Clips).AddDefaultHandler();
            context.AddChild<MotFile, List<MotPropertyTrack>>("Animated Properties", instance, getter: (m) => m!.MotPropertyTracks).AddDefaultHandler();
            context.AddChild<MotFile, MotPropertyTree>("Property Tree", instance, new LazyPlainObjectHandler(typeof(MotPropertyTree)), (m) => m!.PropertyTree, (m, v) => m.PropertyTree = v);
        }

        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(MotTreeFile))]
public class MotTreeFileHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<MotTreeFile>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            context.AddChild<MotTreeFile, string>("Name", instance, getter: (m) => m!.Name, setter: (m, v) => m!.Name = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<MotTreeFile, string>("User Variables Path", instance, new ResourcePathPicker(ws, KnownFileFormats.UserVariables), (m) => m!.UvarPath, (m, v) => m!.UvarPath = v ?? string.Empty);
            context.AddChild<MotTreeFile, string>("Resource Path", instance, getter: (m) => m!.ResourcePath, setter: (m, v) => m!.ResourcePath = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<MotTreeFile, List<TreeIndexPair>>("Indices", instance, getter: (m) => m!.Indices).AddDefaultHandler();
            context.AddChild<MotTreeFile, List<TreeIndexPair>>("Motion ID Remapping Table", instance, getter: (m) => m!.MotionIDRemaps).AddDefaultHandler();
            context.AddChild<MotTreeFile, List<MotionTreeNode>>("Nodes", instance, getter: (m) => m!.Nodes).AddDefaultHandler();
            context.AddChild<MotTreeFile, List<MotionTreeLink>>("Links", instance, getter: (m) => m!.Links).AddDefaultHandler();
        }

        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(MotBone))]
public class MotBoneHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(MotBone).GetProperty(nameof(MotBone.Header))!,
        typeof(MotBone).GetProperty(nameof(MotBone.Children))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<MotBone>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(BoneHeader), false, DisplayedFields);
        }

        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(Track), Stateless = true)]
public class TrackHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(Track).GetField(nameof(Track.frameRate))!,
        typeof(Track).GetField(nameof(Track.maxFrame))!,
        typeof(Track).GetProperty(nameof(Track.FrameIndexType))!,
    ];

    public static readonly TrackHandler Instance = new();

    public void OnIMGUI(UIContext context)
    {
        using var _ = ImguiHelpers.ScopedID(context.label);
        var instance = context.Get<Track>();
        if (instance == null) {
            ImGui.Text(context.label);
            ImGui.SameLine();
            if (ImGui.Button("Create")) {
                var editor = context.FindHandlerInParents<MotlistEditor>();
                if (editor == null) {
                    Logger.Error("Could not found motlist editor context");
                    return;
                }
                if (context.target is MotPropertyTrack motTrack) {
                    instance = new Track(motTrack.Version, TrackValueType.Float);
                    UndoRedo.RecordSet(context, instance);
                } else {
                    var trackHandlers = context.parent?.children.Where(c => c.uiHandler?.GetType() == typeof(TrackHandler)).Select(c => c.uiHandler).ToList();
                    if (trackHandlers == null) {
                        Logger.Error("Could not determine track type");
                    } else {
                        var type = trackHandlers.IndexOf(this) == 1 ? TrackValueType.Quaternion : TrackValueType.Vector3;
                        instance = new Track(editor.File.Header.version.GetMotVersion(), type);
                        UndoRedo.RecordSet(context, instance);
                    }
                }
            }
            return;
        }

        if (context.children.Count == 0) {
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(Track), false, DisplayedFields);
            if (instance.TrackType == TrackValueType.Quaternion) {
                context.AddChild<Track, QuaternionDecompression>(nameof(Track.Compression), instance, getter: (t) => t!.RotationCompressionType, setter: (t, v) => t.RotationCompressionType = v).AddDefaultHandler();
                context.AddChild<Track, Quaternion[]>(nameof(Track.rotations), instance, new ResizableArrayHandler(typeof(Quaternion)), (t) => t!.rotations, (t, v) => t.rotations = v);
            } else if (instance.TrackType == TrackValueType.Vector3) {
                context.AddChild<Track, Vector3Decompression>(nameof(Track.Compression), instance, getter: (t) => t!.TranslationCompressionType, setter: (t, v) => t.TranslationCompressionType = v).AddDefaultHandler();
                context.AddChild<Track, Vector3[]>(nameof(Track.translations), instance, new ResizableArrayHandler(typeof(Vector3)), (t) => t!.translations, (t, v) => t.translations = v);
            } else if (instance.TrackType == TrackValueType.Float) {
                context.AddChild<Track, FloatDecompression>(nameof(Track.Compression), instance, getter: (t) => t!.FloatCompressionType, setter: (t, v) => t.FloatCompressionType = v).AddDefaultHandler();
                context.AddChild<Track, float[]>(nameof(Track.floats), instance, new ResizableArrayHandler(typeof(float)), (t) => t!.floats, (t, v) => t.floats = v);
            }
            context.AddChild<Track, int[]>(nameof(Track.frameIndexes), instance, new ResizableArrayHandler(typeof(int)), (t) => t!.frameIndexes, (t, v) => t.frameIndexes = v);
        }

        if (ImguiHelpers.TreeNodeSuffix(context.label, instance.ToString()!)) {
            for (int i = 0; i < context.children.Count; ++i) {
                context.children[i].ShowUI();
            }
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(BoneMotionClip))]
public class BoneMotionClipHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(BoneMotionClip).GetProperty(nameof(BoneMotionClip.ClipHeader))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<BoneMotionClip>();
        if (context.children.Count == 0) {
            context.AddChild<BoneMotionClip, BoneClipHeader>(nameof(BoneMotionClip.ClipHeader), instance, getter: m => m!.ClipHeader).AddDefaultHandler();
            context.AddChild<BoneMotionClip, Track>(nameof(BoneMotionClip.Translation), instance, new TrackHandler(), m => m!.Translation, (m, v) => m.Translation = v);
            context.AddChild<BoneMotionClip, Track>(nameof(BoneMotionClip.Rotation), instance, new TrackHandler(), m => m!.Rotation, (m, v) => m.Rotation = v);
            context.AddChild<BoneMotionClip, Track>(nameof(BoneMotionClip.Scale), instance, new TrackHandler(), m => m!.Scale, (m, v) => m.Scale = v);
        }

        if (ImguiHelpers.TreeNodeSuffix(context.label, instance.ToString())) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(MotPropertyTrack))]
public class MotPropertyTrackHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(MotPropertyTrack).GetField(nameof(MotPropertyTrack.propertyHash))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<MotPropertyTrack>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(MotPropertyTrack), false, DisplayedFields);
            context.AddChild<MotPropertyTrack, Track>("Track", instance, TrackHandler.Instance, (m) => m!.Track, (m, v) => m.Track = v);
        }

        if (ImguiHelpers.TreeNodeSuffix(context.label, instance.ToString())) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(List<BoneMotionClip>))]
public class BoneMotionClipListHandler : ListHandler
{
    public BoneMotionClipListHandler() : base(typeof(BoneMotionClip), typeof(List<BoneMotionClip>))
    {
        CanCreateNewElements = true;
    }

    protected override object? CreateNewElement(UIContext context)
    {
        var editor = context.FindHandlerInParents<MotlistEditor>();
        if (editor == null) {
            Logger.Error("Could not found motlist editor context");
            return null;
        }
        var clip = new BoneMotionClip(new BoneClipHeader(editor.File.Header.version.GetMotVersion()));
        return clip;
    }
}

[ObjectImguiHandler(typeof(BoneHeader))]
public class MotBoneHeaderHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(BoneHeader).GetField(nameof(BoneHeader.boneName))!,
        typeof(BoneHeader).GetField(nameof(BoneHeader.Index))!,
        typeof(BoneHeader).GetField(nameof(BoneHeader.translation))!,
        typeof(BoneHeader).GetField(nameof(BoneHeader.quaternion))!,
        typeof(BoneHeader).GetField(nameof(BoneHeader.uknValue1))!,
        typeof(BoneHeader).GetField(nameof(BoneHeader.uknValue2))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<BoneHeader>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(BoneHeader), false, DisplayedFields);
        }

        var show = ImguiHelpers.TreeNodeSuffix("Bone", instance.ToString());
        if (show) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        } else {
            ImGui.SameLine();
        }
    }
}

[ObjectImguiHandler(typeof(ClipHeader))]
public class MotClipHeaderHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(ClipHeader).GetField(nameof(ClipHeader.numFrames))!,
        typeof(ClipHeader).GetField(nameof(ClipHeader.guid))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<ClipHeader>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(ClipHeader), false, DisplayedFields);
        }

        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(BoneClipHeader))]
public class MotBoneClipHeaderHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(BoneClipHeader).GetField(nameof(BoneClipHeader.boneIndex))!,
        typeof(BoneClipHeader).GetField(nameof(BoneClipHeader.boneName))!,
        typeof(BoneClipHeader).GetField(nameof(BoneClipHeader.boneHash))!,
        typeof(BoneClipHeader).GetField(nameof(BoneClipHeader.trackFlags))!,
        typeof(BoneClipHeader).GetField(nameof(BoneClipHeader.uknIndex))!,
        typeof(BoneClipHeader).GetField(nameof(BoneClipHeader.uknFloat))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<BoneClipHeader>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(BoneClipHeader), false, DisplayedFields);
        }

        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(MotClip))]
public class MotClipHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(MotClip).GetField(nameof(MotClip.mainTrackIndex))!,
        typeof(MotClip).GetField(nameof(MotClip.lastTrackIndex))!,
        typeof(MotClip).GetField(nameof(MotClip.uknBytes28))!,
        typeof(MotClip).GetProperty(nameof(MotClip.ClipEntry))!,
        typeof(MotClip).GetProperty(nameof(MotClip.EndClipStructs))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<MotClip>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(MotClip), false, DisplayedFields);
        }

        if (ImguiHelpers.TreeNodeSuffix(context.label, instance.ToString())) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(List<MotFileBase>))]
public class MotFileListHandler : ListHandler
{
    public MotFileListHandler() : base(typeof(MotFileBase), typeof(List<MotFileBase>))
    {
        CanCreateNewElements = true;
        Filterable = true;
    }

    protected override bool MatchesFilter(object? obj, string filter)
    {
        return obj is MotFileBase mot && mot.Name.Contains(filter, StringComparison.InvariantCultureIgnoreCase);
    }

    protected override object? CreateNewElement(UIContext context)
    {
        var editor = context.FindHandlerInParents<MotlistEditor>();
        if (editor == null) {
            Logger.Error("Could not found motlist editor context");
            return null;
        }
        var mot = new MotFile(editor.File.FileHandler);
        var boneSource = editor.File.MotFiles.FirstOrDefault() as MotFile;
        if (boneSource != null) mot.CopyBones(boneSource);

        return mot;
    }
}

[ObjectImguiHandler(typeof(List<MotBone>))]
public class MotBoneListHandler : ListHandler
{
    public MotBoneListHandler() : base(typeof(MotBone), typeof(List<MotBone>))
    {
        CanCreateNewElements = true;
    }

    protected override object? CreateNewElement(UIContext context)
    {
        var editor = context.FindHandlerInParents<MotlistEditor>();
        if (editor == null) {
            Logger.Error("Could not found motlist editor context");
            return null;
        }
        var mot = context.FindValueInParentValues<MotFile>();
        var newIndex = mot == null || mot.Bones.Count == 0 ? 0 : (mot.Bones.Max(b => b.Index) + 1);
        var bone = new MotBone(new BoneHeader() {
            boneName = "New_bone".GetUniqueName((n) => true == mot?.Bones.Any(b => b.Name == n)),
            Index = newIndex,
            quaternion = Quaternion.Identity,
        });

        bone.Parent = context.FindValueInParentValues<MotBone>();
        if (mot == null) return bone;

        UndoRedo.RecordCallback(context, DoAdd(context, mot, bone), DoRemove(context, mot, bone));
        return null;
    }

    protected override void RemoveFromList(UIContext context, IList list, int i)
    {
        var mot = context.FindValueInParentValues<MotFile>();
        if (mot == null) {
            base.RemoveFromList(context, list, i);
            return;
        }

        var bone = (MotBone)list[i]!;
        UndoRedo.RecordCallback(context, DoRemove(context, mot, bone), DoAdd(context, mot, bone));
    }

    private static Action DoAdd(UIContext context, MotFile mot, MotBone bone)
    {
        return () => {
            context.ClearChildren();
            mot.AddBone(bone);
        };
    }

    private static Action DoRemove(UIContext context, MotFile mot, MotBone bone)
    {
        return () => {
            context.ClearChildren();
            mot.RemoveBone(bone);
        };
    }
}

[ObjectImguiHandler(typeof(ClipEntry))]
public class ClipEntryHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(ClipEntry).GetField(nameof(ClipEntry.ExtraPropertyData))!,
        typeof(ClipEntry).GetProperty(nameof(ClipEntry.FrameCount))!,
        typeof(ClipEntry).GetProperty(nameof(ClipEntry.Guid))!,
        typeof(ClipEntry).GetProperty(nameof(ClipEntry.Tracks))!,
        typeof(ClipEntry).GetProperty(nameof(ClipEntry.SpeedPointData))!,
        typeof(ClipEntry).GetProperty(nameof(ClipEntry.HermiteData))!,
        typeof(ClipEntry).GetProperty(nameof(ClipEntry.ClipInfoList))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<ClipEntry>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(ClipEntry), false, DisplayedFields);
        }

        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(CTrack))]
public class CTrackHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(CTrack).GetProperty(nameof(CTrack.Name))!,
        typeof(CTrack).GetField(nameof(CTrack.nameHash))!,
        typeof(CTrack).GetField(nameof(CTrack.nodeCount))!,
        typeof(CTrack).GetProperty(nameof(CTrack.Properties))!,
        typeof(CTrack).GetField(nameof(CTrack.Start_Frame))!,
        typeof(CTrack).GetField(nameof(CTrack.End_Frame))!,
        typeof(CTrack).GetField(nameof(CTrack.guid1))!,
        typeof(CTrack).GetField(nameof(CTrack.guid2))!,
        typeof(CTrack).GetField(nameof(CTrack.nodeType))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<CTrack>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(CTrack), false, DisplayedFields);
        }

        if (ImguiHelpers.TreeNodeSuffix(context.label, instance.ToString())) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(Key))]
public class KeyHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(Key).GetProperty(nameof(Key.Value))!,
        typeof(Key).GetProperty(nameof(Key.PropertyType))!,
        typeof(Key).GetField(nameof(Key.frame))!,
        typeof(Key).GetField(nameof(Key.rate))!,
        typeof(Key).GetField(nameof(Key.interpolation))!,
        typeof(Key).GetField(nameof(Key.instanceValue))!,
        typeof(Key).GetField(nameof(Key.unknown))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<Key>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(Key), false, DisplayedFields);
        }

        if (ImguiHelpers.TreeNodeSuffix(context.label, instance.ToString())) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(ReeLib.Clip.PropertyInfo))]
public class PropertyInfoHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(ReeLib.Clip.PropertyInfo).GetField(nameof(ReeLib.Clip.PropertyInfo.startFrame))!,
        typeof(ReeLib.Clip.PropertyInfo).GetField(nameof(ReeLib.Clip.PropertyInfo.endFrame))!,
        typeof(ReeLib.Clip.PropertyInfo).GetField(nameof(ReeLib.Clip.PropertyInfo.DataType))!,
        typeof(ReeLib.Clip.PropertyInfo).GetProperty(nameof(ReeLib.Clip.PropertyInfo.FunctionName))!,
        typeof(ReeLib.Clip.PropertyInfo).GetField(nameof(ReeLib.Clip.PropertyInfo.nameUtf16Hash))!,
        typeof(ReeLib.Clip.PropertyInfo).GetField(nameof(ReeLib.Clip.PropertyInfo.nameAsciiHash))!,
        typeof(ReeLib.Clip.PropertyInfo).GetField(nameof(ReeLib.Clip.PropertyInfo.arrayIndex))!,
        typeof(ReeLib.Clip.PropertyInfo).GetField(nameof(ReeLib.Clip.PropertyInfo.speedPointNum))!,
        typeof(ReeLib.Clip.PropertyInfo).GetField(nameof(ReeLib.Clip.PropertyInfo.uknCount))!,
        typeof(ReeLib.Clip.PropertyInfo).GetField(nameof(ReeLib.Clip.PropertyInfo.RE3hash))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<ReeLib.Clip.PropertyInfo>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(ReeLib.Clip.PropertyInfo), false, DisplayedFields);
        }

        if (ImguiHelpers.TreeNodeSuffix(context.label, instance.ToString()!)) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}
