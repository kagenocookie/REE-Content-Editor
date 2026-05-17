using System.Collections;
using ContentEditor.BackgroundTasks;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Jmap;
using ReeLib.Sfur;

namespace ContentEditor.App.ImguiHandling;

[ObjectImguiHandler(typeof(ShellFurMaterial))]
public class ShellFurMaterialHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(ShellFurMaterial), orderFunc: (f) => {
                if (f.Name == nameof(ShellFurMaterial.materialName)) return 0;
                if (f.Name == nameof(ShellFurMaterial.furmaskTexture)) return 1;
                return 2;
            });
            context.children[1].uiHandler = new ResourcePathPicker(context.GetWorkspace(), KnownFileFormats.Texture);
        }
        context.ShowChildrenNestedUI();
    }
}
