using System.Reflection;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;

namespace ContentEditor.App.ImguiHandling;

[ObjectImguiHandler(typeof(Folder), Stateless = true)]
public class FolderDataEditor : IObjectUIHandler
{
    private static readonly MemberInfo[] BaseMembers = [
        typeof(Folder).GetProperty(nameof(Folder.Name))!,
        typeof(Folder).GetField(nameof(Folder.Tags))!,
        typeof(Folder).GetField(nameof(Folder.ScenePath))!,
        typeof(Folder).GetField(nameof(Folder.Update))!,
        typeof(Folder).GetField(nameof(Folder.Draw))!,
        typeof(Folder).GetField(nameof(Folder.Standby))!,
    ];
    private static readonly MemberInfo[] Offset = [
        typeof(Folder).GetProperty(nameof(Folder.Offset))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var folder = context.Get<Folder>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            if (folder.Parent != null) {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(Folder), members: BaseMembers);
                if (ws != null && RszFieldCache.Folder.UniversalOffset.Exists(ws.Env.Classes.Folder)) {
                    WindowHandlerFactory.SetupObjectUIContext(context, typeof(Folder), members: Offset);
                }
            }
        }

        context.ShowChildrenUI();
    }
}
