using System.Numerics;
using ContentPatcher;
using ReeLib;
using ReeLib.Clsp;
using ReeLib.Mesh;

namespace ContentEditor.App.ImguiHandling.Chain;

public class ClspEditor(ContentWorkspace env, FileHandle file, Component? component = null)
    : FileEditor(file), IBoneReferenceHolderComponent
{
    public ContentWorkspace Workspace { get; } = env;

    public MeshComponent? MeshComponent => component?.GameObject.GetComponent<MeshComponent>();

    protected override void DrawFileContents()
    {
        var isEmpty = context.children.Count == 0;
        if (context.children.Count == 0) {
            context.AddChild("Data", Handle.GetFile<ClspFile>()).AddDefaultHandler();
        }
        context.children[0].ShowUI();
    }
}

[ObjectImguiHandler(typeof(CollisionPreset))]
internal class ClspCollisionPresetHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var instance = context.Get<CollisionPreset>();
        if (AppImguiHelpers.CopyableTreeNode<CollisionPreset>(context)) {
            if (context.children.Count == 0) {
                context.AddChild("Bone 1", instance, new BoneHashHandler(), c => c!.hash1, (c, v) => c.hash1 = v);
                context.AddChild("Bone 2", instance, new BoneHashHandler(), c => c!.hash2, (c, v) => c.hash2 = v);
                context.AddChild("Shape", instance, getter: c => c!.shape, setter: (c, v) => c.shape = v).AddDefaultHandler();
                context.AddChildContextSetter("Shape Type", instance, getter: c => c!.ShapeType, setter: (ctx, c, v) => {
                    c.ShapeType = v;

                    var shapeCtx = ctx.parent?.children[2];
                    shapeCtx?.uiHandler = WindowHandlerFactory.CreateUIHandler(shapeCtx.GetRaw(), shapeCtx.GetRaw()?.GetType());
                }).AddDefaultHandler();
                context.AddChild("Flags 1", instance, getter: c => c!.flags1, setter: (c, v) => c.flags1 = v).AddDefaultHandler();
                context.AddChild("Flags 2", instance, getter: c => c!.flags2, setter: (c, v) => c.flags2 = v).AddDefaultHandler();
            }
            context.ShowChildrenUI();
            ImGui.TreePop();
        }
    }
}
