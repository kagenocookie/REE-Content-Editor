using ContentPatcher;
using ReeLib;
using ReeLib.Gui;

namespace ContentEditor.App.ImguiHandling;

public class GuiEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "GUI";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public GuiFile File => Handle.GetFile<GuiFile>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    static GuiEditor()
    {
        WindowHandlerFactory.DefineInstantiator<GuiContainer>(ctx => {
            var version = ctx.FindHandlerInParents<GuiEditor>()?.File.Header.GuiVersion ?? GuiVersion.RE4;
            var instance = new GuiContainer(version);
            return instance;
        });
        WindowHandlerFactory.DefineInstantiator<GuiClip>(ctx => {
            var version = ctx.FindHandlerInParents<GuiEditor>()?.File.Header.GuiVersion ?? GuiVersion.RE4;
            var instance = new GuiClip(version);
            return instance;
        });
        WindowHandlerFactory.DefineInstantiator<Element>(ctx => {
            var version = ctx.FindHandlerInParents<GuiEditor>()?.File.Header.GuiVersion ?? GuiVersion.RE4;
            var instance = new Element(version);
            return instance;
        });
        WindowHandlerFactory.DefineInstantiator<AttributeOverride>(ctx => {
            var version = ctx.FindHandlerInParents<GuiEditor>()?.File.Header.GuiVersion ?? GuiVersion.RE4;
            var instance = new AttributeOverride(version);
            return instance;
        });
    }

    public GuiEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild<GuiFile, DisplayElement>("Root View Element", File, getter: (f) => f!.RootView!).AddDefaultHandler();
            context.AddChild<GuiFile, List<GuiContainer>>("Containers", File, new ListHandlerTyped<GuiContainer>(), (f) => f!.Containers);
            context.AddChild<GuiFile, List<AttributeOverride>>("Attribute Overrides", File, getter: (f) => f!.AttributeOverrides!).AddDefaultHandler();
            context.AddChild<GuiFile, List<string>>("Included GUIs", File, new ResourceListPathPicker(Workspace, KnownFileFormats.GUI), (f) => f!.LinkedGUIs!);
            context.AddChild<GuiFile, List<string>>("Resources", File, new ResourceListPathPicker(Workspace), (f) => f!.Resources!);
            context.AddChild<GuiFile, List<GuiParameter>>("Parameters", File, new ListHandlerTyped<GuiParameter>(), (f) => f!.Parameters!);
            context.AddChild<GuiFile, List<GuiParameterReference>>("Parameter References", File, new ListHandlerTyped<GuiParameterReference>(), (f) => f!.ParameterReferences!);
            context.AddChild<GuiFile, List<GuiParameterOverride>>("Parameter Overrides", File, new ListHandlerTyped<GuiParameterOverride>(), (f) => f!.ParameterOverrides!);
        }

        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}
