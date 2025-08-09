using System.Numerics;
using ImGuiNET;
using ReeLib.via;

namespace ContentEditor.App.ImguiHandling;

public class NumericFieldHandler<T>(ImGuiDataType type) : IObjectUIHandler where T : unmanaged
{
    public unsafe void OnIMGUI(UIContext context)
    {
        var num = (T)context.Get<object>();
        if (ImGui.DragScalar(context.label, type, (IntPtr)(&num), 0.05f)) {
            UndoRedo.RecordSet(context, num);
        }
    }
}

public class BoolFieldHandler : Singleton<BoolFieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<bool>();
        if (ImGui.Checkbox(context.label, ref val)) {
            UndoRedo.RecordSet(context, val);
        }
    }
}

public class Vector2FieldHandler : Singleton<Vector2FieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Vector2>();
        if (ImGui.DragFloat2(context.label, ref val, 0.01f)) UndoRedo.RecordSet(context, val);
    }
}

public class Vector3FieldHandler : Singleton<Vector3FieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Vector3>();
        if (ImGui.DragFloat3(context.label, ref val, 0.01f)) UndoRedo.RecordSet(context, val);
    }
}

public class Vector4FieldHandler : Singleton<Vector4FieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Vector4>();
        if (ImGui.DragFloat4(context.label, ref val, 0.01f)) UndoRedo.RecordSet(context, val);
    }
}

public class IntRangeFieldHandler : Singleton<IntRangeFieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<ReeLib.via.RangeI>();
        if (ImGui.DragIntRange2(context.label, ref val.r, ref val.s, 0.05f)) {
            UndoRedo.RecordSet(context, val);
        }
    }
}

public class QuaternionFieldHandler : Singleton<QuaternionFieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Quaternion>();
        var vec = new Vector4(val.X, val.Y, val.Z, val.W);
        if (ImGui.DragFloat4(context.label, ref vec, 0.001f)) UndoRedo.RecordSet(context, new Quaternion(vec.X, vec.Y, vec.Z, vec.W));
    }
}

public class StringFieldHandler : Singleton<StringFieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<string?>() ?? string.Empty;
        if (ImGui.InputText(context.label, ref val, 255)) {
            UndoRedo.RecordSet(context, val);
        }
    }
}

public class ConfirmedStringFieldHandler : Singleton<ConfirmedStringFieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var curString = context.Get<string?>() ?? string.Empty;
        context.state ??= curString;
        if (ImGui.InputText(context.label, ref context.state, 255, ImGuiInputTextFlags.EnterReturnsTrue)) {
            UndoRedo.RecordSet(context, context.state);
        } else if (context.state != curString) {
            if (ImGui.Button("Confirm")) {
                UndoRedo.RecordSet(context, context.state);
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) {
                context.state = curString;
            }
        }
    }
}

public class ColorFieldHandler : Singleton<ColorFieldHandler>, IObjectUIHandler
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
