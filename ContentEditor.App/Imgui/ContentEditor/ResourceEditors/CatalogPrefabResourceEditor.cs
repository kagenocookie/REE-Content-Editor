using ContentEditor;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

[ObjectImguiHandler(typeof(CatalogPrefabResource))]
public class CatalogPrefabResourceEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var res = context.Get<CatalogPrefabResource>();
        if (context.children.Count == 0) {
            if (context.EntityParams?.ResourceType == null) {
                WindowHandlerFactory.SetupObjectUIContext(context, res.GetType());
                return;
            }

            var workspace = context.GetWorkspace();
            if (workspace == null) return;

            if (res.ResourceType.Resource is ResourceProxyPrefabHandler proxy) {
                var instanceChild = context.AddChild("Instance", res, ChildrenOnlyHandler.Instance, r => r!.Instance, (r, v) => r.Instance = v!);
                WindowHandlerFactory.AddRszInstanceFieldChildren(instanceChild, proxy.SkipFieldCount);
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
