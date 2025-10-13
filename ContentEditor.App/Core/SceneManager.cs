using ContentEditor.App.ImguiHandling;
using ContentEditor.Editor;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

public sealed class SceneManager(IRectWindow window) : IDisposable
{
    private readonly List<Scene> scenes = new();
    private ContentWorkspace? env = (window as IWorkspaceContainer)?.Workspace;

    public IRectWindow Window { get; } = window;

    public IEnumerable<Scene> RootMasterScenes => scenes.Where(scene => scene.OwnRenderContext.RenderTargetTextureHandle == 0 && scene.ParentScene == null);
    public bool HasActiveMasterScene => RootMasterScenes.Where(sc => sc.IsActive).Any();
    public Scene? ActiveMasterScene => RootMasterScenes.FirstOrDefault(sc => sc.IsActive);

    public Scene CreateScene(FileHandle sourceFile, bool render, Scene? parentScene = null, Folder? rootFolder = null)
    {
        return CreateScene(sourceFile.Filepath, sourceFile.InternalPath ?? sourceFile.Filepath, render, parentScene, rootFolder);
    }

    public Scene CreateScene(string name, string internalPath, bool render, Scene? parentScene = null, Folder? rootFolder = null)
    {
        if (env == null) throw new Exception("Workspace unset");

        // convert in case we received a native and not internal path
        internalPath = PathUtils.GetInternalFromNativePath(internalPath);
        var scene = new Scene(name, internalPath, env, parentScene, rootFolder) { IsActive = render, SceneManager = this };
        scene.OwnRenderContext.ResourceManager = env.ResourceManager;
        scenes.Add(scene);
        // if (render) Logger.Debug("Loading scene " + rootFolder?.Name ?? internalPath);
        rootFolder?.MoveToScene(scene);
        return scene;
    }

    private void RemoveScene(Scene scene)
    {
        scenes.Remove(scene);
        foreach (var sub in scene.ChildScenes) RemoveScene(sub);
    }

    public void UnloadScene(Scene scene)
    {
        RemoveScene(scene);
        scene?.Dispose();
    }

    internal void ChangeWorkspace(ContentWorkspace workspace)
    {
        if (env == workspace) return;

        ClearScenes();
        env = workspace;
    }

    public void Update(float deltaTime)
    {
        for (int i = 0; i < scenes.Count; i++) {
            Scene? scene = scenes[i];
            scene.ExecuteDeferredActions();
        }

        foreach (var scene in scenes) {
            scene.Update(deltaTime);
        }
    }

    public void Render(float deltaTime)
    {
        // should we sort scenes with render buffers first and then the main viewport scenes to minimize viewport switching?
        foreach (var scene in scenes) {
            scene.OwnRenderContext.ViewportSize = Window.Size;
            if (scene.ParentScene != null) continue;
            scene.Render(deltaTime);
        }
    }

    public void ChangeMasterScene(Scene? scene)
    {
        foreach (var master in RootMasterScenes) {
            if (master.IsActive) {
                if (master == scene) return;
                master.SetActive(false);
            }
        }

        scene?.SetActive(true);
    }

    private void ClearScenes()
    {
        foreach (var scene in scenes) {
            scene.Dispose();
        }
        scenes.Clear();
    }

    public void Dispose()
    {
        ClearScenes();
    }
}