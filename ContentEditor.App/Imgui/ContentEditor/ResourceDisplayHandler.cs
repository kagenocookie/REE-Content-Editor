using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;

namespace ContentEditor.App;

public class ResourceDisplayHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<IContentResource>();

        if (context.children.Count == 0) {
            WindowHandlerFactory.SetupResourceContent(context);
        }

        for (int i = 0; i < context.children.Count; i++) {
            ImGui.PushID(i);
            var child = context.children[i];
            ImguiHelpers.BeginRect();
            ImGui.Text(child.label);
            child.ShowUI();

            if (child.Changed) {
                var workspace = context.GetWorkspace();
                if (workspace?.CurrentBundle == null) {
                    ImGui.TextColored(Colors.Warning, Lang.Bundles.NeedBundleToSave);
                } else {
                    var resourceType = child.EntityParams?.ResourceType;
                    if (!string.IsNullOrEmpty(resourceType)) {
                        var id = child.EntityParams?.ResourceId ?? context.EntityParams?.ResourceId ?? -1;
                        var cres = child.Get<IContentResource>();
                        if (workspace.CurrentBundle.RecordEntityResource(resourceType, id, cres.ToJson(workspace.Env)) == Bundle.EntityRecordUpdateType.Added) {
                            Logger.Info($"Resource {cres.Label} added to current bundle {workspace.CurrentBundle.Name}");
                        }
                    }
                }
            }
            ImguiHelpers.EndRect();
            ImGui.Spacing();
            ImGui.PopID();
        }
    }
}
