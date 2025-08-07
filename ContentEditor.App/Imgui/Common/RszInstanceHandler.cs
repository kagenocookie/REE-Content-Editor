using System.Collections;
using System.Numerics;
using System.Reflection;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Il2cpp;
using ReeLib.via;

namespace ContentEditor.App.ImguiHandling;

public class RszInstanceHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<RszInstance>();
        if (instance == null) {
            ImGui.Text(context.label + ": NULL");
            return;
        }
        ImguiHelpers.TextSuffix(context.label, context.stringFormatter?.GetString(instance) ?? instance.RszClass.name);

        foreach (var child in context.children) {
            child.ShowUI();
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
        if (!ForceDefaultClose) {
            ImGui.SetNextItemOpen(instance.Fields.Length <= 3, ImGuiCond.FirstUseEver);
        }
        if (ImguiHelpers.TreeNodeSuffix(context.label, context.stringFormatter?.GetString(instance) ?? instance.RszClass.name)) {
            if (context.children.Count == 0) {
                if (instance.RSZUserData != null) {
                    context.AddChild(context.label, instance, new UserDataReferenceHandler());
                } else {
                    WindowHandlerFactory.SetupRSZInstanceHandler(context);
                }
            }
            ImGui.PushID(context.GetRaw()!.GetHashCode());
            foreach (var child in context.children) {
                child.ShowUI();
            }
            ImGui.PopID();
            ImGui.TreePop();
        }
    }
}

public class ArrayRSZHandler : BaseListHandler
{
    private RszField field;

    public ArrayRSZHandler(RszField field)
    {
        this.field = field;
        CanCreateNewElements = true;
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

    protected override object? CreateNewElement(UIContext context)
    {
        var env = context.GetWorkspace();
        if (env == null) return null;
        string? classname;
        if (string.IsNullOrEmpty(field.original_type)) {
            var first = context.Get<IList<object>>().FirstOrDefault() as RszInstance;
            classname = first?.RszClass.name;
            if (classname == null) {
                Logger.Error("Could not determine array element type");
                return null;
            }
        } else {
            classname = RszInstance.GetElementType(field.original_type);
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

public class RectFieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var vec = context.Get<Rect>().AsVector;
        if (ImGui.DragFloat4(context.label, ref vec)) {
            UndoRedo.RecordSet(context, new Rect(vec.X, vec.Y, vec.Z, vec.W));
        }
    }
}

public class PositionFieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Position>();
        // we lose some precision here but I haven't yet seen a case where it makes a noticeable difference
        var vec = new Vector3((float)val.x, (float)val.y, (float)val.z);
        if (ImGui.DragFloat3(context.label, ref vec)) {
            val.x = vec.X;
            val.y = vec.Y;
            val.z = vec.Z;
            UndoRedo.RecordSet(context, val);
        }
    }
}

public class RangeFieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<ReeLib.via.Range>();
        if (ImGui.DragFloatRange2(context.label, ref val.r, ref val.s)) {
            UndoRedo.RecordSet(context, val);
        }
    }
}

public class SizeFieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<ReeLib.via.Size>();
        if (ImGui.DragFloatRange2(context.label, ref val.w, ref val.h)) {
            UndoRedo.RecordSet(context, val);
        }
    }
}

public class UserDataReferenceHandler : IObjectUIHandler
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
                if (null == ws.Env.FindSingleFile(info.Path, out var resolvedPath)) {
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
                    ImGui.TreePop();
                    return;
                }
                var user = new UserFile(ws.Env.RszFileOption, rsz.FileHandler, rsz);
                var parentFileHandle = context.FindClassValueInParentValues<FileHandle>();
                FileHandle childHandle;
                if (parentFileHandle != null) {
                    childHandle = FileHandle.CreateEmbedded(parentFileHandle.Loader, new BaseFileResource<UserFile>(user));
                } else {
                    childHandle = FileHandle.CreateEmbedded(UserFileLoader.Instance, new BaseFileResource<UserFile>(user));
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

public class GuidFieldHandler : IObjectUIHandler
{
    private const uint GuidLength = 36;

    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Guid>();
        var str = val.ToString();
        if (ImGui.InputText(context.label, ref str, GuidLength, ImGuiInputTextFlags.CharsHexadecimal|ImGuiInputTextFlags.CharsNoBlank)) {
            if (Guid.TryParse(str, out var newguid)) {
                UndoRedo.RecordSet(context, newguid);
            } else {
                ImGui.TextColored(Colors.Error, "Invalid GUID");
            }
        }

        if (ImGui.BeginPopupContextItem(context.label)) {
            if (ImGui.Button("Randomize")) {
                UndoRedo.RecordSet(context, Guid.NewGuid());
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }
}

public class ReadOnlyLabelHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        ImGui.Text(context.label + " (readonly)");
    }
}

[ObjectImguiHandler(typeof(ResourceInfo))]
public class ResourceInfoHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            context.AddChild<ResourceInfo, string>(context.label, context.Get<ResourceInfo>(), new ResourcePathPicker(), static (c) => c!.Path, static (c, v) => c.Path = v);
        }
        context.children[0].ShowUI();
    }
}
