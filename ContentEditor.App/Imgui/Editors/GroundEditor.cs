using ContentEditor.App.FileLoaders;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

public class GroundEditor : FileEditor
{
    public GroundEditor(FileHandle file) : base(file)
    {
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            var instance = Handle.GetFile<GrndFile>();
            var ws = context.GetWorkspace();
            context.AddChild<GrndFile, ReeLib.Grnd.Header>("Header", instance, getter: v => v!.Header).AddDefaultHandler();
            context.AddChild<GrndFile, List<ReeLib.Grnd.GroundDataHeaders>>("Content Headers", instance, getter: v => v!.ContentHeaders).AddDefaultHandler();
            context.AddChild<GrndFile, List<string>>("Materials", instance, new ResourceListPathPicker(ws, KnownFileFormats.GroundTextureList), getter: v => v!.GroundTextures);
        }

        context.ShowChildrenUI();
    }
}

public class GroundTextureEditor : FileEditor
{
    public GroundTextureEditor(FileHandle file) : base(file)
    {
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            var instance = Handle.GetResource<GroundTerrainResourceFile>().File;
            var ws = context.GetWorkspace();
            context.AddChild<GtlFile, ReeLib.Gtl.Header>("Header", instance, getter: v => v!.Header).AddDefaultHandler();
            context.AddChild<GtlFile, List<int>>("Indices", instance, getter: v => v!.Indices).AddDefaultHandler();
            context.AddChild<GtlFile, List<ReeLib.Gtl.HeightmapRange>>("Ranges", instance, getter: v => v!.Ranges).AddDefaultHandler();
            context.AddChild<GtlFile, List<ReeLib.Gtl.GtlData>>("Data", instance, getter: v => v!.DataItems).AddDefaultHandler();
        }

        context.ShowChildrenUI();
    }
}

public class GroundMaterialEditor : FileEditor
{
    public GroundMaterialEditor(FileHandle file) : base(file)
    {
    }

    protected override void DrawFileContents()
    {
        if (context.children.Count == 0) {
            var instance = Handle.GetResource<GroundMaterialResourceFile>().File;
            var ws = context.GetWorkspace();
            context.AddChild<GmlFile, ReeLib.Gml.Header>("Header", instance, getter: v => v!.Header).AddDefaultHandler();
            context.AddChild<GmlFile, List<ReeLib.Gml.GroundMaterialTexture>>("Textures", instance, getter: v => v!.Textures).AddDefaultHandler();
            context.AddChild<GmlFile, List<ReeLib.Gml.GroundMaterialData>>("Data", instance, getter: v => v!.Data).AddDefaultHandler();
        }

        context.ShowChildrenUI();
    }
}
