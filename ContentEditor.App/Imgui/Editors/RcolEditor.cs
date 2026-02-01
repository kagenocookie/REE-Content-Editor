using System.Reflection;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.Rcol;

namespace ContentEditor.App.ImguiHandling.Rcol;

public class RcolEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler, IInspectorController, IRSZFileEditor
{
    public override string HandlerName => "RequestSetCollider";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public RcolFile File => Handle.GetFile<RcolFile>();

    public ContentWorkspace Workspace { get; }

    public RcolEditor RootEditor => context.FindHandlerInParents<RcolEditor>(true) ?? this;

    private RequestSetColliderComponent? _component;
    public RequestSetColliderComponent? Component => _component;

    protected override bool IsRevertable => context.Changed;

    private readonly List<ObjectInspector> inspectors = new();
    private ObjectInspector? primaryInspector;
    public object? PrimaryTarget => primaryInspector?.Target;

    private static MemberInfo[] BasicFields = [
        typeof(RcolFile).GetProperty(nameof(RcolFile.IgnoreTags))!,
        typeof(RcolFile).GetProperty(nameof(RcolFile.AutoGenerateJointDescs))!,
        typeof(RcolFile).GetProperty(nameof(RcolFile.Groups))!,
        typeof(RcolFile).GetProperty(nameof(RcolFile.RequestSets))!,
    ];

    public RcolEditor(ContentWorkspace env, FileHandle file) : base(file)
    {
        Workspace = env;
    }

    public RcolEditor(ContentWorkspace env, FileHandle file, RequestSetColliderComponent component) : base(file)
    {
        Workspace = env;
        _component = component;
    }

    public RSZFile GetRSZFile() => File.RSZ;

    protected override void Reset()
    {
        base.Reset();
        if (primaryInspector != null) primaryInspector.Target = null!;
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            var child = context.AddChild("Data", File);
            child.uiHandler = new PlainObjectHandler();
            WindowHandlerFactory.SetupObjectUIContext(child, typeof(RcolFile), members: BasicFields);
        }
        var p = ImGui.GetCursorPos();
        var s = ImGui.GetWindowSize();
        ImGui.BeginChild("Tree", new System.Numerics.Vector2(400, s.Y - p.Y - ImGui.GetStyle().WindowPadding.Y), ImGuiChildFlags.ResizeX);
        context.children[0].ShowUI();
        ImGui.EndChild();
        if (context.children.Count == 1 && primaryInspector != null) {
            context.children.Add(primaryInspector.Context);
        }
        if (context.children.Count > 1 && primaryInspector != null) {
            ImGui.SameLine();
            ImGui.BeginChild("Inspector");
            primaryInspector!.OnIMGUI();
            ImGui.EndChild();
        }
    }

    public void SetPrimaryInspector(object? target)
    {
        if (primaryInspector == null) {
            primaryInspector = AddEmbeddedInspector(target);
        } else {
            primaryInspector.Target = target;
        }
    }

    public ObjectInspector AddInspector(object target)
    {
        var inspector = new ObjectInspector(this);
        var window = EditorWindow.CurrentWindow!.AddSubwindow(inspector);
        var child = context.AddChild("Inspector", window, NullUIHandler.Instance);
        inspectors.Add(inspector);
        inspector.Target = target;
        inspector.Closed += () => OnInspectorClosed(inspector);
        return inspector;
    }

    private ObjectInspector AddEmbeddedInspector(object? target)
    {
        var inspector = new ObjectInspector(this);
        var window = WindowData.CreateEmbeddedWindow(context, context.GetWindow()!, inspector, "Inspector");
        inspectors.Add(inspector);
        inspector.Target = target;
        inspector.Closed += () => OnInspectorClosed(inspector);
        return inspector;
    }

    private void OnInspectorClosed(ObjectInspector inspector)
    {
        inspectors.Remove(inspector);
        if (primaryInspector == inspector) {
            primaryInspector = null;
        }
    }

    void IInspectorController.EmitSave()
    {
        foreach (var inspector in inspectors) inspector.Context.Save();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(List<RequestSet>))]
