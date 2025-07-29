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

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<RszInstance>();
        if (instance == null) {
            ImGui.Text(context.label + ": NULL");
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
                WindowHandlerFactory.SetupRSZInstanceHandler(context);
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
        return RszInstance.CreateArrayItem(env.Env.RszParser, field);
    }
}

public class NumericFieldHandler<T>(ImGuiDataType type) : IObjectUIHandler where T : unmanaged
{
    public unsafe void OnIMGUI(UIContext context)
    {
        var num = (T)context.Get<object>();
        if (ImGui.DragScalar(context.label, type, (IntPtr)(&num))) {
            UndoRedo.RecordSet(context, num);
        }
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
        public string[] GetLabels() => descriptor.GetLabels();
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
        var labels = enumDescriptor.GetLabels();
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
                UndoRedo.RecordSet(context, hasFlag ? enumDescriptor.AddFlag(value, flagValue) : enumDescriptor.RemoveFlag(value, flagValue));
            }
        }
        ImGui.PopID();
        ImguiHelpers.EndRect(2);
    }
}

public class BoolFieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<bool>();
        if (ImGui.Checkbox(context.label, ref val)) {
            UndoRedo.RecordSet(context, val);
        }
    }
}

public class Vector2FieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Vector2>();
        if (ImGui.DragFloat2(context.label, ref val)) UndoRedo.RecordSet(context, val);
    }
}

public class Vector3FieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Vector3>();
        if (ImGui.DragFloat3(context.label, ref val)) UndoRedo.RecordSet(context, val);
    }
}

public class Vector4FieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Vector4>();
        if (ImGui.DragFloat4(context.label, ref val)) UndoRedo.RecordSet(context, val);
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

public class IntRangeFieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<ReeLib.via.RangeI>();
        if (ImGui.DragIntRange2(context.label, ref val.r, ref val.s)) {
            UndoRedo.RecordSet(context, val);
        }
    }
}

public class QuaternionFieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Quaternion>();
        var vec = new Vector4(val.X, val.Y, val.Z, val.W);
        if (ImGui.DragFloat4(context.label, ref vec)) UndoRedo.RecordSet(context, new Quaternion(vec.X, vec.Y, vec.Z, vec.W));
    }
}

public class StringFieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<string?>() ?? string.Empty;
        if (ImGui.InputText(context.label, ref val, 255)) {
            UndoRedo.RecordSet(context, val);
        }
    }
}

public class UserDataReferenceHandler : IObjectUIHandler
{
    public RszField field;

    public UserDataReferenceHandler(RszField field)
    {
        this.field = field;
    }

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
                    var editor = (UserDataFileEditor)context.children[0].uiHandler!;
                    EditorWindow.CurrentWindow!.AddSubwindow(editor);
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
                if (string.IsNullOrEmpty(info.Path)) {
                    ImGui.TextColored(Colors.Error, "No path for user data");
                    return;
                }
                if (null == ws.Env.FindSingleFile(info.Path, out var resolvedPath)) {
                    ImGui.TextColored(Colors.Error, "User data file not found: " + info.Path);
                    return;
                }

                var handle = ws.ResourceManager.GetFileHandle(resolvedPath!);
                var data = new WindowData() {
                    ParentWindow = context.GetWindow()!,
                    Handler = new UserDataFileEditor(ws, handle),
                };
                data.Context = context.AddChild("UserFile", data, (UserDataFileEditor)data.Handler!);
                data.Handler.Init(data.Context);
            } else if (instance.RSZUserData is RSZUserDataInfo_TDB_LE_67 infoEmbedded) {
                file = infoEmbedded.EmbeddedRSZ;
                // TODO re7?
                if (file == null) {
                    ImGui.TextColored(Colors.Error, "Missing embedded user data");
                    return;
                }

                var rsz = context.FindInterfaceInParentHandlers<IRSZFileEditor>()?.GetRSZFile();
                if (rsz == null) {
                    ImGui.TextColored(Colors.Warning, "Invalid UserData instance");
                    ImGui.TreePop();
                    return;
                }
                var user = new UserFile(ws.Env.RszFileOption, rsz.FileHandler, rsz);
                var parentFileHandle = context.FindInterfaceInParentValues<FileHandle>();
                FileHandle childHandle;
                if (parentFileHandle != null) {
                    childHandle = FileHandle.CreateEmbedded(parentFileHandle.Loader, new BaseFileResource<UserFile>(user));
                } else {
                    childHandle = FileHandle.CreateEmbedded(UserFileLoader.Instance, new BaseFileResource<UserFile>(user));
                }
                var wd = new WindowData() { Handler = new UserDataFileEditor(ws, childHandle) };
                wd.Context = context.AddChild("UserFile", ws, (UserDataFileEditor)wd.Handler);
                wd.Handler.Init(wd.Context);
            }

            // context.AddChild("UserFile", file);
        }

        if (context.children.Count == 0) {
            ImGui.TextColored(Colors.Warning, "Failed to set up UserData reference");
        } else {
            var prevChanged = context.children[0].Changed;
            context.children[0].ShowUI();
            if (!prevChanged && context.children[0].Changed) {
                if (context.children[0].Get<WindowData>().Handler is UserDataFileEditor udfe) {
                    udfe.Handle.Modified = true;
                } else {
                    Console.Error.WriteLine("a");
                }
            }
        }
    }
}

public class ColorFieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Color>();
        var vec = val.ToVector4();
        if (ImGui.ColorEdit4(context.label, ref vec, ImGuiColorEditFlags.Uint8)) {
            UndoRedo.RecordSet(context, Color.FromVector4(vec));
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
    }
}

public class LabelHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        ImGui.Text(context.label + " (readonly)");
    }
}

public class UnsupportedHandler : IObjectUIHandler
{
    public UnsupportedHandler()
    {
        FieldType = "unknown";
    }

    public UnsupportedHandler(RszField field)
    {
        FieldType = field.type.ToString();
    }

    public UnsupportedHandler(Type? type)
    {
        FieldType = type?.Name ?? "unknown";
    }

    public UnsupportedHandler(FieldInfo field) : this(field.FieldType) { }

    public UnsupportedHandler(MemberInfo field)
    {
        FieldType = ((field as FieldInfo)?.FieldType)?.Name ?? (field as PropertyInfo)?.PropertyType?.Name ?? "unknown";
    }

    public string FieldType { get; }

    public void OnIMGUI(UIContext context)
    {
        ImGui.TextColored(Colors.Error, $"{context.label} (unsupported {FieldType} value {context.GetRaw() ?? "NULL"})");
    }
}


public class ReadOnlyWrapperHandler : IObjectUIHandler
{
    public IObjectUIHandler next;

    public ReadOnlyWrapperHandler(IObjectUIHandler next)
    {
        this.next = next;
    }

    public void OnIMGUI(UIContext container)
    {
        ImGui.BeginDisabled();
        next.OnIMGUI(container);
        ImGui.EndDisabled();
    }
}