using System.Reflection;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Motbank;

namespace ContentEditor.App.ImguiHandling;

[ObjectImguiHandler(typeof(MotbankFile))]
public class MotbankFileHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<MotbankFile>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            context.AddChild<MotbankFile, string>("Joint Map", instance, new ResourcePathPicker(ws, KnownFileFormats.JointMap), v => v!.JmapPath, (v, p) => v.JmapPath = p ?? string.Empty);
            context.AddChild<MotbankFile, string>("User Variables", instance, new ResourcePathPicker(ws, KnownFileFormats.UserVariables), v => v!.UvarPath, (v, p) => v.UvarPath = p ?? string.Empty);
            context.AddChild<MotbankFile, List<MotbankEntry>>("Mot Lists", instance, new ListHandler(typeof(MotbankEntry), typeof(List<MotbankEntry>)), v => v!.MotlistItems);
        }

        context.ShowChildrenUI();
    }

    static MotbankFileHandler()
    {
        WindowHandlerFactory.DefineInstantiator<MotbankEntry>((ctx) => {
            var editor = ctx.FindValueInParentValues<MotbankFile>();
            return new MotbankEntry(editor?.version ?? 3);
        });
    }
}

[ObjectImguiHandler(typeof(MotbankEntry))]
public class MotlistItemHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(MotbankEntry).GetField(nameof(MotbankEntry.BankID))!,
        typeof(MotbankEntry).GetField(nameof(MotbankEntry.BankType))!,
        typeof(MotbankEntry).GetField(nameof(MotbankEntry.BankTypeMaskBits))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<MotbankEntry>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(MotbankEntry), false, DisplayedFields);
            context.AddChild<MotbankEntry, string>("Path", instance, new ResourcePathPicker(ws, KnownFileFormats.MotionList), v => v!.Path, (v, p) => v.Path = p ?? string.Empty);
        }

        if (ImguiHelpers.TreeNodeSuffix(context.label, instance.ToString())) {
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}
