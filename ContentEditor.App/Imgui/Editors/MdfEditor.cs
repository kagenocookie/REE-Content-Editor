using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Mdf;
using ReeLib.UVar;
using ReeLib.via;

namespace ContentEditor.App.ImguiHandling.Mdf2;

public class MdfEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "Mdf2";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public MdfFile File => Handle.GetContent<MdfFile>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public MdfEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    protected override void OnFileReverted()
    {
        Reset();
    }

    private void Reset()
    {
        if (context.children.Count > 0) {
            // not letting the child contexts dispose - so we don't dispose the file stream
            context.children.Clear();
        }
        failedToReadfile = false;
    }

    protected override void DrawFileContents()
    {
        var isEmpty = context.children.Count == 0;
        if (context.children.Count == 0) {
            context.AddChild("Data", File, new MdfFileImguiHandler());
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(MdfFile))]
public class MdfFileImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var file = context.Get<MdfFile>();
            context.AddChild("Data", file.MatDatas).AddDefaultHandler<List<MatData>>();
        }
        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(MatHeader))]
public class MatHeaderImguiHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(MatHeader).GetField(nameof(MatHeader.matName))!,
        typeof(MatHeader).GetField(nameof(MatHeader.shaderType))!,
        typeof(MatHeader).GetProperty(nameof(MatHeader.Flags1))!,
        typeof(MatHeader).GetProperty(nameof(MatHeader.Flags2))!,
        typeof(MatHeader).GetProperty(nameof(MatHeader.Phong))!,
        typeof(MatHeader).GetProperty(nameof(MatHeader.Tesselation))!,
        typeof(MatHeader).GetField(nameof(MatHeader.ukn1))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var tex = context.Get<MatHeader>();
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(MatHeader), members: DisplayedFields);
            context.AddChild<MatHeader, string>("MMTR path", tex, new ResourcePathPicker(ws, KnownFileFormats.MasterMaterial), (p) => p.mmtrPath, (p, v) => p.mmtrPath = v);
        }
        ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(TexHeader))]
public class TexHeaderImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var tex = context.Get<TexHeader>();
            context.AddChild<TexHeader, string>(tex.texType, tex, new StringFieldHandler(), (p) => p.texPath, (p, v) => p.texPath = v);
        }
        context.children[0].ShowUI();
    }
}

[ObjectImguiHandler(typeof((GpbfHeader name, GpbfHeader data)))]
public class Mdf2GpbfPairImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var tex = context.Get<(GpbfHeader name, GpbfHeader data)>();
            context.AddChild<(GpbfHeader name, GpbfHeader data), string>(tex.name.name, tex, new StringFieldHandler(), (p) => p.data.name, (p, v) => p.data.name = v ?? string.Empty);
        }
        context.children[0].ShowUI();
    }
}

[ObjectImguiHandler(typeof(List<MatData>))]
public class MdfMaterialListImguiHandler : DictionaryListImguiHandler<string, MatData, List<MatData>>
{
    protected override string GetKey(MatData item) => item.Header.matName;

    protected override MatData CreateItem(UIContext context, string key)
        => new MatData(new MatHeader(context.FindClassValueInParentValues<MdfFile>()!.Option.Version) { matName = key });
}

[ObjectImguiHandler(typeof(List<TexHeader>))]
public class TexHeaderListImguiHandler : DictionaryListImguiHandler<string, TexHeader, List<TexHeader>>
{
    public TexHeaderListImguiHandler()
    {
        AllowAdditions = false;
    }

    protected override string GetKey(TexHeader item) => item.texType;

    protected override TexHeader CreateItem(UIContext context, string key)
        => new TexHeader(context.FindClassValueInParentValues<MdfFile>()!.Option.Version);
}


[ObjectImguiHandler(typeof(List<ParamHeader>))]
public class ParamHeaderListImguiHandler : DictionaryListImguiHandler<string, ParamHeader, List<ParamHeader>>
{
    public ParamHeaderListImguiHandler()
    {
        AllowAdditions = false;
    }

    protected override string GetKey(ParamHeader item) => item.paramName;

    protected override ParamHeader CreateItem(UIContext context, string key)
        => new ParamHeader(context.FindClassValueInParentValues<MdfFile>()!.Option.Version);
}

[ObjectImguiHandler(typeof(ParamHeader))]
public class ParamHeaderImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var param = context.Get<ParamHeader>();
            switch (param.componentCount) {
                case 1:
                    context.AddChild<ParamHeader, float>(param.paramName, param, new NumericFieldHandler<float>(ImGuiDataType.Float), (p) => p.parameter.X, (p, v) => p.parameter = new Vector4(v, 0, 0, 0));
                    break;
                case 2:
                    context.AddChild<ParamHeader, Vector2>(param.paramName, param, new Vector2FieldHandler(), (p) => p.parameter.ToVec2(), (p, v) => p.parameter = v.ToVec4());
                    break;
                case 3:
                    context.AddChild<ParamHeader, Vector3>(param.paramName, param, new Vector3FieldHandler(), (p) => p.parameter.ToVec3(), (p, v) => p.parameter = v.ToVec4());
                    break;
                default:
                case 4:
                    if (param.paramName.Contains("color", StringComparison.OrdinalIgnoreCase)) {
                        context.AddChild<ParamHeader, Color>(param.paramName, param, new ColorFieldHandler(), (p) => Color.FromVector4(p.parameter), (p, v) => p.parameter = v.ToVector4());
                    } else {
                        context.AddChild<ParamHeader, Vector4>(param.paramName, param, new Vector4FieldHandler(), (p) => p.parameter, (p, v) => p.parameter = v);
                    }
                    break;
            }
        }
        context.children[0].ShowUI();
    }
}
