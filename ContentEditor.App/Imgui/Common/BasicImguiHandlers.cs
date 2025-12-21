using System.Numerics;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ReeLib.Aimp;
using ReeLib.Common;
using ReeLib.via;

namespace ContentEditor.App.ImguiHandling;

public class NumericFieldHandler<T>(ImGuiDataType type) : IObjectUIHandler where T : unmanaged
{
    public static readonly NumericFieldHandler<T> FloatInstance = new NumericFieldHandler<T>(ImGuiDataType.Float);

    public unsafe void OnIMGUI(UIContext context)
    {
        var num = (T)context.Get<object>();
        if (ImGui.DragScalar(context.label, type, &num, type is ImGuiDataType.Float or ImGuiDataType.Double ? 0.01f : 0.05f)) {
            UndoRedo.RecordSet(context, num);
        }
    }
}

public class HalfFloatFieldHandler : IObjectUIHandler
{
    public static readonly HalfFloatFieldHandler Instance = new();

    public unsafe void OnIMGUI(UIContext context)
    {
        var num = (float)context.Get<Half>();
        if (ImGui.DragFloat(context.label, ref num, 0.01f)) {
            UndoRedo.RecordSet(context, (Half)num);
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
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

public class Vector3FieldHandler : Singleton<Vector3FieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Vector3>();
        if (ImGui.DragFloat3(context.label, ref val, 0.01f)) UndoRedo.RecordSet(context, val);
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

[ObjectImguiHandler(typeof(PaddedVec3), Stateless = true)]
public class PaddedVec3FieldHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<PaddedVec3>();
        var valVec = val.Vector3;
        if (ImGui.DragFloat3(context.label, ref valVec, 0.01f)) UndoRedo.RecordSet(context, new PaddedVec3(valVec.X, valVec.Y, valVec.Z));
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

public class Vector4FieldHandler : Singleton<Vector4FieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Vector4>();
        if (ImGui.DragFloat4(context.label, ref val, 0.01f)) UndoRedo.RecordSet(context, val);
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
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
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}

public class QuaternionFieldHandler : Singleton<QuaternionFieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var val = context.Get<Quaternion>();
        var vec = new Vector4(val.X, val.Y, val.Z, val.W);
        if (ImGui.DragFloat4(context.label, ref vec, 0.001f)) {
            val = Quaternion.Normalize(new Quaternion(vec.X, vec.Y, vec.Z, vec.W));
            UndoRedo.RecordSet(context, val);
        }
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
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

        if (ImGui.BeginPopupContextItem(context.label)) {
            if (ImGui.Selectable("Copy UTF-16 hash")) {
                var hash = MurMur3HashUtils.GetHash(val);
                EditorWindow.CurrentWindow?.CopyToClipboard(hash.ToString(), "Copied hash: " + hash);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }
}

public class ConfirmedStringFieldHandler : Singleton<ConfirmedStringFieldHandler>, IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var curString = context.Get<string?>() ?? string.Empty;
        context.InitFilterDefault(curString);
        if (ImGui.InputText(context.label, ref context.Filter, 255, ImGuiInputTextFlags.EnterReturnsTrue)) {
            UndoRedo.RecordSet(context, context.Filter);
        } else if (context.Filter != curString) {
            if (ImGui.Button("Confirm")) {
                UndoRedo.RecordSet(context, context.Filter);
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) {
                context.Filter = curString;
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
        AppImguiHelpers.ShowDefaultCopyPopup(ref val, context);
    }
}
