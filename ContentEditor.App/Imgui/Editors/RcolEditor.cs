using System.Reflection;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
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
    private Scene? scene;

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
        scene = component.Scene;
    }

    protected override void OnFileReverted()
    {
        Reset();
    }

    public RSZFile GetRSZFile() => File.RSZ;

    private void Reset()
    {
        if (context.children.Count > 0) {
            // not letting the child contexts dispose - so we don't dispose the file stream
            context.children.Clear();
        }
        if (primaryInspector != null) primaryInspector.Target = null!;
        failedToReadfile = false;
    }

    protected override void DrawFileContents()
    {
        if (scene == null) {
            // TODO add mesh picker -> open mesh viewer?
        }

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

    private class RequestSetNodeEditor : IObjectUIHandler
    {
        private static MemberInfo[] DisplayedFields = [
            typeof(RequestSet).GetProperty(nameof(RequestSet.Index))!,
        ];

        public void OnIMGUI(UIContext context)
        {
            if (context.children.Count == 0) {
                var rset = context.Get<RequestSet>();
                context.AddChild<RequestSet, int>("Index", rset, getter: (i) => i!.Index, setter: (i, v) => i.Index = v).AddDefaultHandler<int>();
                context.AddChild<RequestSet, string>("Name", rset, getter: (i) => i!.Info.Name, setter: (i, v) => i.Info.Name = v ?? string.Empty).AddDefaultHandler<string>();
                context.AddChild<RequestSet, string>("KeyName", rset, getter: (i) => i!.Info.KeyName, setter: (i, v) => i.Info.KeyName = v ?? string.Empty).AddDefaultHandler<string>();
                context.AddChild<RequestSet, int>("Status", rset, getter: (i) => i!.Info.status, setter: (i, v) => i.Info.status = v).AddDefaultHandler<int>();
            }

            context.ShowChildrenUI();
        }
    }

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
                if (ImGui.Button("Delete")) {
                    var list = context.parent!.Get<List<RequestSet>>();
                    UndoRedo.RecordListRemove(context.parent, list, context.Get<RequestSet>());
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.Button("Duplicate")) {
                    var list = context.parent!.Get<List<RequestSet>>();
                    var item = context.Get<RequestSet>();
                    var clone = item.Clone();
                    clone.Index = item.Index + 1;
                    clone.Info.ID = clone.Info.ID + 1; // TODO verify unique
                    UndoRedo.RecordListInsert(context.parent, list, clone, list.IndexOf(item) + 1);
                    ImGui.CloseCurrentPopup();
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
            context.AddChild<RequestSet, int>("Index", rset, getter: (i) => i!.Index, setter: (i, v) => i.Index = v).AddDefaultHandler<int>();
            context.AddChild<RequestSet, string>("Name", rset, getter: (i) => i!.Info.Name, setter: (i, v) => i.Info.Name = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<RequestSet, string>("KeyName", rset, getter: (i) => i!.Info.KeyName, setter: (i, v) => i.Info.KeyName = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<RequestSet, int>("Status", rset, getter: (i) => i!.Info.status, setter: (i, v) => i.Info.status = v).AddDefaultHandler<int>();
            context.AddChild<RequestSet, RcolGroup>(
                "Group",
                rset,
                new NestedUIHandlerStringSuffixed(new RcolGroupEditor()),
                (i) => i!.Group,
                (i, v) => i.Group = v);
            context.AddChild<RequestSet, RszInstance>(
                "UserData",
                rset,
                new NestedUIHandlerStringSuffixed(new SwappableRszInstanceHandler("via.physics.RequestSetColliderUserData")),
                (i) => i!.Instance,
                setter: (i, v) => i.Instance = v);
            context.AddChild<RequestSet, List<RszInstance>>("Shape userdata", rset, getter: (i) => i!.ShapeUserdata, setter: (i, v) => i.ShapeUserdata = v!).AddDefaultHandler<List<RszInstance>>();
        }

        var name = rset.Info.Name;
        context.ShowChildrenUI();
        if (rset.Info.Name != name) {
            var listeditor = context.FindParentContextByHandler<RequestSetListEditor>()
                ?? (context.FindHandlerInParents<ObjectInspector>()?.ParentWindow as RcolEditor)?.Context.FindNestedChildByHandler<RequestSetListEditor>();
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
            context.AddChild<RcolGroup, List<Guid>>("Masks", group, new ListHandler(typeof(Guid), typeof(List<Guid>)) { CanCreateNewElements = true }, (i) => i!.Info.MaskGuids, (i, v) => i.Info.MaskGuids = v);
            context.AddChild<RcolGroup, RszInstance>(
                "UserData",
                group,
                new NestedUIHandlerStringSuffixed(new SwappableRszInstanceHandler("via.physics.RequestSetColliderUserData")),
                (i) => i!.Info.UserData,
                setter: (i, v) => i.Info.UserData = v);
            context.AddChild<RcolGroup, List<RcolShape>>("Shapes", group, new ListHandler(typeof(RcolShape)) { CanCreateNewElements = true }, getter: (i) => i!.Shapes);
            context.AddChild<RcolGroup, List<RcolShape>>("ExtraShapes", group, new ListHandler(typeof(RcolShape)) { CanCreateNewElements = true }, getter: (i) => i!.ExtraShapes);
        }

        context.ShowChildrenUI();
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

        context.ShowChildrenNestedUI();
    }
}

[ObjectImguiHandler(typeof(IgnoreTag))]
public class RcolIgnoreTagHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var tag = context.Get<IgnoreTag>();
        var tagStr = tag.tag;
        var w = ImGui.CalcItemWidth();
        ImGui.SetNextItemWidth(w * 0.7f);
        ImGui.PushID(context.label);
        if (ImGui.InputText("##tag", ref tagStr, 100)) {
            UndoRedo.RecordCallbackSetter(context, tag, tag.tag, tagStr, (o, v) => o.tag = v, $"{tag.GetHashCode()} tag");
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(w * 0.3f - ImGui.GetStyle().FramePadding.X);
        var ukn = (int)tag.ukn;
        if (ImGui.InputInt(context.label, ref ukn)) {
            UndoRedo.RecordCallbackSetter(context, tag, tag.ukn, (uint)ukn, (o, v) => o.ukn = v, $"{tag.GetHashCode()} ukn");
        }
        ImGui.PopID();
    }
}
