using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Common;
using ReeLib.Efx;
using ReeLib.Efx.Structs.Basic;
using ReeLib.Efx.Structs.Common;
using ReeLib.Rcol;
using SmartFormat.Core.Parsing;

namespace ContentEditor.App.ImguiHandling.Rcol;

public class RcolEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler, IInspectorController, IRSZFileEditor, ISceneEditor
{
    public override string HandlerName => "RequestSetCollider";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public RcolFile File => Handle.GetFile<RcolFile>();

    public ContentWorkspace Workspace { get; }
    public RcolEditor RootEditor => context.FindHandlerInParents<RcolEditor>(true) ?? this;

    public RequestSetColliderComponent? Component => scene?.RootFolder.GameObjects.FirstOrDefault()?.GetComponent<RequestSetColliderComponent>();

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

    protected override void OnFileReverted()
    {
        Reset();
    }

    public RSZFile GetRSZFile() => File.RSZ;
    public Scene? GetScene() => scene;

    private void Reset()
    {
        if (context.children.Count > 0) {
            // not letting the child contexts dispose - so we don't dispose the file stream
            context.children.Clear();
        }
        if (primaryInspector != null) primaryInspector.Target = null!;
        failedToReadfile = false;
    }

    private Scene? LoadScene()
    {
        context.ClearChildren();
        if (scene == null) {
            var workspace = context.GetWorkspace()!;
            scene = context.GetNativeWindow()?.SceneManager.CreateScene(Handle, false, ((ISceneEditor)this).GetRootScene(context));
            if (Logger.ErrorIf(scene == null, "Failed to create new scene")) return null;
            var root = new GameObject("RcolRoot", workspace.Env, scene.RootFolder, scene);
            var rcol = root.AddComponent<RequestSetColliderComponent>();
            var group = RszInstance.CreateInstance(workspace.Env.RszParser, workspace.Env.RszParser.GetRSZClass("via.physics.RequestSetCollider.RequestSetGroup")!);
            RszFieldCache.RequestSetCollider.RequestSetGroups.Set(rcol.Data, [group]);
            RszFieldCache.RequestSetGroup.Resource.Set(group, Handle.NativePath ?? Handle.Filepath);
            scene.Add(root);
        }
        return scene;
    }

    protected override void DrawFileContents()
    {
        if (scene == null) {
            LoadScene();
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
    // protected override UIContext CreateElementContext(UIContext context, IList list, int elementIndex)
    // {
    //     var ctx = WindowHandlerFactory.CreateListElementContext(context, elementIndex);
    //     ctx.uiHandler = ItemHandler;
    //     return ctx;
    // }

    protected override string GetKey(RequestSet item) => item.Info.Name;

    protected override RequestSet? CreateItem(UIContext context, string key)
    {
        throw new NotImplementedException();
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

                // context.AddChild<RequestSet, RcolGroup>("Group", rset, getter: (i) => i!.Group, setter: (i, v) => i.Group = v).AddDefaultHandler<RcolGroup>();
                // context.AddChild<RequestSet, RszInstance>(
                //     "UserData",
                //     rset,
                //     new NestedUIHandlerStringSuffixed(new SwappableRszInstanceHandler("via.physics.RequestSetColliderUserData")),
                //     (i) => i!.Instance,
                //     setter: (i, v) => i.Instance = v);
                // context.AddChild<RequestSet, List<RszInstance>>("Shape userdata", rset, getter: (i) => i!.ShapeUserdata, setter: (i, v) => i.ShapeUserdata = v!).AddDefaultHandler<List<RszInstance>>();
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
        if (context.children.Count == 0) {
            var rset = context.Get<RequestSet>();
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

        context.ShowChildrenUI();
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
            context.AddChild<RcolGroup, List<Guid>>("Masks", group, getter: (i) => i!.Info.MaskGuids, setter: (i, v) => i.Info.MaskGuids = v).AddDefaultHandler<List<Guid>>();
            context.AddChild<RcolGroup, RszInstance>(
                "UserData",
                group,
                new NestedUIHandlerStringSuffixed(new SwappableRszInstanceHandler("via.physics.RequestSetColliderUserData")),
                (i) => i!.Info.UserData,
                setter: (i, v) => i.Info.UserData = v);
            context.AddChild<RcolGroup, List<RcolShape>>("Shapes", group, new ListHandler(typeof(RcolShape)) { CanCreateNewElements = false }, getter: (i) => i!.Shapes);
            context.AddChild<RcolGroup, List<RcolShape>>("ExtraShapes", group, new ListHandler(typeof(RcolShape)) { CanCreateNewElements = false }, getter: (i) => i!.ExtraShapes);
        }

        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(RcolShape))]
public class RcolShapeHandler : IObjectUIHandler, IUIContextEventHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(RcolShape));
        }
        context.ShowChildrenUI();
    }

    public bool HandleEvent(UIContext context, EditorUIEvent eventData)
    {
        if (eventData.IsChangeFromChild) {
            context.FindHandlerInParents<RcolEditor>()?.Component?.ReloadMeshes();
        }
        return true;
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
