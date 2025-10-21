using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Il2cpp;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App.ImguiHandling;

public class RszInstanceHandler : Singleton<RszInstanceHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context, bool showLabel)
    {
        var instance = context.Get<RszInstance>();
        if (instance == null) {
            ImGui.Text(context.label + ": NULL");
            return;
        }
        if (showLabel) ImguiHelpers.TextSuffix(context.label, context.stringFormatter?.GetString(instance) ?? instance.RszClass.name);

        if (context.children.Count >= 10) {
            ImGui.Indent(8);
            context.state ??= "";
            ImGui.SetNextItemWidth(Math.Min(200, ImGui.CalcItemWidth() - 16));
            ImGui.InputText("Filter fields", ref context.state, 48);
            ImGui.Unindent(8);
            ImGui.Spacing();
        }
        if (string.IsNullOrEmpty(context.state)) {
            context.ShowChildrenUI();
        } else {
            foreach (var child in context.children) {
                if (child.label.Contains(context.state, StringComparison.InvariantCultureIgnoreCase)) {
                    child.ShowUI();
                }
            }
        }
    }
    public void OnIMGUI(UIContext context)
    {
        OnIMGUI(context, true);
    }

    public static bool ShowDefaultTooltip(UIContext context)
    {
        var popupClicked = false;
        if (ImGui.BeginPopupContextItem(context.label)) {
            if (RszInstanceHandler.ShowTooltipActions(context)) {
                ImGui.CloseCurrentPopup();
                popupClicked = true;
            }
            ImGui.EndPopup();
        }
        return popupClicked;
    }

    public static bool ShowTooltipActions(UIContext context)
    {
        if (ImGui.Selectable("Copy as JSON")) {
            var ws = context.GetWorkspace();
            if (ws != null) {
                EditorWindow.CurrentWindow?.CopyToClipboard(JsonSerializer.Serialize(context.GetRaw(), ws.Env.JsonOptions)!, "Copied!");
            }
            return true;
        }
        var instance = context.Cast<RszInstance>();
        if (instance != null) {
            var clipboard = EditorWindow.CurrentWindow?.GetClipboard();
            if (!string.IsNullOrEmpty(clipboard)) {
                if (ImGui.Selectable("Paste JSON (replace value)")) {
                    var env = context.GetWorkspace()!;
                    try {
                        var newJson = JsonSerializer.Deserialize<JsonNode>(clipboard, env.Env.JsonOptions)!;
                        var prevJson = instance.ToJson(env.Env);
                        UndoRedo.RecordCallbackSetter(context, instance, prevJson, newJson, (target, json) => env.Diff.ApplyDiff(target, json));
                    } catch (Exception e) {
                        Logger.Error(e, "Invalid JSON");
                    }
                    return true;
                }
            }
        }
        if (context.parent?.uiHandler is ArrayRSZHandler array && instance != null) {
            if (ImGui.Selectable("Duplicate")) {
                var clone = instance.Clone();
                var parentList = context.parent.Get<IList>();
                UndoRedo.RecordListAdd(context.parent, parentList, clone);
                return true;
            }
        }
        if (context.uiHandler is ArrayRSZHandler thisArray && context.TryCast<IList>(out var list)) {
            var elementType = thisArray.GetElementClassnameType(context);
            // note: UserData copy is not supported atm because it behaves differently from plain RszInstances
            var type = thisArray.Field.type == RszFieldType.UserData ? null : RszInstance.RszFieldTypeToRuntimeCSharpType(thisArray.Field.type);
            if (type != null) {
                var clipboard = EditorWindow.CurrentWindow?.GetClipboard();
                if (!string.IsNullOrEmpty(clipboard)) {
                    if (ImGui.Selectable("Paste JSON")) {
                        var env = context.GetWorkspace()!.Env;
                        try {
                            var newItems = ((IList)(JsonSerializer.Deserialize(clipboard, typeof(List<>).MakeGenericType(type), env.JsonOptions)!)).Cast<object>().ToList();
                            foreach (var item in newItems) {
                                if (item is RszInstance rsz && elementType != null && !env.TypeCache.IsAssignableTo(rsz.RszClass.name, elementType)) {
                                    Logger.Error("Unsupported type " + rsz.RszClass.name + " for array element type " + elementType);
                                    return true;
                                }
                            }
                            UndoRedo.RecordSet(context, newItems);
                        } catch (Exception) {
                            Logger.Error($"Could not deserialize JSON as {elementType} list");
                        }
                        return true;
                    }
                }
            }
        }

        if (WindowHandlerFactory.ShowCustomActions(context)) {
            return true;
        }
        return false;
    }
}

public class RszClassnamePickerHandler(string? baseClass = null, string label = "Classname") : IObjectUIHandler
{
    private static readonly RszInstanceHandler inner = new();
    private string[]? classOptions;
    private HashSet<string>? classOptionsSet;
    private string? classInput;

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<RszInstance?>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            if (ws != null && !string.IsNullOrEmpty(baseClass)) {
                classOptions = ws.Env.TypeCache.GetSubclasses(baseClass).ToArray();
                classOptionsSet = classOptions.ToHashSet();
            }
            if (instance != null) {
                WindowHandlerFactory.SetupRSZInstanceHandler(context);
            }
        }

        if (classOptions?.Length > 1) {
            classInput ??= instance?.RszClass.name ?? string.Empty;
            ImguiHelpers.FilterableCombo(label, classOptions, classOptions, ref classInput, ref context.state);
            if (!string.IsNullOrEmpty(classInput) && classInput != instance?.RszClass.name) {
                if (ImGui.Button("Change")) {
                    var ws = context.GetWorkspace();
                    var cls = ws!.Env.RszParser.GetRSZClass(classInput);
                    if (cls == null) {
                        Logger.Error("Invalid classname " + classInput);
                    } else {
                        var newInstance = RszInstance.CreateInstance(ws!.Env.RszParser, cls);
                        UndoRedo.RecordSet(context, newInstance, postChangeAction: (ctx) => {
                            ctx.ClearChildren();
                            WindowHandlerFactory.SetupRSZInstanceHandler(ctx);
                            classInput = null;
                        }, mergeMode: UndoRedoMergeMode.NeverMerge);
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel")) {
                    classInput = null;
                }
            }
        }

        inner.OnIMGUI(context);
    }
}

