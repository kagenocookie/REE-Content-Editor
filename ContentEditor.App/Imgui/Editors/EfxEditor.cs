using System.Reflection;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.Efx;
using ReeLib.Efx.Structs.Basic;
using ReeLib.Efx.Structs.Common;

namespace ContentEditor.App.ImguiHandling.Efx;

public class EfxEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler, IInspectorController
{
    public override string HandlerName => "EFX";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public EfxFile File => Handle.GetFile<EfxFile>();

    public ContentWorkspace Workspace { get; }
    public EfxEditor RootEditor => context.FindHandlerInParents<EfxEditor>(true) ?? this;

    protected override bool IsRevertable => context.Changed;

    private readonly List<ObjectInspector> inspectors = new();
    private ObjectInspector? primaryInspector;
    public object? PrimaryTarget => primaryInspector?.Target;

    public EfxEditor(ContentWorkspace env, FileHandle file) : base(file)
    {
        Workspace = env;
    }

    protected override void Reset()
    {
        base.Reset();
        if (primaryInspector != null) primaryInspector.Target = null!;
    }

    protected override void DrawFileControls(WindowData data)
    {
        base.DrawFileControls(data);
        ImGui.SameLine();
        ShowFileJsonCopyPasteButtons<EfxFile>(EfxJsonTypeResolver.jsonOptions);
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild("Data", File, new EfxFileImguiHandler());
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

[ObjectImguiHandler(typeof(EfxFile), Stateless = true)]
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

[ObjectImguiHandler(typeof(EFXEntry), Stateless = true)]
[ObjectImguiHandler(typeof(EFXAction), Stateless = true)]
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
            if (entry is EFXEntry) {
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

    protected override void InitChildContext(UIContext itemContext)
    {
        base.InitChildContext(itemContext);
        itemContext.label = $"[{itemContext.parent!.Get<List<TType>>().IndexOf(itemContext.Get<TType>())}] {itemContext.label}";
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
            var item = itemContext.Get<TType>();
            var list = itemContext.parent!.Get<List<TType>>();
            if (ImGui.Selectable("Copy")) {
                VirtualClipboard.CopyToClipboard(item.DeepCloneGeneric<TType>());
                ImGui.CloseCurrentPopup();
            }
            if (VirtualClipboard.TryGetFromClipboard<TType>(out var pasteSource)) {
                if (pasteSource.Version != item.Version) {
                    ImGui.BeginDisabled();
                    ImGui.Selectable("Paste unavailable due to source EFX version mismatch");
                    ImGui.EndDisabled();
                } else {
                    if (ImGui.Selectable("Paste (replace)")) {
                        var clone = pasteSource.DeepCloneGeneric<TType>();
                        clone.name = item.name;
                        UndoRedo.RecordSet(itemContext, clone);
                        itemContext.ClearChildren();
                        itemContext.parent.FindHandlerInParents<EfxEditor>()?.SetPrimaryInspector(clone);
                        ImGui.CloseCurrentPopup();
                    }
                    if (ImGui.Selectable("Paste (new)")) {
                        var clone = pasteSource.DeepCloneGeneric<TType>();
                        clone.name = (clone.name ?? "New_entry").GetUniqueName((x) => list.Any(e => e.name == x), "copy");
                        UndoRedo.RecordListInsert(itemContext.parent, list, clone, list.IndexOf(item) + 1);
                        itemContext.parent.ClearChildren();
                        itemContext.parent.FindHandlerInParents<EfxEditor>()?.SetPrimaryInspector(clone);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
            if (ImGui.Selectable("Duplicate")) {
                var clone = item.Clone();
                clone.name = (clone.name ?? "New_entry").GetUniqueName((x) => list.Any(e => e.name == x), "copy");
                UndoRedo.RecordListInsert(itemContext.parent, list, clone, list.IndexOf(item) + 1);
                itemContext.parent.FindHandlerInParents<EfxEditor>()?.SetPrimaryInspector(clone);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Selectable("Delete")) {
                UndoRedo.RecordListRemove(itemContext.parent, list, item);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private class EntryNodeHandler : IObjectUIHandler
    {
        public void OnIMGUI(UIContext context)
        {
            var node = context.Get<EFXEntryBase>();
            ImGui.BeginGroup();
            var inspector = context.FindHandlerInParents<IInspectorController>();
            if (ImGui.Selectable(context.label, node == inspector?.PrimaryTarget)) {
                OnSelect(context, node);
            }
            var typeAttr = node.TypeAttribute;
            if (typeAttr != null) {
                ImGui.SameLine();
                ImGui.TextColored(Colors.Faded, typeAttr.type.ToString());
            }
            ImGui.EndGroup();
        }

        protected virtual void OnSelect(UIContext context, EFXEntryBase entry)
        {
            var efx = context.FindHandlerInParents<EfxEditor>()?.RootEditor;
            if (efx == null) {
                Logger.Error("UI structure invalid, EFX file not found");
                return;
            }

            efx.SetPrimaryInspector(entry);
        }
    }
}
[ObjectImguiHandler(typeof(List<EFXEntry>), Stateless = true)]
public class EfxEntriesListEditor : EfxEntriesListEditorBase<EFXEntry> { }

[ObjectImguiHandler(typeof(List<EFXAction>), Stateless = true)]
public class EfxActionsListEditor : EfxEntriesListEditorBase<EFXAction>
{
    public EfxActionsListEditor()
    {
        base.FlatList = false;
    }
}

[ObjectImguiHandler(typeof(EFXAttribute), Stateless = true, Inherited = true, Priority = 10)]
public class EFXAttributeImguiHandler : IObjectUIHandler
{
    private static MemberInfo[] BasicFields = [
        typeof(EFXAttribute).GetField(nameof(EFXAttribute.UniqueID))!,
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
                if (ws.Env.EfxCacheData.EnumAttributeTypes.TryGetValue(entry.type, out var data)) {
                    AddBaseFields(context, entryType);
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

    public static void AddBaseFields(UIContext context, Type entryType)
    {
        WindowHandlerFactory.SetupObjectUIContext(context, entryType, members: BasicFields);
    }
}

[ObjectImguiHandler(typeof(List<EFXAttribute>), Stateless = true)]
public class EfxAttributeListEditor : DictionaryListImguiHandler<EfxAttributeType, EFXAttribute, List<EFXAttribute>>
{
    public EfxAttributeListEditor()
    {
        Filterable = true;
        FlatList = true;
    }

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
            var index = entry.Attributes.IndexOf(newAttr) + 1;
            UndoRedo.RecordCallback(context, () => {
                if (!entry.Attributes.Contains(newAttr)) {
                    entry.AddAttribute(newAttr);
                }
            }, () => entry.Attributes.Remove(newAttr));
            UndoRedo.AttachCallbackToLastAction(UndoRedo.CallbackType.Both, () => context.children.RemoveAtAfter(index));
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
        var handler = WindowHandlerFactory.CreateUIHandler(itemContext.GetRaw(), null);
        itemContext.uiHandler = new EfxAttributeContextHandler(handler);
    }

    private sealed class EfxAttributeContextHandler(IObjectUIHandler handler) : TreeContextUIHandler(handler)
    {
        protected override void HandleContextMenu(UIContext context)
        {
            if (ImGui.BeginPopupContextItem(context.label)) {
                if (ImGui.Button("Delete")) {
                    var list = context.parent!.Get<List<EFXAttribute>>();
                    UndoRedo.RecordListRemove(context.parent, list, context.Get<EFXAttribute>(), null, 1);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }
    }
}

[ObjectImguiHandler(typeof(IExpressionAttribute), Priority = 5)]
public class EfxExpressionAttributeEditor : IObjectUIHandler
{
    private readonly Dictionary<int, string> pendingStrings = new();
    private readonly Dictionary<int, string> confirmedStrings = new();

    public void OnIMGUI(UIContext context)
    {
        var attr = context.Get<EFXAttribute>();
        if (context.children.Count == 0) {
            EFXAttributeImguiHandler.AddBaseFields(context, attr.GetType());
        }

        context.ShowChildrenUI();
        var data = (IExpressionAttribute)attr;
        if (data.Expression == null) {
            if (ImGui.Button("Create expression")) {
                data.Expression = new(attr.Version);
            }
            return;
        }
        var ws = context.GetWorkspace();
        var efx = context.FindHandlerInParents<EfxEditor>()?.RootEditor.File;
        if (efx == null || ws == null) {
            ImGui.TextColored(Colors.Error, "EFX file or workspace not found");
            return;
        }

        var bits = data.ExpressionBits;
        if (data.Expression.ParsedExpressions == null) {
            data.Expression.ParsedExpressions = EfxExpressionTreeUtils.ReconstructExpressionTreeList(data.Expression.Expressions, efx);
        }

        var set = 0;
        var w = ImGui.CalcItemWidth();
        var typeInfo = ws.Env.EfxCacheData.EnumAttributeTypes[attr.type];
        for (int i = 0; i < bits.BitCount; ++i) {
            int myBitIndex = i;
            var name = bits.GetBitName(myBitIndex) ?? ("Field " + (i + 1));
            var enabled = data.ExpressionBits.HasBit(myBitIndex);
            // Velocity3DExpression has some extra floats at the start for whatever reason
            var fieldInfoStartIndex = attr.type == EfxAttributeType.Velocity3DExpression && attr.Version != EfxVersion.RE8 ? 5 : 1;
            ImGui.PushID(i);
            ImGui.SetNextItemWidth(60);
            if (ImGui.Checkbox("##enabled", ref enabled)) {
                void ToggleBit(int bit, bool toggle)
                {
                    var index = bits.GetBitInsertIndex(bit);
                    bits.SetBit(bit, toggle);
                    if (toggle) {
                        data.Expression!.expressions.Insert(index, new EFXExpressionObject(attr.Version));
                        data.Expression!.ParsedExpressions!.Insert(index, new EFXExpressionTree());
                    } else {
                        data.Expression!.expressions.RemoveAt(index);
                        data.Expression!.ParsedExpressions!.RemoveAt(index);
                    }
                }
                UndoRedo.RecordCallback(context, () => ToggleBit(myBitIndex, enabled), () => ToggleBit(myBitIndex, !enabled));
            }
            ImGui.SameLine();
            ImGui.Text(name);
            if (enabled) {
                var fieldInfo = typeInfo.FieldInfos[fieldInfoStartIndex + i];
                var parsed = data.Expression.ParsedExpressions[set++];
                var assign = (ExpressionAssignType)fieldInfo.GetValue(data)!;
                var prevAssign = assign;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(140);
                if (ImguiHelpers.CSharpEnumCombo<ExpressionAssignType>("##assign", ref assign)) {
                    UndoRedo.RecordCallbackSetter(context, data, prevAssign, assign, (d, v) => fieldInfo.SetValue(d, v));
                }
                if (!confirmedStrings.TryGetValue(myBitIndex, out var orgStr)) {
                    confirmedStrings[myBitIndex] = orgStr = parsed.root.ToString()!;
                }
                var str = pendingStrings.GetValueOrDefault(myBitIndex) ?? orgStr;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(30);
                var showParams = ImGui.TreeNode("##showParams");
                ImGui.SameLine();
                if (ImguiHelpers.TextMultilineAutoResize("##expression", ref str, w - 230, 300, UI.FontSize)) {
                    pendingStrings[myBitIndex] = str;
                }
                if (orgStr != str) {
                    if (ImGui.Button("Revert")) {
                        pendingStrings.Remove(myBitIndex);
                    }
                    ImGui.SameLine();
                    var err = GetExpressionError(str, parsed.parameters);
                    if (err == null) {
                        if (ImGui.Button("Confirm")) {
                            void UpdateExpression(string text)
                            {
                                var index = bits.GetBitInsertIndex(myBitIndex);
                                var parsed = EfxExpressionStringParser.Parse(text, data.Expression!.expressions[index].parameters ?? new(0));
                                data.Expression!.expressions[index].parameters = parsed.parameters.ToList();
                                EfxExpressionTreeUtils.FlattenExpressions(data.Expression!.expressions[index].components, parsed, efx);
                                data.Expression!.ParsedExpressions![index] = parsed;
                                confirmedStrings[myBitIndex] = text;
                            }
                            UndoRedo.RecordCallback(context, () => UpdateExpression(str), () => UpdateExpression(orgStr));
                        }
                    } else {
                        ImGui.TextColored(Colors.Error, err);
                    }
                }

                if (showParams) {
                    for (int paramIndex = 0; paramIndex < parsed.parameters.Count; paramIndex++) {
                        var param = parsed.parameters[paramIndex];
                        var paramName = param.GetName(efx) ?? "Hash: " + param.parameterNameHash;
                        ImGui.PushID(paramIndex);
                        var prevSource = param.source;
                        ImGui.SetNextItemWidth(120);
                        if (ImguiHelpers.CSharpEnumCombo<ExpressionParameterSource>("##source", ref prevSource)) {
                            var updated = param with { source = prevSource };
                            UndoRedo.RecordCallbackSetter(
                                context, (parsed.parameters, paramIndex), param, updated,
                                (listIndex, value) => listIndex.parameters[listIndex.paramIndex] = value,
                                $"Param {paramName} source");
                        }
                        ImGui.SameLine();
                        if (param.source != ExpressionParameterSource.Parameter) {
                            var value = param.constantValue;
                            ImGui.SetNextItemWidth(w - 120);
                            if (ImGui.DragFloat(paramName + (param.source == ExpressionParameterSource.Constant ? " (Value)" : " (Default value)"), ref value, 0.001f)) {
                                var updated = param with { constantValue = value };
                                UndoRedo.RecordCallbackSetter(
                                    context, (parsed.parameters, paramIndex), param, updated,
                                    (listIndex, value) => listIndex.parameters[listIndex.paramIndex] = value,
                                    $"Param {paramName} value");
                            }
                        } else {
                            var efxParam = efx.FindParameterByHash(param.parameterNameHash);
                            ImGui.Text(paramName + " = " + efxParam);
                        }
                        ImGui.PopID();
                    }
                    ImGui.TreePop();
                }
            }
            ImGui.PopID();
        }
    }

    private static string? GetExpressionError(string text, IEnumerable<EFXExpressionParameterName> parameters)
    {
        try {
            var expression = EfxExpressionStringParser.Parse(text, parameters.ToArray());
            return null;
        } catch (Exception e) {
            return "Syntax error: " + e.Message;
        }
    }
}
