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

            if (res.ResourceType.Resource is ResourceProxyPrefabHandler proxy) {
                context.AddChild(context.label, res, new ResourcePathPicker(workspace, proxy.ResourceType),
                    c => c!.ResourcePath,
                    (c, v) => {
                        c.ResourcePath = v ?? "";
                        // modify/create the catalog entry as well
                        proxy.UpdateCatalogEntry(c, context.EntityParams.ResourceId, workspace);
                    }
                );
                // only show full catalog entry data if it has more than just id and via.Prefab fields
                if (res.CatalogEntry?.Fields.Length > 2) {
                    var cc = context.AddChild("Catalog Data", res, getter: (r) => r!.CatalogEntry);
                    cc.uiHandler = new NestedRszInstanceHandler();
                }
            }
        }

        context.ShowChildrenUI();
    }
}