public class RequestSetListEditor : DictionaryListImguiHandler<string, RequestSet, List<RequestSet>>
{
    public RequestSetListEditor()
    {
        Filterable = true;
    }

    protected override string GetKey(RequestSet item) => item.Info.Name;

    protected override RequestSet? CreateItem(UIContext context, string key)
    {
        var rcol = context.FindValueInParentValues<RcolFile>();
        if (rcol == null) return null;
        rcol.CreateNewRequestSet(key);
        return null;
    }

    protected override void InitChildContext(UIContext itemContext)
    {
        itemContext.uiHandler = ItemHandler;
    }

    private static readonly RequestSetNodeItem ItemHandler = new RequestSetNodeItem();

    private class RequestSetNodeItem : IObjectUIHandler
    {
        public void OnIMGUI(UIContext context)
        {
            var item = context.Get<RequestSet>();
            var show = ImguiHelpers.TreeNodeSuffix(context.label, context.GetRaw()?.ToString() ?? string.Empty);
            if (ImGui.IsItemClicked()) {
                context.FindHandlerInParents<IInspectorController>()?.SetPrimaryInspector(item!);
            }
            HandleContextMenu(context);
            if (show) {
                var inspector = context.FindHandlerInParents<IInspectorController>();
                if (ImGui.Selectable("Info", inspector?.PrimaryTarget == item)) {
                    inspector?.SetPrimaryInspector(item!);
                }
                if (ImguiHelpers.SelectableSuffix($"UserData", item.Instance?.ToString(), inspector?.PrimaryTarget == item.Instance)) {
                    inspector?.SetPrimaryInspector(item.Instance!);
                }
                if (ImguiHelpers.SelectableSuffix($"Group", item.Group?.Info.Name, inspector?.PrimaryTarget == item.Group)) {
                    inspector?.SetPrimaryInspector(item.Group!);
                }
                ImGui.TreePop();
            }
        }

        protected void HandleContextMenu(UIContext context)
        {
            if (ImGui.BeginPopupContextItem(context.label)) {
                var item = context.Get<RequestSet>();
                if (ImGui.Selectable("Delete")) {
                    var list = context.parent!.Get<List<RequestSet>>();
                    UndoRedo.RecordListRemove(context.parent, list, context.Get<RequestSet>());
                }
                if (ImGui.Selectable("Duplicate")) {
                    var list = context.parent!.Get<List<RequestSet>>();
                    var clone = item.Clone();
                    clone.Info.ID = clone.Info.ID + 1; // TODO verify unique
                    UndoRedo.RecordListInsert(context.parent, list, clone, list.IndexOf(item) + 1);
                }
                if (ImGui.Selectable("Copy")) {
                    VirtualClipboard.CopyToClipboard(item.Clone());
                }
                if (VirtualClipboard.TryGetFromClipboard<RequestSet>(out var newSet)) {
                    if (ImGui.Selectable("Paste (replace)")) {
                        var rcol = context.FindHandlerInParents<RcolEditor>()?.File;
                        var groupExists = newSet.Group != null && rcol?.Groups.Contains(newSet.Group) == true;
                        var clone = newSet.Clone(!groupExists);
                        if (!groupExists) rcol?.Groups.Add(clone.Group!);
                        UndoRedo.RecordSet(context, clone);
                        UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, context);
                    }
                    if (ImGui.Selectable("Paste (new)")) {
                        var list = context.parent!.Get<List<RequestSet>>();
                        var rcol = context.FindHandlerInParents<RcolEditor>()?.File;
                        var groupExists = newSet.Group != null && rcol?.Groups.Contains(newSet.Group) == true;
                        var clone = newSet.Clone(!groupExists);
                        if (!groupExists) rcol?.Groups.Add(clone.Group!);
                        UndoRedo.RecordListInsert(context.parent, list, clone, list.IndexOf(item) + 1);
                        UndoRedo.AttachClearChildren(UndoRedo.CallbackType.Both, context.parent);
                    }
                }
                ImGui.EndPopup();
            }
        }
    }
}


