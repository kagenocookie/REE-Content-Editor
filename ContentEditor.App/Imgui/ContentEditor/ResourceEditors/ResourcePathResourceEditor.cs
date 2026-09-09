// ResourcePathResource

using ContentEditor;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(ResourcePathResource))]
public class ResourcePathResourceEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var res = context.Get<ResourcePathResource>();
        if (context.children.Count == 0) {
            if (context.EntityParams?.ResourceType == null) {
                WindowHandlerFactory.SetupObjectUIContext(context, res.GetType());
                return;
            }

            var workspace = context.GetWorkspace();
            if (workspace == null) return;

            var config = workspace.ResourceManager.GetResourceConfig(context.EntityParams.ResourceType);
            if (config?.Patcher is ResourceProxyPrefabHandler ppp) {
                context.AddChild("Resource Path", res, new ResourcePathPicker(workspace, ppp.ResourceType), c => c!.ResourcePath, (c, v) => c.ResourcePath = v ?? "");
                if (res.CatalogEntry?.Fields.Length > 2) {
                    var cc = context.AddChild("Catalog Data", res, getter: (r) => r!.CatalogEntry);
                    cc.uiHandler = new NestedRszInstanceHandler();
                }
            }
        }

        context.ShowChildrenUI();
    }
}
