using System.Reflection;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Mot;
using ReeLib.Motlist;

namespace ContentEditor.App.ImguiHandling;

public class MotlistEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public ContentWorkspace Workspace { get; }
    public MotlistFile File { get; }

    public MotlistEditor(ContentWorkspace env, FileHandle file) : base(file)
    {
        Workspace = env;
        File = file.GetFile<MotlistFile>();
    }

    protected override void OnFileReverted()
    {
        base.OnFileReverted();
        context.ClearChildren();
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild<MotlistFile, List<MotFileBase>>("Motion files", File, getter: (m) => m!.MotFiles).AddDefaultHandler<List<MotFileBase>>();
            context.AddChild<MotlistFile, List<MotIndex>>("Motions", File, getter: (m) => m!.Motions).AddDefaultHandler<List<MotIndex>>();
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext context)
    {
        OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(MotIndex))]
public class MotIndexImguiHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(MotIndex).GetField(nameof(MotIndex.motNumber))!,
        typeof(MotIndex).GetField(nameof(MotIndex.Switch))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var mot = context.Get<MotIndex>();
        if (context.children.Count == 0) {
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(MotIndex), members: DisplayedFields);
            var motlist = context.FindHandlerInParents<MotlistEditor>()?.File;
            if (motlist != null) {
                context.AddChild<MotIndex, MotFileBase>("Mot File Instance", mot, getter: (c) => c!.MotFile, setter: (c, v) => c.MotFile = v,
                    handler: new InstancePickerHandler<MotFileBase>(true, (ctx, forceRefresh) => {
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

            WindowHandlerFactory.SetupObjectUIContext(context, typeof(MotIndex), members: [typeof(MotIndex).GetProperty(nameof(MotIndex.MotClip))!]);
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
                dataChild.uiHandler = new NestedUIHandlerStringSuffixed(dataChild.uiHandler!);
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

            context.AddChild<MotFile, ushort>("UknShort", instance, getter: (m) => m!.Header.uknShort, setter: (m, v) => m!.Header.uknShort = v).AddDefaultHandler<ushort>();
            context.AddChild<MotFile, float>("uknFloat1", instance, getter: (m) => m!.Header.uknFloat1, setter: (m, v) => m!.Header.uknFloat1 = v).AddDefaultHandler<float>();
            context.AddChild<MotFile, float>("uknFloat2", instance, getter: (m) => m!.Header.uknFloat2, setter: (m, v) => m!.Header.uknFloat2 = v).AddDefaultHandler<float>();

            context.AddChild<MotFile, List<BoneHeader>>("Bones", instance, getter: (m) => m!.BoneHeaders, setter: (m, v) => m.BoneHeaders = v, handler: new ListHandler(typeof(BoneHeader), typeof(List<BoneHeader>)) { CanCreateNewElements = true });
            context.AddChild<MotFile, List<BoneMotionClip>>("Bone Clips", instance, getter: (m) => m!.BoneClips).AddDefaultHandler();
            context.AddChild<MotFile, List<MotClip>>("Clips", instance, getter: (m) => m!.Clips).AddDefaultHandler();
        }

        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(Bone))]
public class MotBoneHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(Bone).GetField(nameof(BoneHeader.boneName))!,
        typeof(MotIndex).GetField(nameof(MotIndex.Switch))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<Bone>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            context.AddChild<Bone, string>("Name", instance, getter: (m) => m!.Header.boneName, setter: (m, v) => m!.Header.boneName = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<Bone, uint>("Name hash", instance, getter: (m) => m!.Header.boneHash, handler: new ReadOnlyWrapperHandler(new NumericFieldHandler<int>(ImGuiDataType.U32)));
        }

        context.ShowChildrenUI();
    }
}
