using ContentPatcher;
using Lua;
using ReeLib;

namespace ContentEditor.App.Lua;

[LuaObject]
public partial class LuaFileHandleWrapper : ILuaObjectWrapper
{
    private readonly FileHandle file;
    private readonly ContentWorkspace workspace;

    public LuaFileHandleWrapper(FileHandle file, ContentWorkspace workspace)
    {
        this.file = file;
        this.workspace = workspace;
        LuaReflectionObject.CreateObjectMixedMetaTable(this);
    }

    public FileHandle File => file;
    object ILuaObjectWrapper.Object => file;

    public static LuaValue GetFileResource(LuaFileHandleWrapper file)
    {
        switch (file.File.Format.format) {
            case ReeLib.KnownFileFormats.Message:
                return new LuaMsg(file);
        }
        return new LuaDefaultResource(file);
    }

    // private LuaValue? resource;
    [LuaMember("resource")]
    public LuaValue Resource => GetFileResource(this);

    [LuaMember("type")]
    public string FileType => file.Format.format.ToString();

    [LuaMember("format")]
    public string Format => file.Format.ToString();

    [LuaMember("filename")]
    public string Filename => file.Filename.ToString();

    [LuaMember("filepath")]
    public string Filepath => file.Filepath;

    [LuaMember("target_path")]
    public string? TargetPath => file.TargetPath;

    [LuaMember("resource_path")]
    public string? ResourcePath => file.ResourcePath;

    [LuaMember("modified")]
    public bool Modified { get => file.Modified; set => file.Modified = value; }

    [LuaMember("revert")]
    public void Revert() => file.Revert(workspace);

    [LuaMember("save")]
    public void Save() => file.Save(workspace);

    [LuaMember("save_as")]
    public void SaveAs(string path) => file.Save(workspace, path);

    [LuaMember("close")]
    public void Close() => workspace.ResourceManager.CloseFile(file);
}

[LuaObject]
public partial class LuaBaseResource(LuaFileHandleWrapper file) : ILuaObjectWrapper
{
    [LuaMember("handle")]
    public LuaFileHandleWrapper Handle { get; } = file;

    public object Object => Handle.File.Resource;
}

[LuaObject]
public partial class LuaFileResource<TFile>(LuaFileHandleWrapper file) : LuaBaseResource(file) where TFile : BaseFile
{
    public TFile File { get; } = file.File.GetFile<TFile>();
}

[LuaObject]
public partial class LuaDefaultResource : LuaBaseResource
{
    public LuaDefaultResource(LuaFileHandleWrapper file) : base(file)
    {
        LuaReflectionObject.CreateObjectMixedMetaTable(this);
    }
}