[ObjectImguiHandler(typeof(RequestSet))]
public class RequestSetEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var rset = context.Get<RequestSet>();
        if (context.children.Count == 0) {
            context.AddChild<RequestSet, uint>("ID", rset, getter: (i) => i!.Info.ID, setter: (i, v) => i.Info.ID = v).AddDefaultHandler<uint>();
            context.AddChild<RequestSet, string>("Name", rset, getter: (i) => i!.Info.Name, setter: (i, v) => i.Info.Name = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<RequestSet, string>("KeyName", rset, getter: (i) => i!.Info.KeyName, setter: (i, v) => i.Info.KeyName = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<RequestSet, int>("Status", rset, getter: (i) => i!.Info.status, setter: (i, v) => i.Info.status = v).AddDefaultHandler<int>();

            context.AddChildContextSetter<RequestSet, RcolGroup>(
                "Group Instance",
                rset,
                new InstancePickerHandler<RcolGroup>(false, (ctx, refresh) => {
                    return ctx.FindObjectInspectorParent<RcolEditor>()?.File.Groups ?? [];
                }) { DisableRefresh = true },
                (i) => i!.Group,
                (ctx, i, v) => {
                    i.Group = v;
                    ctx.parent?.ClearChildren();
                });
            context.AddChild<RequestSet, RcolGroup>(
                "Group",
                rset,
                getter: (i) => i!.Group,
                setter: (i, v) => i.Group = v).AddDefaultHandler<RcolGroup>();

            context.AddChild<RequestSet, RszInstance>(
                "UserData",
                rset,
                new NestedUIHandlerStringSuffixed(new RszClassnamePickerHandler("via.physics.RequestSetColliderUserData")),
                (i) => i!.Instance,
                setter: (i, v) => i.Instance = v);
            context.AddChild<RequestSet, List<RszInstance>>("Shape userdata", rset, new RszListInstanceHandler("via.physics.RequestSetColliderUserData"), (i) => i!.ShapeUserdata, (i, v) => i.ShapeUserdata = v!);
        }

        var name = rset.Info.Name;
        context.ShowChildrenUI();
        if (rset.Info.Name != name) {
            var listeditor = context.FindParentContextByHandler<RequestSetListEditor>()
                ?? context.FindObjectInspectorParent<RcolEditor>()?.Context.FindNestedChildByHandler<RequestSetListEditor>();
            listeditor?.ClearChildren();
            // context.parent?.ClearChildren();
        }
    }
}

[ObjectImguiHandler(typeof(RcolGroup))]
public class RcolGroupEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var group = context.Get<RcolGroup>();
            context.AddChild<RcolGroup, string>("Name", group, getter: (i) => i!.Info.Name, setter: (i, v) => i.Info.Name = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<RcolGroup, Guid>("GUID", group, getter: (i) => i!.Info.guid, setter: (i, v) => i.Info.guid = v).AddDefaultHandler<Guid>();
            context.AddChild<RcolGroup, Guid>("Layer", group, getter: (i) => i!.Info.LayerGuid, setter: (i, v) => i.Info.LayerGuid = v).AddDefaultHandler<Guid>();
            context.AddChild<RcolGroup, List<Guid>>("Masks", group, new ListHandler(typeof(Guid), typeof(List<Guid>)) { CanCreateRemoveElements = true }, (i) => i!.Info.MaskGuids, (i, v) => i.Info.MaskGuids = v);
            context.AddChild<RcolGroup, RszInstance>(
                "UserData",
                group,
                new NestedUIHandlerStringSuffixed(new SwappableRszInstanceHandler("via.physics.RequestSetColliderUserData")),
                (i) => i!.Info.UserData,
                setter: (i, v) => i.Info.UserData = v);
            context.AddChild<RcolGroup, List<RcolShape>>("Shapes", group, new ListHandler(typeof(RcolShape)) { CanCreateRemoveElements = true }, getter: (i) => i!.Shapes);
            context.AddChild<RcolGroup, List<RcolShape>>("ExtraShapes", group, new ListHandler(typeof(RcolShape)) { CanCreateRemoveElements = true }, getter: (i) => i!.ExtraShapes);
        }

        if (AppImguiHelpers.CopyableTreeNode<RcolGroup>(context)) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(RcolShape))]
