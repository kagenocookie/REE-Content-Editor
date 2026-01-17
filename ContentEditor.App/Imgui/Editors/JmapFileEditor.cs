using System.Collections;
using ContentPatcher;
using ReeLib;
using ReeLib.Jmap;

namespace ContentEditor.App.ImguiHandling;

public class JmapFileEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "Joint Map";

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public JmapFileEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    private static bool ConditionJmapDmc5(UIContext ctx) => ((JmapFile)ctx.target!).Header.Version <= 10;
    private static bool ConditionJmapRERT(UIContext ctx) => ((JmapFile)ctx.target!).Header.Version >= 19;

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            var instance = Handle.GetFile<JmapFile>();
            var ws = context.GetWorkspace();
            context.AddChild<JmapFile, uint>("Attribute Flags", instance, getter: v => v!.Header.attributeFlags, setter: (o, v) => o.Header.attributeFlags = v).AddDefaultHandler();
            context.AddChild<JmapFile, List<JointData>>("Joints", instance, new ConditionalUIHandler(new ListHandlerTyped<JointData>() { Filterable = true }, ConditionJmapDmc5), v => v!.Joints);
            context.AddChild<JmapFile, uint[]?>("AfterBoneData", instance, new ConditionalUIHandler(new ArrayHandler(typeof(uint)), ConditionJmapDmc5), v => v!.AfterBoneData);

            context.AddChild<JmapFile, List<JointMaskGroup>>("Joint Mask Groups", instance, new ListHandlerTyped<JointMaskGroup>(), v => v!.MaskGroups);
            context.AddChild<JmapFile, List<ExtraJointInfo>>("Extra Joints", instance, getter: v => v!.ExtraJoints.Joints).AddDefaultHandler<List<ExtraJointInfo>>();
            context.AddChild<JmapFile, List<ExtraJointGroup>>("Extra Joint Groups", instance, getter: v => v!.ExtraJointGroups.Groups).AddDefaultHandler<List<ExtraJointGroup>>();

            context.AddChild<JmapFile, IkMotionData?>("IK Motion Data", instance, new ConditionalUIHandler(new LazyPlainObjectHandler<IkMotionData>(), ConditionJmapRERT), v => v!.IkMotionData);
            context.AddChild<JmapFile, SymmetryMirrorData?>("Symmetry Data", instance, new ConditionalUIHandler(new LazyPlainObjectHandler<SymmetryMirrorData>(), ConditionJmapRERT), v => v!.SymmetryData);
            context.AddChild<JmapFile, ExtraHashData?>("Extra Hashes", instance, new ConditionalUIHandler(new LazyPlainObjectHandler<ExtraHashData>(), ConditionJmapRERT), v => v!.ExtraHashes);
        }

        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(List<ExtraJointGroup>))]
public class ExtraJointGroupHandler : ListHandlerTyped<ExtraJointGroup>
{
    public ExtraJointGroupHandler()
    {
        CanCreateRemoveElements = true;
    }

    protected override UIContext CreateElementContext(UIContext context, IList list, int elementIndex)
    {
        var ctx = WindowHandlerFactory.CreateListElementContext(context, elementIndex);
        ctx.uiHandler = new InstanceTypePickerHandler<ExtraJointGroup>(ExtraJointGroup.ExtraJointTypeMap.Values.ToArray(), innerHandler: new LazyPlainObjectHandler(ctx.GetRaw()!.GetType()));
        return ctx;
    }

    protected override object? CreateNewElement(UIContext context)
    {
        return new ExtraJointGroupRotation();
    }
}
