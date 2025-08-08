using System.Reflection;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Efx;
using ReeLib.Efx.Structs.Basic;

namespace ContentEditor.App.ImguiHandling.Efx;

public class EfxEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler, IInspectorController
{
    public override string HandlerName => "EFX";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public EfxFile File => Handle.GetFile<EfxFile>();

    public ContentWorkspace Workspace { get; }
    public EfxEditor RootEfx => context.FindHandlerInParents<EfxEditor>(true) ?? this;

    protected override bool IsRevertable => context.Changed;

    private readonly List<ObjectInspector> inspectors = new();
    private ObjectInspector? primaryInspector;
    public object? PrimaryTarget => primaryInspector?.Target;

    public EfxEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    protected override void OnFileReverted()
    {
        Reset();
    }

    private void Reset()
    {
        if (context.children.Count > 0) {
            // not letting the child contexts dispose - so we don't dispose the file stream
            context.children.Clear();
        }
        failedToReadfile = false;
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            // var raw = context.AddChild("Raw data", File, new PlainObjectHandler());
            // WindowHandlerFactory.SetupObjectUIContext(raw, null, true);
            context.AddChild("Data", File, new EfxFileImguiHandler());
        }
        var p = ImGui.GetCursorPos();
        var s = ImGui.GetWindowSize();
        ImGui.BeginChild("Tree", new System.Numerics.Vector2(400, s.Y - p.Y - ImGui.GetStyle().WindowPadding.Y), ImGuiChildFlags.ResizeX);
        context.children[0].ShowUI();
        ImGui.EndChild();
        if (context.children.Count > 1 && primaryInspector != null) {
            ImGui.SameLine();
            // ImGui.CalcItemWidth();
            ImGui.BeginChild("Inspector");
            primaryInspector!.OnIMGUI();
            // context.children[1].ShowUI();
            ImGui.EndChild();
        }
    }

    public void SetPrimaryInspector(object target)
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

    private ObjectInspector AddEmbeddedInspector(object target)
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

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(EfxFile))]
public class EfxFileImguiHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFieldsRoot = [
        typeof(EfxFile).GetField(nameof(EfxFile.ExpressionParameters))!,
        typeof(EfxFile).GetField(nameof(EfxFile.FieldParameterValues))!,
        typeof(EfxFile).GetField(nameof(EfxFile.UvarGroups))!,
        typeof(EfxFile).GetProperty(nameof(EfxFile.Bones))!,
        typeof(EfxFile).GetField(nameof(EfxFile.Actions))!,
        typeof(EfxFile).GetProperty(nameof(EfxFile.Entries))!,
    ];

    private static MemberInfo[] DisplayedFieldsSub = [
        typeof(EfxFile).GetField(nameof(EfxFile.FieldParameterValues))!,
        typeof(EfxFile).GetProperty(nameof(EfxFile.Bones))!,
        typeof(EfxFile).GetProperty(nameof(EfxFile.Entries))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<EfxFile>();
            if (file.parentFile == null) {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(EfxFile), members: DisplayedFieldsRoot);
            } else {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(EfxFile), members: DisplayedFieldsSub);
            }
        }
        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(EFXEntry))]
[ObjectImguiHandler(typeof(EFXAction))]
public class EFXEntryImguiHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(EFXEntryBase).GetField(nameof(EFXEntryBase.name))!,
        typeof(EFXEntry).GetField(nameof(EFXEntry.entryAssignment))!,
        typeof(EFXEntry).GetField(nameof(EFXEntry.index))!,
        typeof(EFXEntry).GetProperty(nameof(EFXEntry.Groups))!,
        typeof(EFXEntryBase).GetProperty(nameof(EFXEntryBase.Attributes))!,
    ];

    private static MemberInfo[] DisplayedFieldsActions = [
        typeof(EFXEntryBase).GetField(nameof(EFXEntryBase.name))!,
        typeof(EFXAction).GetField(nameof(EFXAction.actionUnkn0))!,
        typeof(EFXEntryBase).GetProperty(nameof(EFXEntryBase.Attributes))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var entry = context.Get<EFXEntryBase>();
        if (context.children.Count == 0) {
            if (entry.Contains(EfxAttributeType.PlayEmitter)) {
                context.AddChild("TODO", entry, new ReadOnlyLabelHandler());
            } else if (entry is EFXEntry) {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(EFXEntry), members: DisplayedFields);
            } else {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(EFXAction), members: DisplayedFieldsActions);
            }
        }
        context.ShowChildrenUI();
    }
}

public class EfxEntriesListEditorBase<TType> : DictionaryListImguiHandler<string, TType, List<TType>> where TType : EFXEntryBase, new()
{
    public EfxEntriesListEditorBase()
    {
        Filterable = true;
        FlatList = true;
    }

