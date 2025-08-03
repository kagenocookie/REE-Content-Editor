using ContentEditor.App;
using ReeLib;

namespace ContentPatcher;

public class PrefabLoader : IFileLoader,
    IFileHandleContentProvider<PfbFile>,
    IFileHandleContentProvider<GameObject>,
    IFileHandleContentProvider<Prefab>,
    IFileHandleContentProvider<Scene>
{
    int IFileLoader.Priority => 30;
    public bool CanHandleFile(string filepath, REFileFormat format) => format.format == KnownFileFormats.Prefab;

    public IResourceFilePatcher? CreateDiffHandler() => null;

    public IResourceFile? Load(ContentWorkspace workspace, FileHandle handle)
    {
        var fileHandler = new FileHandler(handle.Stream, handle.Filepath);
        var file = new PfbFile(workspace.Env.RszFileOption, fileHandler);
        if (!file.Read()) return null;

        file.SetupGameObjects();
        return new Prefab(handle, file);
    }

    public bool Save(ContentWorkspace workspace, FileHandle handle, string outputPath)
    {
        var prefab = handle.GetResource<Prefab>();
        prefab.WriteTo(outputPath);
        return true;
    }

    public PfbFile GetFile(FileHandle handle) => handle.GetResource<Prefab>().File;

    GameObject IFileHandleContentProvider<GameObject>.GetFile(FileHandle handle)
    {
        var prefab = handle.GetResource<Prefab>();
        return prefab.GetSharedInstance();
    }

    Prefab IFileHandleContentProvider<Prefab>.GetFile(FileHandle handle)
    {
        return handle.GetResource<Prefab>();
    }

    Scene IFileHandleContentProvider<Scene>.GetFile(FileHandle handle)
    {
        var prefab = handle.GetResource<Prefab>();
        return prefab.GetSharedInstance().Scene!;
    }
}

public class Prefab(FileHandle handle, PfbFile file) : IResourceFile
{
    private GameObject? _instance;
    public PfbFile File => file;

    public PfbFile GetExportedFile()
    {
        ExportGameObject();
        return file;
    }

    public GameObject GetSharedInstance()
    {
        return _instance ??= Instantiate();
    }

    public GameObject Instantiate(Scene? scene = null)
    {
        return new GameObject(file.GameObjects![0], scene);
    }

    public void WriteTo(string filepath)
    {
        var file = GetExportedFile();
        file.RebuildInfoTables();
        file.SaveOrWriteTo(handle, filepath);
    }
    private void ExportGameObject()
    {
        if (_instance == null) return;

        file.ClearGameObjects();
        file.AddGameObject(_instance.ToPfbGameObject());
        file.RSZ.RebuildInstanceList();
    }

}