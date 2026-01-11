using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentPatcher;
using ContentPatcher.DD2;
using ReeLib;
using ReeLib.Il2cpp;
using ZstdSharp.Unsafe;

namespace ContentEditor.App.DD2;

[CustomUIHandlerType("DD2.ArmorStyle", "dd2")]
public sealed class DD2ArmorStyleNoHandler : IObjectUIHandler
{
    private RszEnumFieldHandler? _enumHandler;

    public void OnIMGUI(UIContext context)
    {
        var entity = context.GetOwnerEntity();
        var data = entity?.Get("data") as RSZObjectResource;
        var workspace = context.GetWorkspace();
        if (entity == null || data == null || workspace == null) {
            ImGui.TextColored(Colors.Error, $"StyleNo field requires a valid item entity and workspace");
            return;
        }

        var equipType = data.Instance.GetFieldValue("_EquipCategory");

        var id = context.Get<short>();
        var expectedEnumName = equipType switch {
            2 => "ce.HelmStyleNo",
            3 => "ce.TopsStyleNo",
            4 => "ce.PantsStyleNo",
            5 => "ce.MantleStyleNo",
            7 => "ce.FacewearStyleNo",
            _ => ""
        };
        var expectedEnum = workspace.Env.TypeCache.GetEnumDescriptor(expectedEnumName, RszFieldType.S16);
        if (expectedEnum.IsEmpty) {
            ImGui.TextColored(Colors.Error, "Could not find suitable enum for equip type " + equipType);
            return;
        }

        if (_enumHandler == null || _enumHandler.EnumDescriptor != expectedEnum) {
            _enumHandler = new RszEnumFieldHandler(expectedEnum);
        }

        _enumHandler.OnIMGUI(context);
    }
}