public class SwappableRszInstanceHandler(string? baseClass = null, bool referenceOnly = false, string instanceLabel = "Instance") : IObjectUIHandler
{
    private static readonly RszInstanceHandler inner = new();
    private string[]? classOptions;
    private HashSet<string>? classOptionsSet;
    private string? classInput;

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<RszInstance?>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            if (ws != null && !string.IsNullOrEmpty(baseClass)) {
                classOptions = ws.Env.TypeCache.GetSubclasses(baseClass).ToArray();
                classOptionsSet = classOptions.ToHashSet();
            }
            if (instance != null && !referenceOnly) {
                WindowHandlerFactory.SetupRSZInstanceHandler(context);
            }
        }

        if (classOptions != null && classOptions.Length > 1) {
            classInput ??= instance?.RszClass.name ?? string.Empty;
            ImguiHelpers.FilterableCombo("Class", classOptions, classOptions, ref classInput, ref context.state);
            if (!string.IsNullOrEmpty(classInput) && classInput != instance?.RszClass.name) {
                if (ImGui.Button("Change")) {
                    var ws = context.GetWorkspace();
                    var cls = ws!.Env.RszParser.GetRSZClass(classInput);
                    if (cls == null) {
                        Logger.Error("Invalid classname " + classInput);
                    } else {
                        var newInstance = RszInstance.CreateInstance(ws!.Env.RszParser, cls);
                        UndoRedo.RecordSet(context, newInstance, postChangeAction: (ctx) => {
                            ctx.ClearChildren();
                            WindowHandlerFactory.SetupRSZInstanceHandler(ctx);
                            classInput = null;
                        }, mergeMode: UndoRedoMergeMode.NeverMerge);
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel")) {
                    classInput = null;
                }
            }
        }

        var rsz = context.FindHandlerInParents<IRSZFileEditor>()?.GetRSZFile();
        if (rsz != null) {
            RszInstance[] otherInstanceOptions;
            if (classOptionsSet != null) {
                otherInstanceOptions = rsz.InstanceList.Where(cls => classOptionsSet.Contains(cls.RszClass.name)).Take(500).ToArray();
            } else {
                otherInstanceOptions = rsz.InstanceList.Take(500).ToArray();
            }
            var labels = otherInstanceOptions.Select(inst => inst.GetString()).ToArray();

            var newInstance = instance;
            if (ImguiHelpers.FilterableCombo(instanceLabel, labels, otherInstanceOptions, ref newInstance, ref context.state)) {
                if (newInstance?.RszClass != instance?.RszClass) {
                    UndoRedo.RecordSet(context, newInstance, (ctx) => {
                        ctx.ClearChildren();
                        WindowHandlerFactory.SetupRSZInstanceHandler(ctx);
                        classInput = null;
                    }, mergeMode: UndoRedoMergeMode.NeverMerge);
                } else {
                    UndoRedo.RecordSet(context, newInstance, mergeMode: UndoRedoMergeMode.NeverMerge);
                }
            }
        }

        if (!referenceOnly) {
            inner.OnIMGUI(context);
        }
    }
}

public class InstancePickerHandler<T>(bool allowNull, Func<UIContext, bool, IEnumerable<T>> instanceProvider, Action<UIContext, T?>? instanceSwapper = null) : IObjectUIHandler
{
    public bool DisableRefresh { get; init; }

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<T>();
        var availableInstances = instanceProvider.Invoke(context, false).ToArray();
        var labels = availableInstances.Select(inst => inst?.ToString() ?? "<NULL>").ToArray();

        var restW = ImGui.CalcItemWidth();
        if (allowNull && instance != null) {
            if (ImGui.Button("Remove")) {
                UndoRedo.RecordSet(context, default(T), mergeMode: UndoRedoMergeMode.NeverMerge);
            }
            ImGui.SameLine();
            restW -= ImGui.CalcTextSize("Remove").X + ImGui.GetStyle().FramePadding.X * 2;
        }
        if (!DisableRefresh) {
            if (ImGui.Button("Refresh list")) {
                _ = instanceProvider.Invoke(context, true).ToArray();
            }
            restW -= ImGui.CalcTextSize("Refresh list").X + ImGui.GetStyle().FramePadding.X * 2;
            ImGui.SameLine();
        }

        ImGui.SetNextItemWidth(restW);
        var newInstance = instance;
        if (ImguiHelpers.FilterableCombo(context.label, labels, availableInstances, ref newInstance, ref context.state)) {
            if (instanceSwapper != null) {
                instanceSwapper.Invoke(context, newInstance);
            } else {
                UndoRedo.RecordSet(context, newInstance, mergeMode: UndoRedoMergeMode.NeverMerge);
            }
        }
    }
}