public class RcolShapeEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var group = context.Get<RcolShape>();
            context.AddChild<RcolShape, string>("Name", group, getter: (i) => i!.Info.Name, setter: (i, v) => i.Info.Name = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<RcolShape, Guid>("GUID", group, getter: (i) => i!.Info.Guid, setter: (i, v) => i.Info.Guid = v).AddDefaultHandler<Guid>();
            context.AddChild<RcolShape, int>("LayerIndex", group, getter: (i) => i!.Info.LayerIndex, setter: (i, v) => i.Info.LayerIndex = v).AddDefaultHandler<int>();
            context.AddChild("Shape Type", group, getter: (ctx) => ((RcolShape)ctx.target!).Info.shapeType, setter: (ctx, v) => {
                var i = (RcolShape)ctx.target!;
                if (i.Info.shapeType == (ShapeType)v!) return;
                i.Info.shapeType = (ShapeType)v!;
                i.UpdateShapeType();
                ctx.parent?.ClearChildren();
            }).AddDefaultHandler<ShapeType>();
            context.AddChild<RcolShape, int>("Attribute", group, getter: (i) => i!.Info.Attribute, setter: (i, v) => i.Info.Attribute = v).AddDefaultHandler<int>();
            context.AddChild<RcolShape, uint>("Skip ID Bits", group, getter: (i) => i!.Info.SkipIdBits, setter: (i, v) => i.Info.SkipIdBits = v).AddDefaultHandler<uint>();
            context.AddChild<RcolShape, uint>("IgnoreTag Bits", group, getter: (i) => i!.Info.IgnoreTagBits, setter: (i, v) => i.Info.IgnoreTagBits = v).AddDefaultHandler<uint>();
            context.AddChild<RcolShape, string>("Primary Joint", group, getter: (i) => i!.Info.primaryJointNameStr, setter: (i, v) => i.Info.primaryJointNameStr = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<RcolShape, string>("Secondary Joint", group, getter: (i) => i!.Info.secondaryJointNameStr, setter: (i, v) => i.Info.secondaryJointNameStr = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild("Shape", group, getter: (i) => ((RcolShape)i.target!).shape, setter: (i, s) => ((RcolShape)i.target!).shape = s).AddDefaultHandler();
            context.AddChild<RcolShape, RszInstance>(
                "UserData",
                group,
                new NestedUIHandlerStringSuffixed(new SwappableRszInstanceHandler("via.physics.RequestSetColliderUserData")),
                (i) => i!.Instance,
                setter: (i, v) => i.Instance = v);
        }

        if (AppImguiHelpers.CopyableTreeNode<RcolShape>(context)) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(IgnoreTag))]
public class RcolIgnoreTagHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var tag = context.Get<IgnoreTag>();
        var tagStr = tag.tag;
        if (ImGui.InputText(context.label, ref tagStr, 128)) {
            UndoRedo.RecordCallbackSetter(context, tag, tag.tag, tagStr, (o, v) => {
                o.tag = v;
                o.hash = MurMur3HashUtils.GetHash(v);
            }, $"{tag.GetHashCode()} tag");
        }
    }
}
