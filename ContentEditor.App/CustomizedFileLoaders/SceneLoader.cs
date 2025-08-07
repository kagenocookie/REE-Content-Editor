using ContentEditor.App;
using ReeLib;
using ReeLib.Scn;

namespace ContentPatcher;

public class SceneLoader : IFileLoader,
    IFileHandleContentProvider<ScnFile>,
    IFileHandleContentProvider<RawScene>
{
    int IFileLoader.Priority => 30;

    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.Scene;

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new ScnFile(workspace.Env.RszFileOption, fileHandler);
        if (!file.Read()) return null;

        file.SetupGameObjects();
        return new RawScene(handle, file);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var RawScene = handle.GetResource<RawScene>();
        RawScene.WriteTo(outputPath);
        return true;
    }

    public ScnFile GetFile(FileHandle handle) => handle.GetResource<RawScene>().File;

    RawScene IFileHandleContentProvider<RawScene>.GetFile(FileHandle handle)
    {
        return handle.GetResource<RawScene>();
    }
}

public class RawScene(FileHandle handle, ScnFile file) : IResourceFile
{
    private Folder? _rootInstance;
    public ScnFile File => file;

    public ScnFile GetExportedFile()
    {
        ExportData();
        return file;
    }

    public Folder GetSharedInstance(Workspace env)
    {
        return _rootInstance ??= Instantiate(env);
    }

    public Folder Instantiate(Workspace env, Scene scene)
    {
        return Instantiate(env, scene.RootFolder);
    }

    public Folder Instantiate(Workspace env, Folder? parentFolder = null)
    {
        var fn = Path.GetFileName(PathUtils.GetFilepathWithoutExtensionOrVersion(file.FileHandler.FilePath));
        var root = new Folder(
            fn.ToString(),
            env,
            File.FolderDatas?.AsEnumerable() ?? Array.Empty<ScnFolderData>(),
            File.GameObjects?.AsEnumerable() ?? Array.Empty<ScnGameObject>(),
            File.PrefabInfoList,
            parentFolder?.Scene
        );
        parentFolder?.AddChild(root);
        return root;
    }

    public void WriteTo(string filepath)
    {
        var file = GetExportedFile();
        file.RebuildInfoTable();
        file.SaveOrWriteTo(handle, filepath);
    }
    private void ExportData()
    {
        if (_rootInstance == null) return;

        file.Clear();
        file.FolderDatas ??= new();
        file.GameObjects ??= new();
        foreach (var folder in _rootInstance.Children) {
            file.FolderDatas.Add(folder.ToScnFolder(file.PrefabInfoList));
        }
        foreach (var folder in _rootInstance.GameObjects) {
            file.GameObjects.Add(folder.ToScnGameObject(file.PrefabInfoList));
        }
    }

}