    protected override bool Filter(UIContext context, string filter)
    {
        var obj = context.Get<TType>();
        if (obj.name == null) return true;

        if (obj.name.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        foreach (var attr in obj.Attributes) {
            if (attr.type.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    protected override TType? CreateItem(UIContext context, string key)
    {
        var ws = context.GetWorkspace();
        if (ws == null) {
            return null;
        }

        var version = ws.Env.EfxVersion;
        var owner = context.FindValueInParentValues<EfxFile>();
        if (typeof(TType) == typeof(EFXEntry)) {
            return new EFXEntry() { name = key, index = owner == null || owner.Entries.Count == 0 ? 1 : (owner.Entries.Max(e => e.index) + 1), Version = version } as TType;
        } else {
            return new EFXAction() { name = key, Version = version } as TType;
        }
    }

    protected override string GetKey(TType item)
    {
        return item.name ?? "New entry";
    }

    protected override void AddItemHandler(UIContext item)
    {
        var action = item.Cast<EFXAction>();
        if (action != null && action.TryGet<EFXAttributePlayEmitter>(out var emitter)) {
            var child = item.AddChild("PlayEmitter", emitter.efxrData, new BoxedUIHandler(new EfxFileImguiHandler()));
            item.uiHandler = new NestedUIHandler(child.uiHandler!);
            return;
        }
        item.uiHandler = new EntryNodeHandler();
    }

    protected override void PostItem(UIContext itemContext)
    {
        if (ImGui.BeginPopupContextItem(itemContext.label)) {
            ImGui.Text("TODO");
            ImGui.EndPopup();
        }
    }

    private class EntryNodeHandler : IObjectUIHandler
    {
        public void OnIMGUI(UIContext context)
        {
            var node = context.Get<EFXEntryBase>();
            var inspector = context.FindHandlerInParents<IInspectorController>();
            if (ImGui.Selectable(context.label, node == inspector?.PrimaryTarget)) {
                OnSelect(context, node);
            }
            var typeAttr = node.TypeAttribute;
            if (typeAttr != null) {
                ImGui.SameLine();
                ImGui.TextColored(Colors.Faded, typeAttr.type.ToString());
            }
        }

        protected virtual void OnSelect(UIContext context, EFXEntryBase entry)
        {
            var efx = context.FindHandlerInParents<EfxEditor>()?.RootEfx;
            if (efx == null) {
                Logger.Error("UI structure invalid, EFX file not found");
                return;
            }

            efx.SetPrimaryInspector(entry);
        }
    }
}
[ObjectImguiHandler(typeof(List<EFXEntry>))]
public class EfxEntriesListEditor : EfxEntriesListEditorBase<EFXEntry> {}

[ObjectImguiHandler(typeof(List<EFXAction>))]
public class EfxActionsListEditor : EfxEntriesListEditorBase<EFXAction>
{
    public EfxActionsListEditor()
    {
        base.FlatList = false;
    }
}

[ObjectImguiHandler(typeof(EFXAttribute), Inherited = true)]
public class EFXAttributeImguiHandler : IObjectUIHandler
{
    private static MemberInfo[] BasicFields = [
        typeof(EFXAttribute).GetField(nameof(EFXAttribute.unknSeqNum))!,
        typeof(EFXAttribute).GetProperty(nameof(EFXAttribute.NodePosition))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var entry = context.Get<EFXAttribute>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            var entryType = entry.GetType();
            if (ws == null) {
                WindowHandlerFactory.SetupObjectUIContext(context, entryType);
            } else {
                if (ws.Env.EfxCacheData.AttributeTypes.TryGetValue(entry.type.ToString(), out var data)) {
                    WindowHandlerFactory.SetupObjectUIContext(context, entryType, members: BasicFields);
                    var showFields = data.FieldInfos
                        .Where(fi => data.Fields[fi.Name].Flag is EfxFieldFlags.None or EfxFieldFlags.BitSet)
                        .ToArray();
                    WindowHandlerFactory.SetupObjectUIContext(context, entryType, members: showFields);
                } else {
                    WindowHandlerFactory.SetupObjectUIContext(context, entryType);
                }
            }
        }
        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(List<EFXAttribute>))]
public class EfxAttributeListEditor : DictionaryListImguiHandler<EfxAttributeType, EFXAttribute, List<EFXAttribute>>
{
    public EfxAttributeListEditor()
    {
        Filterable = true;
        FlatList = true;
    }

    private static readonly NestedUIHandlerStringSuffixed NestedAttributeHandler = new NestedUIHandlerStringSuffixed(new EFXAttributeImguiHandler());

    protected override bool Filter(UIContext context, string filter)
    {
        var obj = context.Get<EFXAttribute>();
        return obj.type.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    protected override EFXAttribute? CreateItem(UIContext context, EfxAttributeType key)
    {
        var ws = context.GetWorkspace();
        if (ws == null) {
            return null;
        }

        var entry = context.FindValueInParentValues<EFXEntry>();

        var version = ws.Env.EfxVersion;
        var newAttr = EFXAttribute.Create(version, key);
        if (entry == null) return newAttr;

        // add it directly so it gets placed at the right index and not at the end
        if (entry.AddAttribute(newAttr)) {
            context.children.RemoveAtAfter(entry.Attributes.IndexOf(newAttr) + 1);
        }
        return null;
    }

    protected override EfxAttributeType GetKey(EFXAttribute item)
    {
        return item.type;
    }

    protected override IObjectUIHandler CreateNewItemInput(UIContext context)
    {
        var entry = context.FindValueInParentValues<EFXEntry>();
        if (entry == null) return base.CreateNewItemInput(context);

        var types = EfxAttributeTypeRemapper.GetAllTypes(entry.Version).ToDictionary(kv => (object)kv.Value, kv => kv.Value.ToString());
        return new CsharpEnumHandler(typeof(EfxAttributeType), types) { NoUndoRedo = true };
    }

    protected override void InitChildContext(UIContext itemContext)
    {
        itemContext.uiHandler = NestedAttributeHandler;
    }

    protected override void PostItem(UIContext itemContext)
    {
        if (ImGui.BeginPopupContextItem(itemContext.label)) {

            ImGui.EndPopup();
        }
    }
}