public class NestedRszInstanceHandler : IObjectUIHandler
{
    public bool ForceDefaultClose { get; set; }
    private bool _wasInit = false;
    private readonly RszField? field;

    public NestedRszInstanceHandler()
    {
    }

    public NestedRszInstanceHandler(RszField field)
    {
        this.field = field;
    }

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<RszInstance>();
        if (instance == null) {
            ImGui.Text(context.label + ": NULL");

            if (string.IsNullOrEmpty(field?.original_type)) return;

            var ws = context.GetWorkspace();
            if (ws == null) return;

            ImGui.SameLine();
            ImGui.PushID(context.label);
            if (ImGui.Button("Create")) {
                var cls = ws.Env.RszParser.GetRSZClass(field.original_type);
                if (cls == null) {
                    Logger.Error("Class not found");
                } else {
                    UndoRedo.RecordSet(context, RszInstance.CreateInstance(ws.Env.RszParser, cls));
                }
            }
            ImGui.PopID();
            return;
        }
        if (!_wasInit) {
            _wasInit = true;
            context.stringFormatter = WindowHandlerFactory.GetStringFormatter(instance);
        }
        if (instance.Fields.Length == 0) {
            // no point in showing it in the UI - at least until we add subclass selection
            return;
        }
        if (!ForceDefaultClose && instance.Fields.Length <= AppConfig.Instance.AutoExpandFieldsCount) {
            ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
        }
        var show = ImguiHelpers.TreeNodeSuffix(context.label, context.stringFormatter?.GetString(instance) ?? instance.RszClass.name);
        RszInstanceHandler.ShowDefaultTooltip(context);
        if (show) {
            if (context.children.Count == 0) {
                if (instance.RSZUserData != null) {
                    context.AddChild(context.label, instance, new UserDataReferenceHandler());
                } else {
                    WindowHandlerFactory.SetupRSZInstanceHandler(context);
                }
            }
            ImGui.PushID(context.GetRaw()!.GetHashCode());
            RszInstanceHandler.Instance.OnIMGUI(context, false);
            ImGui.PopID();
            ImGui.TreePop();
        }
    }
}

public class ArrayRSZHandler : BaseListHandler, ITooltipHandler
{
    private RszField field;
    public RszField Field => field;

    public ArrayRSZHandler(RszField field)
    {
        this.field = field;
        CanCreateNewElements = true;
    }

    public bool HandleTooltip(UIContext context)
    {
        return RszInstanceHandler.ShowDefaultTooltip(context);
    }

    protected override UIContext CreateElementContext(UIContext context, IList list, int elementIndex)
    {
        var ctx = WindowHandlerFactory.CreateListElementContext(context, elementIndex);
        WindowHandlerFactory.CreateRSZFieldElementHandler(ctx, field);
        if (list.Count > 300 && ctx.uiHandler is NestedRszInstanceHandler lazy) {
            lazy.ForceDefaultClose = true;
        }
        return ctx;
    }

    public string? GetElementClassnameType(UIContext context)
    {
        if (string.IsNullOrEmpty(field.original_type)) {
            var first = context.Get<IList<object>>().FirstOrDefault() as RszInstance;
            return first?.RszClass.name;
        } else {
            return RszInstance.GetElementType(field.original_type);
        }
    }

    protected override object? CreateNewElement(UIContext context)
    {
        var env = context.GetWorkspace();
        if (env == null) return null;
        string? classname = GetElementClassnameType(context);
        if (classname == null) {
            Logger.Error("Could not determine array element type");
            return null;
        }
        return RszInstance.CreateArrayItem(env.Env.RszParser, field, classname);
    }
}

public class EnumFieldHandler : IObjectUIHandler
{
    private EnumDescriptor enumDescriptor;

    public EnumFieldHandler(EnumDescriptor enumDescriptor)
    {
        this.enumDescriptor = enumDescriptor;
    }

    private struct RszEnumSource : IEnumDataSource
    {
        public EnumDescriptor descriptor;
        public string[] GetLabels() => descriptor.GetDisplayLabels();
        public object[] GetValues() => descriptor.GetValues();
    }

    public void OnIMGUI(UIContext context)
    {
        var selected = context.GetRaw();
        var enumsrc = new RszEnumSource { descriptor = enumDescriptor };
        if (ImguiHelpers.FilterableEnumCombo(context.label, enumsrc, ref selected, ref context.state)) {
            UndoRedo.RecordSet(context, selected);
        }
        AppImguiHelpers.ShowDefaultCopyPopup(selected, selected!.GetType(), context);
    }
}

public class FlagsEnumFieldHandler : IObjectUIHandler
{
    private EnumDescriptor enumDescriptor;
    private ImGuiDataType scalarType;

    private static ImGuiDataType TypeToImguiDataType(Type type)
    {
        if (type == typeof(short)) return ImGuiDataType.S16;
        if (type == typeof(ushort)) return ImGuiDataType.U16;
        if (type == typeof(int)) return ImGuiDataType.S32;
        if (type == typeof(uint)) return ImGuiDataType.U32;
        if (type == typeof(long)) return ImGuiDataType.S64;
        if (type == typeof(ulong)) return ImGuiDataType.U64;
        throw new NotImplementedException($"Unsupported flag enum backing type {type}");
    }

    public FlagsEnumFieldHandler(EnumDescriptor enumDescriptor)
    {
        this.enumDescriptor = enumDescriptor;
        scalarType = TypeToImguiDataType(enumDescriptor.BackingType);
    }

