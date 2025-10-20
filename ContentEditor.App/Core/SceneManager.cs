using ContentEditor.App.ImguiHandling;
using ContentEditor.Editor;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App;

public sealed class SceneManager(IRectWindow window) : IDisposable
{
    private readonly List<Scene> scenes = new();
    private readonly List<Scene> rootScenes = new();
    private ContentWorkspace? env = (window as IWorkspaceContainer)?.Workspace;

    public IRectWindow Window { get; } = window;

    public IEnumerable<Scene> RootMasterScenes => rootScenes.Where(scene => scene.OwnRenderContext.RenderTargetTextureHandle == 0 && scene.ParentScene == null);
    public bool HasActiveMasterScene => RootMasterScenes.Where(sc => sc.IsActive).Any();
    public Scene? ActiveMasterScene => RootMasterScenes.FirstOrDefault(sc => sc.IsActive);
    public IEnumerable<Scene> RootScenes => rootScenes;

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
        if (parentScene == null) rootScenes.Add(scene);
        return scene;
    }

    private void RemoveScene(Scene scene)
    {
        scenes.Remove(scene);
        rootScenes.Remove(scene);
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

        foreach (var master in rootScenes) {
            if (master.MouseHandler != null) {
                if (master.IsActive) {
                    master.MouseHandler.scene = master;
                } else {
                    master.MouseHandler.scene = null;
                }
            }

            if (master.Controller != null) {
                if (master.IsActive) {
                    master.Controller.Scene = master;
                    master.Controller.Update(deltaTime);
                } else {
                    master.Controller.Scene = null!;
                }
            }
        }

    }

    public void Render(float deltaTime)
    {
        foreach (var scene in rootScenes) {
            if (!scene.IsActive) continue;

            scene.OwnRenderContext.ScreenSize = Window.Size;
            if (scene.OwnRenderContext.RenderTargetTextureHandle == 0) {
                scene.OwnRenderContext.ViewportSize = Window.Size - scene.OwnRenderContext.ViewportOffset;
                scene.Render(deltaTime);
                ImGui.SetNextWindowPos(scene.OwnRenderContext.ViewportOffset, ImGuiCond.Always);
                ImGui.SetNextWindowSize(scene.OwnRenderContext.ViewportSize, ImGuiCond.Always);
                ImGui.Begin(scene.Name, ImGuiWindowFlags.NoTitleBar|ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoMove|ImGuiWindowFlags.NoScrollbar|ImGuiWindowFlags.NoScrollWithMouse|ImGuiWindowFlags.NoCollapse|ImGuiWindowFlags.NoDecoration|ImGuiWindowFlags.NoBackground|ImGuiWindowFlags.NoFocusOnAppearing|ImGuiWindowFlags.NoBringToFrontOnFocus|ImGuiWindowFlags.NoInputs);
                scene.RenderUI();
                ImGui.End();
            } else {
                scene.Render(deltaTime);
            }
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
        rootScenes.Clear();
        scenes.Clear();
    }

    public void Dispose()
    {
        ClearScenes();
    }
}