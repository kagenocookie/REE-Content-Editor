using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Il2cpp;
using ReeLib.via;

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

    public static void ShowDefaultTooltip(UIContext context)
    {
        if (ImGui.BeginPopupContextItem(context.label)) {
            if (RszInstanceHandler.ShowTooltipActions(context)) {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    public static bool ShowTooltipActions(UIContext context)
    {
        if (ImGui.Button("Copy as JSON")) {
            var ws = context.GetWorkspace();
            if (ws != null) {
                EditorWindow.CurrentWindow?.CopyToClipboard(JsonSerializer.Serialize(context.GetRaw(), ws.Env.JsonOptions)!);
                EditorWindow.CurrentWindow?.Overlays.ShowTooltip("Copied!", 1f);
            }
            return true;
        }
        if (context.parent?.uiHandler is ArrayRSZHandler array && context.TryCast<RszInstance>(out var instance)) {
            if (ImGui.Button("Duplicate")) {
                var clone = instance.Clone();
                var parentList = context.parent.Get<IList>();
                UndoRedo.RecordListAdd(context.parent, parentList, clone);
                return true;
            }
        }
        if (context.uiHandler is ArrayRSZHandler thisArray && context.TryCast<IList>(out var list)) {
            var elementType = thisArray.GetElementClassnameType(context);
            var type = thisArray.Field.type switch {
                RszFieldType.Object or RszFieldType.String => typeof(RszInstance),
                RszFieldType.String or RszFieldType.Resource or RszFieldType.RuntimeType => typeof(string),
                // RszFieldType.UserData => typeof(RszInstance),
                _ => RszInstance.RszFieldTypeToCSharpType(thisArray.Field.type)
            };
            if (type != null) {
                var clipboard = EditorWindow.CurrentWindow?.GetClipboard();
                if (!string.IsNullOrEmpty(clipboard)) {
                    if (ImGui.Button("Paste JSON")) {
                        var env = context.GetWorkspace()!.Env;
                        try {
                            var newItem = JsonSerializer.Deserialize(clipboard, type, env.JsonOptions);
                            if (newItem is RszInstance rsz && elementType != null && !env.TypeCache.IsAssignableTo(rsz.RszClass.name, elementType)) {
                                Logger.Error("Unsupported type " + rsz.RszClass.name + " for array element type " + elementType);
                                return true;
                            }
                            UndoRedo.RecordListAdd(context, list, newItem);
                        } catch (Exception) {
                            Logger.Error("Invalid JSON");
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
        if (!ForceDefaultClose && instance.Fields.Length <= 3) {
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

    public void HandleTooltip(UIContext context)
    {
        RszInstanceHandler.ShowDefaultTooltip(context);
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
            if (ImGui.Button("Open in new window")) {
                if (context.children.Count > 0) {
                    var editor = context.GetChildHandler<UserDataFileEditor>()!;
                    EditorWindow.CurrentWindow!.AddFileEditor(editor.Handle);
                }
            }
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
            if (ImGui.Button("Randomize")) {
                UndoRedo.RecordSet(context, Guid.NewGuid());
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Button("Find translation")) {
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
                    // gr.target = null;
                    // gr.guid = Guid.Empty;
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
        // ImGui.Text(context.label);
        // if (gref.target != null) {
        //     ImGui.SameLine();
        //     ImGui.TextColored(Colors.Faded, "Target: " + gref.target.ToString());
        // }
        ImGui.PushItemWidth(ImGui.CalcItemWidth() / 2 - ImGui.GetStyle().FramePadding.X);
        context.ShowChildrenUI();
        ImGui.PopItemWidth();
    }
}
