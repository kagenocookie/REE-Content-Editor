using System.Numerics;
using Assimp;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Aimp;
using ReeLib.Bvh;
using ReeLib.Terr;

namespace ContentEditor.App.ImguiHandling;

public class AimapEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "AI Map";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public AimpFile File => Handle.GetFile<AimpFile>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public AimapEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            context.AddChild<AimpFile, AimpHeader>("Header", File, getter: (f) => f!.Header).AddDefaultHandler();
            context.AddChild<AimpFile, RSZFile>("RSZ Data", File, getter: (f) => f!.RSZ).AddDefaultHandler();
            context.AddChild<AimpFile, ContentGroupContainer>("Main Content", File, getter: (f) => f!.mainContent, setter: (f, v) => f.mainContent = v).AddDefaultHandler<ContentGroupContainer>();
            context.AddChild<AimpFile, ContentGroupContainer>("Secondary Content", File, getter: (f) => f!.secondaryContent, setter: (f, v) => f.secondaryContent = v).AddDefaultHandler<ContentGroupContainer>();
            context.AddChild<AimpFile, MapLayers[]>("Map Layers", File, getter: (f) => f!.layers, setter: (f, v) => f.layers = v).AddDefaultHandler<MapLayers[]>();
            context.AddChild<AimpFile, ulong>("Embed Hash 1", File, getter: (f) => f!.embedHash1, setter: (f, v) => f.embedHash1 = v).AddDefaultHandler<ulong>();
            context.AddChild<AimpFile, ulong>("Embed Hash 2", File, getter: (f) => f!.embedHash2, setter: (f, v) => f.embedHash2 = v).AddDefaultHandler<ulong>();
            context.AddChild<AimpFile, EmbeddedMap[]>("Embeds", File, getter: (f) => f!.embeds, setter: (f, v) => f.embeds = v ?? []).AddDefaultHandler<EmbeddedMap[]>();
        }

        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(AimpHeader))]
public class AimpHeaderEditor : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        var rset = context.Get<AimpHeader>();
        if (context.children.Count == 0) {
            context.AddChild<AimpHeader, string>("Name", rset, getter: (i) => i!.name, setter: (i, v) => i.name = v).AddDefaultHandler<string>();
            context.AddChild<AimpHeader, string>("Hash", rset, getter: (i) => i!.hash, setter: (i, v) => i.hash = v ?? string.Empty).AddDefaultHandler<string>();
            context.AddChild<AimpHeader, Guid>("Guid", rset, getter: (i) => i!.guid, setter: (i, v) => i.guid = v).AddDefaultHandler<Guid>();
            context.AddChild<AimpHeader, MapType>("Map Type", rset, getter: (i) => i!.mapType, setter: (i, v) => i.mapType = v).AddDefaultHandler<MapType>();
            context.AddChild<AimpHeader, SectionType>("Section Type", rset, getter: (i) => i!.sectionType, setter: (i, v) => i.sectionType = v).AddDefaultHandler<SectionType>();
            context.AddChild<AimpHeader, float>("Agent Rad When Build", rset, getter: (i) => i!.agentRadWhenBuild, setter: (i, v) => i.agentRadWhenBuild = v).AddDefaultHandler<float>();
            context.AddChild<AimpHeader, ulong>("URI Hash", rset, getter: (i) => i!.uriHash, setter: (i, v) => i.uriHash = v).AddDefaultHandler<ulong>();
            context.AddChild<AimpHeader, int>("uknId", rset, getter: (i) => i!.uknId, setter: (i, v) => i.uknId = v).AddDefaultHandler<int>();
        }

        context.ShowChildrenUI();
    }
}
