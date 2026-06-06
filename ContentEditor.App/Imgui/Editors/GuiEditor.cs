using ContentPatcher;
using ReeLib;
using ReeLib.Clip;
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
        static GuiVersion GetGuiVersion(UIContext ctx) => ctx.FindHandlerInParents<GuiEditor>()?.File.Header.GuiVersion ?? GuiVersion.Pragmata;

        WindowHandlerFactory.DefineInstantiator<GuiContainer>(ctx => new GuiContainer(GetGuiVersion(ctx)));
        WindowHandlerFactory.DefineInstantiator<GuiClip>(ctx => new GuiClip(GetGuiVersion(ctx)));
        WindowHandlerFactory.DefineInstantiator<Element>(ctx => new Element(GetGuiVersion(ctx)));
        WindowHandlerFactory.DefineInstantiator<AttributeOverride>(ctx => new AttributeOverride(GetGuiVersion(ctx)));
        WindowHandlerFactory.DefineInstantiator<ReeLib.Gui.Attribute>(ctx => new ReeLib.Gui.Attribute(GetGuiVersion(ctx)));
        WindowHandlerFactory.DefineInstantiator<ContainerAttribute1>(ctx => new ContainerAttribute1(GetGuiVersion(ctx)));
        WindowHandlerFactory.DefineInstantiator<ContainerAttribute2>(ctx => new ContainerAttribute2(GetGuiVersion(ctx)));
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

[ObjectImguiHandler(typeof(AttributeOverride))]
[ObjectImguiHandler(typeof(GuiParameterOverride))]
[ObjectImguiHandler(typeof(GuiParameterReference))]
[ObjectImguiHandler(typeof(ReeLib.Gui.Attribute))]
[ObjectImguiHandler(typeof(ContainerAttribute1))]
[ObjectImguiHandler(typeof(ContainerAttribute2))]
public class GuiAttributeHandler : IObjectUIHandler, IUIContextEventHandler
{
    public bool HandleEvent(UIContext context, EditorUIEvent eventData)
    {
        if (eventData.IsChangeFromChild && eventData.origin.GetRaw()?.GetType() == typeof(PropertyType)) {
            context.ClearChildren();
        }
        return true;
    }

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var instance = context.GetRaw();
            WindowHandlerFactory.SetupObjectUIContext(context, instance?.GetType());
        }
        context.ShowChildrenNestedUI();
    }
}