    public unsafe void OnIMGUI(UIContext context)
    {
        var labels = enumDescriptor.GetDisplayLabels();
        var values = enumDescriptor.GetValues();
        var value = context.GetRaw()!;
        ImguiHelpers.BeginRect();
        long longValue = enumDescriptor.BackingType == typeof(ulong) ? (long)(ulong)value : Convert.ToInt64(value);
        if (ImGui.InputScalar(context.label, scalarType, (IntPtr)(&longValue))) {
            UndoRedo.RecordSet(context, value = Convert.ChangeType(longValue, value.GetType()));
        }

        var startX = ImGui.GetCursorPosX();
        var endX = ImGui.GetWindowSize().X;
        var totalPadding = startX * 2;
        var w_total = endX - totalPadding;
        ImGui.Text("Flags: ");
        ImGui.SameLine();
        var tabMargin = ImGui.GetStyle().FramePadding.X * 2 + 32; // how do we determine checkbox size properly?

        ImGui.PushID(context.label);
        var x = ImGui.CalcTextSize("Flags: ").X + ImGui.GetStyle().FramePadding.X;
        for (int i = 0; i < labels.Length; ++i) {
            var label = labels[i];
            var flagValue = values[i];
            var tabWidth = ImGui.CalcTextSize(label).X + tabMargin;
            if (i > 0) {
                if (x + tabWidth >= w_total) {
                    x = 0;
                } else {
                    ImGui.SameLine();
                }
            }
            x += tabWidth;

            var hasFlag = enumDescriptor.HasFlag(value, flagValue);
            if (ImGui.Checkbox(label, ref hasFlag)) {
                UndoRedo.RecordSet(context, hasFlag ? enumDescriptor.AddFlag(value, flagValue) : enumDescriptor.RemoveFlag(value, flagValue), mergeMode: UndoRedoMergeMode.NeverMerge);
            }
        }
        ImGui.PopID();
        ImguiHelpers.EndRect(2);
        AppImguiHelpers.ShowDefaultCopyPopup(value, value.GetType(), context);
    }
}

public class RectFieldHandler : Singleton<RectFieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var vec = context.Get<Rect>().AsVector;
        if (ImGui.DragFloat4(context.label, ref vec, 0.01f)) {
            UndoRedo.RecordSet(context, new Rect(vec.X, vec.Y, vec.Z, vec.W));
        }
        AppImguiHelpers.ShowDefaultCopyPopup(ref vec, context);
    }
}

public class PositionFieldHandler : Singleton<PositionFieldHandler>, IObjectUIHandler
{
    public unsafe void OnIMGUI(UIContext context)
    {
        var val = context.Get<Position>();
        if (ImGui.DragScalarN(context.label, ImGuiDataType.Double, (IntPtr)(&val), 4, 0.01f)) {
            UndoRedo.RecordSet(context, val);
        }
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

[ObjectImguiHandler(typeof(Int2), Stateless = true)]
public class Int2FieldHandler : Singleton<Int2FieldHandler>, IObjectUIHandler
{
    public unsafe void OnIMGUI(UIContext context)
    {
        var val = context.Get<Int2>();
        if (ImGui.DragScalarN(context.label, ImGuiDataType.S32, (IntPtr)(&val), 2, 0.05f)) {
            UndoRedo.RecordSet(context, val);
        }
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

[ObjectImguiHandler(typeof(Int3), Stateless = true)]
public class Int3FieldHandler : Singleton<Int3FieldHandler>, IObjectUIHandler
{
    public unsafe void OnIMGUI(UIContext context)
    {
        var val = context.Get<Int3>();
        if (ImGui.DragScalarN(context.label, ImGuiDataType.S32, (IntPtr)(&val), 3, 0.05f)) {
            UndoRedo.RecordSet(context, val);
        }
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

[ObjectImguiHandler(typeof(Int4), Stateless = true)]
public class Int4FieldHandler : Singleton<Int4FieldHandler>, IObjectUIHandler
{
    public unsafe void OnIMGUI(UIContext context)
    {
        var val = context.Get<Int4>();
        if (ImGui.DragScalarN(context.label, ImGuiDataType.S32, (IntPtr)(&val), 4, 0.05f)) {
            UndoRedo.RecordSet(context, val);
        }
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

[ObjectImguiHandler(typeof(Uint2), Stateless = true)]
public class Uint2FieldHandler : Singleton<Uint2FieldHandler>, IObjectUIHandler
{
    public unsafe void OnIMGUI(UIContext context)
    {
        var val = context.Get<Uint2>();
        if (ImGui.DragScalarN(context.label, ImGuiDataType.U32, (IntPtr)(&val), 2, 0.05f)) {
            UndoRedo.RecordSet(context, val);
        }
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

[ObjectImguiHandler(typeof(Uint3), Stateless = true)]
public class Uint3FieldHandler : Singleton<Uint3FieldHandler>, IObjectUIHandler
{
    public unsafe void OnIMGUI(UIContext context)
    {
        var val = context.Get<Uint3>();
        if (ImGui.DragScalarN(context.label, ImGuiDataType.U32, (IntPtr)(&val), 3, 0.05f)) {
            UndoRedo.RecordSet(context, val);
        }
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

[ObjectImguiHandler(typeof(Uint4), Stateless = true)]
public class Uint4FieldHandler : Singleton<Uint4FieldHandler>, IObjectUIHandler
{
    public unsafe void OnIMGUI(UIContext context)
    {
        var val = context.Get<Uint4>();
        if (ImGui.DragScalarN(context.label, ImGuiDataType.U32, (IntPtr)(&val), 4, 0.05f)) {
            UndoRedo.RecordSet(context, val);
        }
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

public class RangeFieldHandler : Singleton<RangeFieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<ReeLib.via.Range>();
        if (ImGui.DragFloatRange2(context.label, ref val.r, ref val.s)) {
            UndoRedo.RecordSet(context, val);
        }
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

public class SizeFieldHandler : Singleton<SizeFieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<ReeLib.via.Size>();
        if (ImGui.DragFloatRange2(context.label, ref val.w, ref val.h)) {
            UndoRedo.RecordSet(context, val);
        }
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

public class UserDataReferenceHandler : Singleton<UserDataReferenceHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<RszInstance>();
        var ws = context.GetWorkspace();
        if (instance.RSZUserData == null || ws == null) {
            if (instance.RszClass.crc == 0) {
                ImGui.Text(context.label + ": NULL");
                return;
            }
            ImGui.TextColored(Colors.Warning, "Invalid UserData instance");
            return;
        }

        if (context.state == null) {
            if (instance.RSZUserData is RSZUserDataInfo info) {
                info.ReadClassName(ws.Env.RszParser);
                context.state = $"{info.ClassName} [{info.Path}]";
            } else if (instance.RSZUserData is RSZUserDataInfo_TDB_LE_67 infoEmbedded) {
                infoEmbedded.ReadClassName(ws.Env.RszParser);
                context.state = $"{infoEmbedded.ClassName} [Hash: {infoEmbedded.jsonPathHash}]";
            } else {
                ImGui.Text(context.label + ": Unhandled UserData");
                return;
            }
        }

        ImguiHelpers.BeginRect();
        if (ImguiHelpers.TreeNodeSuffix(context.label, context.state ?? instance.RSZUserData.ClassName!)) {
            if (ImGui.Button($"{AppIcons.SI_WindowOpenNew}")) {
                if (context.children.Count > 0) {
                    var editor = context.GetChildHandler<UserDataFileEditor>()!;
                    EditorWindow.CurrentWindow!.AddFileEditor(editor.Handle);
                }
            }
            ImguiHelpers.Tooltip("Open in New Window");
            ImGui.SameLine();
            HandleLinkedUserdata(context, instance, ws);
            ImGui.TreePop();
        }
        ImguiHelpers.EndRect(4);
        ImGui.Spacing();
    }

    private void HandleLinkedUserdata(UIContext context, RszInstance instance, ContentPatcher.ContentWorkspace ws)
    {
        if (context.children.Count == 0) {
            RSZFile? file = null;
            context.state = null;

            if (instance.RSZUserData is RSZUserDataInfo info) {
                // var pathCtx = context.AddChild<RSZUserDataInfo, string>(
                var pathCtx = context.AddChild(
                    "Userdata file path",
                    info,
                    new ResourcePathPicker(ws, KnownFileFormats.UserData),
                    getter: (c) => ((RSZUserDataInfo)c!.target!).Path,
                    setter: (ctx, newPathObj) => {
                        var info = (RSZUserDataInfo)ctx.target!;
                        var newPath = newPathObj as string;
                        if (info.Path == newPath) return;
                        if (string.IsNullOrEmpty(newPath)) {
                            Logger.Error("Empty user data file path not allowed");
                            return;
                        }
                        if (!ws.ResourceManager.TryGetOrLoadFile(newPath, out var fileHandle)) {
                            Logger.Error("User data file not found: " + newPath);
                            return;
                        }
                        var file = fileHandle.GetFile<UserFile>();

                        var rsz = ctx.FindHandlerInParents<IRSZFileEditor>()?.GetRSZFile();
                        if (rsz == null || !rsz.InstanceList.Any(ii => ii.RSZUserData?.InstanceId == info.InstanceId && ii != instance)) {
                            // we can do a full replace here - eithe rif we can't find the rsz container, or if there's no other references to this same userdata intance
                            info.Path = newPath;
                            info.typeId = file.RSZ.ObjectList[0].RszClass.typeId;
                        } else {
                            // create a new userdata info
                            ctx.parent!.Set(instance = new RszInstance(file.RSZ.ObjectList[0].RszClass, -1, new RSZUserDataInfo() {
                                Path = newPath,
                                typeId = file.RSZ.ObjectList[0].RszClass.typeId,
                                instanceId = instance.Index,
                            }));

                            rsz.RSZUserDataInfoList.Add(instance.RSZUserData!);
                        }
                        ctx.parent?.ClearChildren();
                    }
                );
                if (string.IsNullOrEmpty(info.Path)) {
                    ImGui.TextColored(Colors.Error, "No path for user data");
                    return;
                }
                var resolvedPath = ws.Env.ResolveFilepath(info.Path);
                if (resolvedPath == null) {
                    ImGui.TextColored(Colors.Error, "User data file not found: " + info.Path);
                    return;
                }

                var handle = ws.ResourceManager.GetFileHandle(resolvedPath!);
                WindowData.CreateEmbeddedWindow(context, context.GetWindow()!, new UserDataFileEditor(ws, handle), "UserFile");
            } else if (instance.RSZUserData is RSZUserDataInfo_TDB_LE_67 infoEmbedded) {
                file = infoEmbedded.EmbeddedRSZ;
                // TODO re7?
                if (file == null) {
                    ImGui.TextColored(Colors.Error, "Missing embedded user data");
                    return;
                }

                var rsz = context.FindHandlerInParents<IRSZFileEditor>()?.GetRSZFile();
                if (rsz == null) {
                    ImGui.TextColored(Colors.Warning, "Invalid UserData instance");
                    return;
                }
                var user = new UserFile(ws.Env.RszFileOption, rsz.FileHandler, file);
                var parentFileHandle = context.FindValueInParentValues<FileHandle>();
                FileHandle childHandle;
                if (parentFileHandle != null) {
                    childHandle = FileHandle.CreateEmbedded(parentFileHandle.Loader, new BaseFileResource<UserFile>(user), "embedded.user.0");
                } else {
                    childHandle = FileHandle.CreateEmbedded(UserFileLoader.Instance, new BaseFileResource<UserFile>(user), "embedded.user.0");
                }

                WindowData.CreateEmbeddedWindow(context, context.GetWindow()!, new UserDataFileEditor(ws, childHandle), "UserFile");
            }

            // context.AddChild("UserFile", file);
        }

        if (context.children.Count == 0) {
            ImGui.TextColored(Colors.Warning, "Failed to set up UserData reference");
        } else {
            context.ShowChildrenUI();
        }
    }
}

public class GuidFieldHandler : Singleton<GuidFieldHandler>, IObjectUIHandler
{
    private const uint GuidLength = 36;
    private bool noContextMenu;

    public static readonly GuidFieldHandler NoContextMenuInstance = new GuidFieldHandler() { noContextMenu = true };

    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Guid>();
        var str = val.ToString();
        if (ImGui.InputText(context.label, ref str, GuidLength, ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.CharsNoBlank)) {
            if (Guid.TryParse(str, out var newguid)) {
                UndoRedo.RecordSet(context, newguid);
            } else {
                ImGui.TextColored(Colors.Error, "Invalid GUID");
            }
        }

        if (!noContextMenu && ImGui.BeginPopupContextItem(context.label)) {
            if (ImGui.Selectable("Randomize")) {
                UndoRedo.RecordSet(context, Guid.NewGuid());
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Selectable("Find translation")) {
                var ws = context.GetWorkspace();
                var window = EditorWindow.CurrentWindow;
                ws?.Messages.GetTextAsync(val).ContinueWith((res) => {
                    if (res.Result == null) {
                        Logger.Info("Message not found for guid " + val);
                    } else {
                        Logger.Info("Guid " + val + " message:\n" + res.Result);
                        window?.Overlays.ShowTooltip(res.Result, 1.5f);
                    }
                });
                ImGui.CloseCurrentPopup();
            }
            ImGui.Separator();
            AppImguiHelpers.ShowDefaultCopyPopupButtons(ref val, context);
            ImGui.EndPopup();
        }
    }
}

public class ReadOnlyLabelHandler : Singleton<ReadOnlyLabelHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        ImGui.Text(context.label + " (readonly)");
    }
}

[ObjectImguiHandler(typeof(ResourceInfo), Stateless = true)]
public class ResourceInfoHandler : Singleton<ResourceInfoHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            context.AddChild<ResourceInfo, string>(context.label, context.Get<ResourceInfo>(), new ResourcePathPicker(), static (c) => c!.Path, static (c, v) => c.Path = v);
        }
        context.children[0].ShowUI();
    }
}

[ObjectImguiHandler(typeof(GameObjectRef), Stateless = true)]
public class GameObjectRefHandler : Singleton<GameObjectRefHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var gref = context.Get<GameObjectRef>();
        if (context.children.Count == 0) {
            context.AddChild<GameObjectRef, Guid>("##Guid", gref, GuidFieldHandler.NoContextMenuInstance, (r) => r!.guid, (r, v) => r.guid = v);
            context.AddChild("_", null, SameLineHandler.Instance);
            context.AddChild(context.label, gref, new SwappableRszInstanceHandler("via.GameObject", true, context.label + " (GameObjectRef)"), (ctx) => ((GameObjectRef)ctx.target!).target?.Instance, (ctx, v) => {
                var newInstance = (RszInstance?)v;
                var gr = (GameObjectRef)ctx.target!;
                if (newInstance == null) {
                    UndoRedo.RecordSet(ctx.parent!, new GameObjectRef(), mergeMode: UndoRedoMergeMode.NeverMerge);
                    ctx.parent!.ClearChildren();
                    return;
                }

                var owner = ctx.FindHandlerInParents<ISceneEditor>()?.GetScene();
                if (owner == null) {
                    Logger.Error("Could not find RSZ data owner");
                    return;
                }
                var newGo = owner.FindGameObjectByInstance(newInstance);
                if (newGo == null) {
                    Logger.Error("Could not find target GameObject");
                    return;
                }

                UndoRedo.RecordSet(ctx.parent!, new GameObjectRef(newGo.guid, newGo), mergeMode: UndoRedoMergeMode.NeverMerge);
                ctx.parent!.ClearChildren();
            });
        }
        ImGui.PushID(context.label);
        ImGui.PushItemWidth(ImGui.CalcItemWidth() / 2 - ImGui.GetStyle().FramePadding.X);
        context.ShowChildrenUI();
        ImGui.PopItemWidth();
        ImGui.PopID();
    }
}

[RszClassHandler("via.Prefab")]
public class PrefabRefHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<RszInstance>();
        if (context.children.Count == 0) {
            context.AddChild<RszInstance, string>(
                context.label,
                instance,
                new ResourcePathPicker(context.GetWorkspace(), KnownFileFormats.Prefab),
                getter: (inst) => inst?.Values[1] as string,
                setter: (inst, str) => inst.Values[1] = str ?? string.Empty);
        }

        var preloadType = instance.Values[0].GetType();
        var preload = instance.Values[0] is bool b ? b : (byte)instance.Values[0] != 0;
        if (ImGui.Checkbox("Preload", ref preload)) {
            if (preloadType == typeof(bool)) {
                UndoRedo.RecordCallbackSetter(context, instance, !preload, preload, (i, v) => i.Values[0] = v, $"{instance.GetHashCode()} Preload");
            } else {
                UndoRedo.RecordCallbackSetter(context, instance, (byte)0, (byte)1, (i, v) => i.Values[0] = v, $"{instance.GetHashCode()} Preload");
            }
        }
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("If true, the prefab will get loaded immediately with the scene and included in the resources list.\nYou rarely want to change this for existing files.");
        ImGui.SameLine();
        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(ReeLib.via.Transform), Stateless = true)]
public class TransformStructHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var data = context.Get<ReeLib.via.Transform>();
        var show = ImguiHelpers.TreeNodeSuffix("Transform", data.ToString()!);
        AppImguiHelpers.ShowDefaultCopyPopup(ref data, context);
        if (show) {
            var pos = data.pos;
            var rot = data.rot.ToVector4();
            var scale = data.scale;
            var w = ImGui.CalcItemWidth();
            ImGui.SetNextItemWidth(w * 0.75f);
            if (ImGui.DragFloat3("##Position", ref pos, 0.005f)) {
                // UndoRedo.RecordCallbackSetter(context, data, data.pos, pos, (i, v) => i.pos = v, $"{data.GetHashCode()} Position");
                UndoRedo.RecordSet(context, new ReeLib.via.Transform(pos, rot.ToQuaternion(), scale));
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(w * 0.25f - ImGui.GetStyle().FramePadding.X * 2);
            ImGui.LabelText("Position", "##labelP");
            if (ImGui.DragFloat4("Rotation", ref rot, 0.002f)) {
                var newrot = Quaternion.Normalize(rot.ToQuaternion());
                UndoRedo.RecordSet(context, new ReeLib.via.Transform(pos, newrot, scale));
            }

            ImGui.SetNextItemWidth(w * 0.75f);
            if (ImGui.DragFloat3("##Scale", ref scale, 0.005f)) {
                // UndoRedo.RecordCallbackSetter(context, data, data.scale, scale, (i, v) => i.scale = v, $"{data.GetHashCode()} Scale");
                UndoRedo.RecordSet(context, new ReeLib.via.Transform(pos, rot.ToQuaternion(), scale));
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(w * 0.25f - ImGui.GetStyle().FramePadding.X * 2);
            ImGui.LabelText("Scale", "##labelS");
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(ReeLib.via.Sphere), Stateless = true)]
public class SphereStructHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var sphere = context.Get<ReeLib.via.Sphere>();
        var show = ImguiHelpers.TreeNodeSuffix(context.label, sphere.ToString());
        AppImguiHelpers.ShowDefaultCopyPopup(ref sphere, context);
        if (show) {
            if (ImGui.DragFloat3("Position", ref sphere.pos, 0.005f)) {
                UndoRedo.RecordSet(context, sphere, undoId: $"{context.GetHashCode()} pos");
            }
            if (ImGui.DragFloat("Radius", ref sphere.r, 0.002f, 0.001f, 1000f)) {
                UndoRedo.RecordSet(context, sphere, undoId: $"{context.GetHashCode()} radius");
            }
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(ReeLib.via.mat4), Stateless = true)]
public class Mat4StructHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var mat = context.Get<ReeLib.via.mat4>();
        var open = ImguiHelpers.TreeNodeSuffix(context.label, mat.ToString());
        AppImguiHelpers.ShowDefaultCopyPopup(ref mat, context);
        if (open) {
            Matrix4X4.Decompose(mat.ToSystem().ToGeneric(), out var scale, out var rot, out var trans);

            var w = ImGui.CalcItemWidth();
            ImGui.SetNextItemWidth(w * 0.75f);
            if (ImGui.DragFloat3("##Offset", ref Unsafe.As<Vector3D<float>, Vector3>(ref trans), 0.005f)) {
                mat = Transform.GetMatrixFromTransforms(trans, rot, scale).ToSystem();
                UndoRedo.RecordSet(context, mat, undoId: $"{context.GetHashCode()} offset");
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(w * 0.25f - ImGui.GetStyle().FramePadding.X * 2);
            ImGui.LabelText("Offset", "##labelP");

            if (ImGui.DragFloat4("Rotation", ref Unsafe.As<Quaternion<float>, Vector4>(ref rot), 0.005f)) {
                rot = Quaternion<float>.Normalize(rot);
                mat = Transform.GetMatrixFromTransforms(trans, rot, scale).ToSystem();
                UndoRedo.RecordSet(context, mat, undoId: $"{context.GetHashCode()} rotation");
            }
            ImGui.SetNextItemWidth(w * 0.75f);
            if (ImGui.DragFloat3("##Scale", ref Unsafe.As<Vector3D<float>, Vector3>(ref scale), 0.005f)) {
                mat = Transform.GetMatrixFromTransforms(trans, rot, scale).ToSystem();
                UndoRedo.RecordSet(context, mat, undoId: $"{context.GetHashCode()} scale");
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(w * 0.25f - ImGui.GetStyle().FramePadding.X * 2);
            ImGui.LabelText("Scale", "##labelS");
            if (MathF.Abs(scale.X - scale.Y) > 0.001f || MathF.Abs(scale.Y - scale.Z) > 0.001f) {
                ImGui.TextColored(Colors.Warning, "A non-uniform scale can sometimes cause issues with collisions.");
            }

            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(ReeLib.via.OBB), Stateless = true)]
public class OBBStructHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var box = context.Get<ReeLib.via.OBB>();
        var show = ImguiHelpers.TreeNodeSuffix(context.label, box.ToString());
        AppImguiHelpers.ShowDefaultCopyPopup(ref box, context);
        if (show) {
            Matrix4X4.Decompose(box.Coord.ToSystem().ToGeneric(), out var scale, out var rot, out var trans);

            if (ImGui.DragFloat3("Offset", ref Unsafe.As<Vector3D<float>, Vector3>(ref trans), 0.005f)) {
                box.Coord = Transform.GetMatrixFromTransforms(trans, rot, scale).ToSystem();
                UndoRedo.RecordSet(context, box, undoId: $"{context.GetHashCode()} offset");
            }
            if (ImGui.DragFloat4("Rotation", ref Unsafe.As<Quaternion<float>, Vector4>(ref rot), 0.005f)) {
                rot = Quaternion<float>.Normalize(rot);
                box.Coord = Transform.GetMatrixFromTransforms(trans, rot, scale).ToSystem();
                UndoRedo.RecordSet(context, box, undoId: $"{context.GetHashCode()} rotation");
            }
            if (ImGui.DragFloat3("Scale", ref Unsafe.As<Vector3D<float>, Vector3>(ref scale), 0.005f)) {
                box.Coord = Transform.GetMatrixFromTransforms(trans, rot, scale).ToSystem();
                UndoRedo.RecordSet(context, box, undoId: $"{context.GetHashCode()} scale");
            }
            if (MathF.Abs(scale.X - 1) > 0.001f || MathF.Abs(scale.Y - 1) > 0.001f || MathF.Abs(scale.Z - 1) > 0.001f) {
                ImGui.TextColored(Colors.Warning, "A scale different from (1, 1, 1) may cause issues with collisions. It's usually more reliable to modify Extent instead.");
            }

            var ext = box.Extent;
            if (ImGui.DragFloat3("Extent", ref ext, 0.002f, 0.001f, 1000f)) {
                box.Extent = ext;
                UndoRedo.RecordSet(context, box, undoId: $"{context.GetHashCode()} extent");
            }
            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(ReeLib.via.Capsule), Stateless = true)]
public class CapsuleStructHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var capsule = context.Get<ReeLib.via.Capsule>();
        var show = ImguiHelpers.TreeNodeSuffix(context.label, capsule.ToString());
        AppImguiHelpers.ShowDefaultCopyPopup(ref capsule, context);
        if (show) {
            if (ImGui.DragFloat3("Point 1", ref capsule.p0, 0.005f)) {
                UndoRedo.RecordSet(context, capsule, undoId: $"{context.GetHashCode()} p0");
            }

            if (ImGui.DragFloat3("Point 2", ref capsule.p1, 0.005f)) {
                UndoRedo.RecordSet(context, capsule, undoId: $"{context.GetHashCode()} p1");
            }

            if (ImGui.DragFloat("Radius", ref capsule.r, 0.005f, 0.001f, 1000f)) {
                UndoRedo.RecordSet(context, capsule, undoId: $"{context.GetHashCode()} r");
            }

            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(ReeLib.via.Cylinder), Stateless = true)]
public class CylinderStructHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var cylinder = context.Get<ReeLib.via.Cylinder>();
        var show = ImguiHelpers.TreeNodeSuffix(context.label, cylinder.ToString());
        AppImguiHelpers.ShowDefaultCopyPopup(ref cylinder, context);
        if (show) {
            if (ImGui.DragFloat3("Point 1", ref cylinder.p0, 0.005f)) {
                UndoRedo.RecordSet(context, cylinder, undoId: $"{context.GetHashCode()} p0");
            }

            if (ImGui.DragFloat3("Point 2", ref cylinder.p1, 0.005f)) {
                UndoRedo.RecordSet(context, cylinder, undoId: $"{context.GetHashCode()} p1");
            }

            if (ImGui.DragFloat("Radius", ref cylinder.r, 0.005f, 0.001f, 1000f)) {
                UndoRedo.RecordSet(context, cylinder, undoId: $"{context.GetHashCode()} r");
            }

            ImGui.TreePop();
        }
    }
}

[ObjectImguiHandler(typeof(ReeLib.via.AABB), Stateless = true)]
public class AABBStructHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var aabb = context.Get<ReeLib.via.AABB>();
        var show = ImguiHelpers.TreeNodeSuffix(context.label, aabb.ToString());
        AppImguiHelpers.ShowDefaultCopyPopup(ref aabb, context);
        if (show) {
            var center = aabb.Center;
            var size = aabb.Size / 2;
            if (ImGui.DragFloat3("Center", ref center, 0.005f)) {
                UndoRedo.RecordSet(context, new AABB(center - size, center + size), undoId: $"{context.GetHashCode()} c");
            }

            if (ImGui.DragFloat3("Size", ref size, 0.005f, 0.001f, 1000f)) {
                UndoRedo.RecordSet(context, new AABB(center - size, center + size), undoId: $"{context.GetHashCode()} s");
            }

            ImGui.TreePop();
        }
    }
}
