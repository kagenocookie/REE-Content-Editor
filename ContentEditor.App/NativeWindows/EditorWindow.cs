namespace ContentEditor.App.Windowing;

using System;
using System.Diagnostics;
using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Data;
using ReeLib.Efx;
using ReeLib.Tools;
using Silk.NET.Input;
using Silk.NET.Maths;

public class EditorWindow : WindowBase, IWorkspaceContainer
{
    protected readonly List<IWindowHandler> windowHandlers = new();

    /// <summary>
    /// Get the current IMGUI-rendering window. Should be null outside of the imgui callback
    /// </summary>
    public static EditorWindow? CurrentImguiWindow { get; protected set; }

    /// <summary>
    /// Get the currently rendering window. Should never be null during the ImGui render phase, likely to be null in multithreaded contexts.
    /// </summary>
    public static EditorWindow? CurrentWindow => _currentRenderingWindow as EditorWindow;

    public ContentWorkspace Workspace => workspace!;

    protected Workspace? env;
    protected ContentWorkspace? workspace;
    public event Action? GameChanged;

    public SceneManager SceneManager { get; }

    private static HashSet<string>? fullSupportedGames;

    private static bool runningRszInference;

    private ActiveSceneBehavior sceneBehavior = new();
    private ActiveSceneBehavior? ActiveSceneBehavior => SceneManager.HasActiveMasterScene ? sceneBehavior : null;

    /// <summary>
    /// True when the last input was on the main window content and not any imgui windows.
    /// </summary>
    private bool IsWindowContentFocused => isMouseDown || lastClickWasWindow;

    private Vector2 firstMouseDownPosition;
    private Vector2 lastLeftMouseDownPosition;
    private Vector2 lastRightMouseDownPosition;
    private Vector2 lastDragMousePosition;
    private MouseButton firstMouseDownButton;
    private bool isDragging = false;
    private bool isMouseDown = false;
    private bool lastClickWasWindow = false;

    protected bool isBaseWindowFocused;

    internal EditorWindow(int id, ContentWorkspace? workspace = null) : base(id)
    {
        Ready += OnReady;
        this.workspace = workspace;
        env = workspace?.Env;
        SceneManager = new(this);
    }

    public void SetWorkspace(GameIdentifier game, string? bundle)
    {
        // close all subwindows since they won't necessarily have the correct data anymore
        if (!RequestCloseAllSubwindows()) return;
        if (env != null && env.Config.Game != game) {
            WorkspaceManager.Instance.Release(env);
            env = null;
            workspace = null;
        }
        env ??= WorkspaceManager.Instance.GetWorkspace(game);

        string? configPath = AppConfig.Instance.GameConfigBaseFilepath;
        if (string.IsNullOrEmpty(configPath)) {
            configPath = $"configs/{game.name}";
        } else {
            configPath = $"{configPath}/{game.name}";
        }
        var patchConfig = workspace?.Config ?? new PatchDataContainer(Path.GetFullPath(configPath));

        workspace = new ContentWorkspace(env, patchConfig, workspace?.BundleManager);
        workspace.ResourceManager.SetupFileLoaders(typeof(PrefabLoader).Assembly);
        SetupTypes(workspace);
        workspace.SetBundle(bundle);
        GameChanged?.Invoke();
        SceneManager.ChangeWorkspace(workspace);
    }

    private static void SetupTypes(ContentWorkspace workspace)
    {
        foreach (var (name, cfg) in workspace.Config.Classes) {
            if (cfg.StringFormatter != null) {
                var cls = workspace.Env.RszParser.GetRSZClass(name);
                if (cls == null) continue;

                WindowHandlerFactory.SetClassFormatter(cls, cfg.StringFormatter);
            }
        }

        WindowHandlerFactory.SetupTypesForGame(workspace.Game, workspace.Env);
        Component.SetupTypesForGame(workspace.Game, typeof(Component).Assembly, workspace.Env);
    }

    protected override void Update(float deltaTime)
    {
        SceneManager.Update(deltaTime);
        sceneBehavior.Keyboard = _inputContext.Keyboards[0];
        if (IsWindowContentFocused) {
            ActiveSceneBehavior?.Update(deltaTime);
        }
    }

    protected override void Render(float deltaTime)
    {
        SceneManager.Render(deltaTime);
    }

    private void OnReady()
    {
        AddSubwindow(new ConsoleWindow());
    }

    protected override void OnFileDrop(string[] filenames, Vector2D<int> position)
    {
        if (env == null || workspace == null) {
            AddSubwindow(new ErrorModal("Game unset", "Select a game first"));
            return;
        }

        foreach (var filename in filenames) {
            if (filename.EndsWith(".pak")) {
                AppConfig.Instance.AddRecentFile(filename);
                AddSubwindow(new PakBrowser(workspace.Env, filename));
                continue;
            }

            if (workspace.ResourceManager.TryGetOrLoadFile(filename, out var file)) {
                AddFileEditor(file);
            } else {
                AddSubwindow(new ErrorModal("Unsupported file", "File is not supported:\n" + filename));
            }
        }
    }

    protected override void SetupMouse(IMouse mouse)
    {
        mouse.DoubleClickTime = 400;
        mouse.MouseMove += (m, vec) => {
            if (ImGui.GetIO().WantCaptureMouse) return;
            OnMouseMove(m, vec);
        };
        mouse.Click += (m, btn, v) => {
            if (ImGui.GetIO().WantCaptureMouse) return;
            OnMouseClick(m, btn, v);
        };
        mouse.DoubleClick += (m, btn, v) => {
            if (ImGui.GetIO().WantCaptureMouse) return;
            OnMouseDoubleClick(m, btn, v);
        };
        mouse.MouseDown += (m, btn) => {
            isBaseWindowFocused = !ImGui.GetIO().WantCaptureMouse;
            if (!isBaseWindowFocused) {
                lastClickWasWindow = false;
                return;
            }
            lastClickWasWindow = true;
            OnMouseDown(m, btn, m.Position);
        };
        mouse.MouseUp += (m, btn) => {
            if (ImGui.GetIO().WantCaptureMouse) return;
            OnMouseUp(m, btn, m.Position);
        };
    }

    protected virtual void OnMouseClick(IMouse mouse, Silk.NET.Input.MouseButton button, Vector2 position)
    {
    }

    protected virtual void OnMouseDoubleClick(IMouse mouse, Silk.NET.Input.MouseButton button, Vector2 position)
    {
    }

    protected virtual void OnMouseDown(IMouse mouse, Silk.NET.Input.MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Left) {
            lastLeftMouseDownPosition = position;
            if (!isMouseDown) {
                isMouseDown = true;
                firstMouseDownButton = button;
                firstMouseDownPosition = position;
                lastDragMousePosition = position;
            }
        } else if (button == MouseButton.Right) {
            lastRightMouseDownPosition = position;
            if (!isMouseDown) {
                isMouseDown = true;
                firstMouseDownButton = button;
                firstMouseDownPosition = position;
                lastDragMousePosition = position;
            }
        }
    }

    protected virtual void OnMouseUp(IMouse mouse, Silk.NET.Input.MouseButton button, Vector2 position)
    {
        mouse.IsButtonPressed(MouseButton.Left);
        if (!mouse.IsButtonPressed(MouseButton.Right) && !mouse.IsButtonPressed(MouseButton.Left)) {
            isMouseDown = false;
            OnStopMouseDrag(mouse);
        }
    }

    protected virtual void OnStopMouseDrag(IMouse mouse)
    {
        isDragging = false;
        var scene = SceneManager.ActiveMasterScene;
        if (scene != null) {
            ActiveSceneBehavior?.OnMouseDragEnd(mouse, firstMouseDownButton, mouse.Position, firstMouseDownPosition);
        }
    }

    protected virtual void OnMouseMove(IMouse mouse, Vector2 pos)
    {
        // var scene = SceneManager.ActiveMasterScene;
        if (isMouseDown) {
            var delta = lastDragMousePosition - pos;
            lastDragMousePosition = pos;
            if (delta != Vector2.Zero && !isDragging) {
                isDragging = true;
                ActiveSceneBehavior?.OnMouseDragStart(mouse, firstMouseDownButton, pos);
            }
            if (isDragging) {
                var left = mouse.IsButtonPressed(MouseButton.Left);
                var right = mouse.IsButtonPressed(MouseButton.Right);
                var mouseEnum = (left ? MouseButtonFlags.Left : 0) | (right ? MouseButtonFlags.Right : 0);
                ActiveSceneBehavior?.OnMouseDrag(mouseEnum, pos, delta);
            }
        }
    }

    public void AddFileEditor(FileHandle file)
    {
        if (workspace == null) return;

        var handler = WindowHandlerFactory.CreateFileResourceHandler(workspace, file);
        if (handler != null) {
            if (file.HandleType != FileHandleType.Embedded) {
                AppConfig.Instance.AddRecentFile(file.Filepath);
            }
            AddSubwindow(handler);
        } else {
            workspace.ResourceManager.CloseFile(file);
            AddSubwindow(new ErrorModal("Unsupported file", "File is not supported for editing:\n" + file.Filepath));
        }
    }

    protected void ShowGameSelectionMenu()
    {
        fullSupportedGames ??= ResourceRepository.RemoteInfo.Resources
            .Where(kv => kv.Value.IsFullySupported)
            .Select(kv => kv.Key)
            .ToHashSet();

        if (ImGui.BeginMenu("Game: " + (env == null ? "<unset>" : env.Config.Game.name))) {
            var games = AppConfig.Instance.GetGamelist();
            foreach (var (game, configured) in games) {
                if (configured && fullSupportedGames.Contains(game)) {
                    if (ImGui.MenuItem(game)) SetWorkspace(game, null);
                }
            }
            ImGui.Separator();
            foreach (var (game, configured) in games) {
                if (configured && !fullSupportedGames.Contains(game)) {
                    if (ImGui.MenuItem(game)) SetWorkspace(game, null);
                }
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Configure games ...")) {
                AddUniqueSubwindow(new SettingsWindowHandler());
            }
            ImGui.EndMenu();
        }

        if (workspace != null) {
            void ShowBundleManagement()
            {
                AddUniqueSubwindow(new BundleManagementUI(
                    workspace!.BundleManager,
                    workspace.CurrentBundle?.Name,
                    (path) => OpenFiles([path]),
                    (path, diff) => AddSubwindow(new JsonViewer(diff, path))
                ));
            }
            if (ImGui.BeginMenu($"Workspace: {workspace.Data.Name ?? "--"}")) {
                if (!workspace.BundleManager.IsLoaded) workspace.BundleManager.LoadDataBundles();
                if (ImGui.BeginMenu($"Active bundle: {workspace.Data.ContentBundle}")) {
                    if (ImGui.MenuItem("Create new...")) {
                        ShowBundleManagement();
                    }
                    ImGui.Separator();
                    foreach (var b in workspace.BundleManager.AllBundles) {
                        if (ImGui.MenuItem(b.Name)) {
                            SetWorkspace(workspace.Env.Config.Game, b.Name);
                        }
                    }
                    ImGui.EndMenu();
                }
                if (workspace.BundleManager.UninitializedBundleFolders.Count > 0) {
                    if (ImGui.BeginMenu("* Uninitialized bundle folders")) {
                        foreach (var item in workspace.BundleManager.UninitializedBundleFolders) {
                            if (ImGui.MenuItem(item)) {
                                try {
                                    workspace.InitializeUnlabelledBundle(item);
                                } catch (Exception ex) {
                                    Logger.Error(ex, "Failed to set up uninitialized bundle " + item);
                                }
                                break;
                            }
                        }
                        ImGui.EndMenu();
                    }
                }
                if (ImGui.MenuItem("Bundle management")) {
                    ShowBundleManagement();
                }
                if (workspace.CurrentBundle != null && ImGui.MenuItem("Open bundle folder")) {
                    FileSystemUtils.ShowFileInExplorer(workspace.BundleManager.GetBundleFolder(workspace.CurrentBundle));
                }
                ImGui.EndMenu();
            }
        }
    }

    protected virtual void OnFileOpen(Stream stream, string filename)
    {
    }

    public void OpenFiles(string[] filepaths)
    {
        OnFileDrop(filepaths, new Silk.NET.Maths.Vector2D<int>());
    }

    public void OpenFile(Stream stream, string filepath, string discriminator)
    {
        if (workspace == null) return;

        var file = workspace.ResourceManager.CreateFileHandle(discriminator + filepath, null, stream);
        AddFileEditor(file);
    }

    private bool HasUnsavedChanges => workspace?.ResourceManager.GetModifiedResourceFiles().Any() == true;

    protected void ShowMainMenuBar()
    {
        ImGui.BeginMainMenuBar();
        var hasUnsavedFiles = HasUnsavedChanges;
        if (hasUnsavedFiles) {
            ImGui.Bullet();
        }
        if (ImGui.BeginMenu("File")) {
            if (ImGui.MenuItem("Open ...")) {
                PlatformUtils.ShowFileDialog((files) => {
                    MainLoop.Instance.MainWindow.InvokeFromUIThread(() => {
                        Logger.Info(string.Join("\n", files));
                        OpenFiles(files);
                    });
                });
            }
            if (workspace != null) {
                ImGui.BeginDisabled(!hasUnsavedFiles);
                if (ImGui.MenuItem("Save modified files")) {
                    workspace.SaveModifiedFiles();
                }
                if (!hasUnsavedFiles && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetItemTooltip("No files have been modified yet.");
                if (ImGui.MenuItem("Revert modified files")) {
                    foreach (var file in workspace.ResourceManager.GetModifiedResourceFiles()) {
                        file.Revert(workspace);
                    }
                }
                if (!hasUnsavedFiles && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetItemTooltip("No files have been modified yet.");
                ImGui.EndDisabled();
                if (ImGui.BeginMenu("Open files")) {
                    var files = workspace.ResourceManager.GetOpenFiles();
                    if (!files.Any()) {
                        ImGui.MenuItem("No files open", false);
                    } else {
                        if (ImGui.Button("Close all")) {
                            if (HasUnsavedChanges) {
                                if (!HasSubwindow<SaveFileConfirmation>(out _)) {
                                    AddSubwindow(new SaveFileConfirmation(
                                        "Unsaved changes",
                                        $"Some files have unsaved changes.\nAre you sure you wish to close the window?",
                                        workspace.ResourceManager.GetModifiedResourceFiles(),
                                        this,
                                        () => workspace.ResourceManager.CloseAllFiles()
                                    ));
                                }
                            } else {
                                workspace.ResourceManager.CloseAllFiles();
                            }
                            ImGui.CloseCurrentPopup();
                        }
                        foreach (var file in files) {
                            if (file.Modified) {
                                ImGui.Bullet();
                            }
                            ImGui.PushID(file.Filepath);
                            if (file.Modified) {
                                if (ImGui.Button("Save")) file.Save(workspace);
                                ImGui.SameLine();
                            }
                            if (!file.References.Any(r => !r.CanClose)) {
                                if (ImGui.Button("Close")) {
                                    if (file.Modified) {
                                        AddSubwindow(new SaveFileConfirmation(
                                            "Unsaved changes",
                                            $"The file {file.Filepath} has unsaved changes.\nAre you sure you wish to close it?",
                                            [file],
                                            this,
                                            () => workspace.ResourceManager.CloseFile(file)
                                        ));
                                    } else {
                                        workspace.ResourceManager.CloseFile(file);
                                    }
                                    ImGui.PopID();
                                    break;
                                }
                                ImGui.SameLine();
                            }
                            if (ImGui.MenuItem($"{file.Filepath} ({file.References.Count})")) {
                                var editor = file.References.OfType<IFocusableFileHandleReferenceHolder>().FirstOrDefault();
                                if (editor?.CanFocus == true) {
                                    editor.Focus();
                                } else {
                                    AddFileEditor(file);
                                }
                            }
                            ImGui.PopID();
                        }
                    }
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("Recent files")) {
                    var recents = AppConfig.Instance.RecentFiles.Get();
                    if (recents == null || recents.Length == 0) {
                        ImGui.MenuItem("No recent files", false);
                    } else {
                        foreach (var file in recents) {
                            if (ImGui.MenuItem(file)) {
                                this.OnFileDrop([file], new Vector2D<int>());
                            }
                        }
                    }
                    ImGui.EndMenu();
                }
                ImGui.Separator();
                ImGui.BeginDisabled(!UndoRedo.CanUndo(this));
                if (ImGui.MenuItem("Undo")) UndoRedo.Undo(this);
                ImGui.EndDisabled();
                ImGui.BeginDisabled(!UndoRedo.CanRedo(this));
                if (ImGui.MenuItem("Redo")) UndoRedo.Redo(this);
                ImGui.EndDisabled();
                ImGui.Separator();
                if (ImGui.MenuItem("Apply patches")) {
                    ApplyContentPatches(null);
                }
                if (ImGui.MenuItem("Patch to ...")) {
                    PlatformUtils.ShowFolderDialog((path) => ApplyContentPatches(path), workspace.Env.Config.GamePath);
                }
                if (ImGui.MenuItem("Revert patches")) {
                    RevertContentPatches();
                }
                if (ImGui.MenuItem("Edit load orders ...")) {
                    AddUniqueSubwindow(new LoadOrderUI(workspace.BundleManager));
                }
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) {
                // Environment.Exit(0);
                _window.Close();
            }
            ImGui.EndMenu();
        }

        if (SceneManager.RootMasterScenes.Any() && ImGui.BeginMenu("Scenes")) {
            foreach (var scene in SceneManager.RootMasterScenes) {
                if (scene.IsActive) {
                    ImGui.Bullet();
                    if (ImGui.MenuItem(scene.Name)) {
                        SceneManager.ChangeMasterScene(null);
                        sceneBehavior.Scene = null!;
                    }
                } else {
                    if (ImGui.MenuItem(scene.Name)) {
                        SceneManager.ChangeMasterScene(scene);
                        sceneBehavior.Scene = scene;
                    }
                }
            }
            ImGui.EndMenu();
        }

        ShowGameSelectionMenu();
        if (ImGui.BeginMenu("Windows")) {
            if (ImGui.MenuItem("Open new window")) {
                UI.OpenWindow(workspace);
            }

            if (workspace != null) {
                if (workspace.Config.Entities.Any()) {
                    if (ImGui.MenuItem("Entities")) {
                        AddSubwindow(new AppContentEditorWindow(workspace));
                    }
                }

                if (ImGui.MenuItem("PAK file browser")) {
                    AddSubwindow(new PakBrowser(workspace.Env, null));
                }
                if (ImGui.MenuItem("Data Search")) {
                    AddUniqueSubwindow(new RszDataFinder());
                }
            }
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Tools")) {
            if (ImGui.MenuItem("Settings")) {
                if (HasSubwindow<SettingsWindowHandler>(out var settings)) {
                    CloseSubwindow(settings);
                } else {
                    AddSubwindow(new SettingsWindowHandler());
                }
            }
            if (ImGui.MenuItem("Theme editor")) {
                AddUniqueSubwindow(new ThemeEditor());
            }
            ImGui.Separator();

            if (ImGui.MenuItem("Rebuild RSZ patch data")) {
                if (workspace != null && !runningRszInference) {
                    runningRszInference = true;
                    Logger.Info("Starting RSZ data scan...");
                    Task.Run(() => {
                        try {
                            var sw = Stopwatch.StartNew();
                            var tools = new ReeLib.Tools.ResourceTools(workspace.Env);
                            tools.BaseOutputPath = Path.Combine(Directory.GetCurrentDirectory(), "rsz-output");
                            tools.InferRszData();
                            var end = sw.Elapsed;
                            InvokeFromUIThread(() => Logger.Info("RSZ data scan finished in " + end.TotalSeconds + "s."));
                            InvokeFromUIThread(() => Logger.Info($"Data stored in {tools.BaseOutputPath}"));
                        } finally {
                            runningRszInference = false;
                        }
                    });
                } else {
                    Logger.Info("Scan already in progress or workspace missing");
                }
            }

            if (ImGui.MenuItem("Rebuild EFX data")) {
                if (workspace != null && !runningRszInference) {
                    runningRszInference = true;
                    var outDir = Path.Combine(Directory.GetCurrentDirectory(), "efx_structs");
                    foreach (var game in AppConfig.Instance.ConfiguredGames) {
                        var gameId = new GameIdentifier(game);
                        var efxOutput = Path.Combine(outDir, game, "efx_structs.json");
                        EfxTools.GenerateEFXStructsJson(gameId.ToEfxVersion(), efxOutput);
                    }
                } else {
                    Logger.Info("Scan already in progress or workspace missing");
                }
            }

            if (ImGui.MenuItem("IMGUI test window")) {
                AddUniqueSubwindow(new ImguiTestWindow());
            }
            if (ImGui.MenuItem("File testing")) {
                AddUniqueSubwindow(new FileTesterWindow());
            }
            ImGui.EndMenu();
        }

        if (ImGui.MenuItem("Support development (Ko-Fi)")) {
            Process.Start(new ProcessStartInfo("https://ko-fi.com/shadowcookie") { UseShellExecute = true });
        }

        if (AppConfig.IsOutdatedVersion && ImGui.MenuItem($"New version ({AppConfig.Instance.LatestVersion.Get()}) available!")) {
            Process.Start(new ProcessStartInfo("https://github.com/kagenocookie/REE-Content-Editor/releases/latest") { UseShellExecute = true });
        }

        ImGui.EndMainMenuBar();
    }

    protected override void OnIMGUI()
    {
        ShowMainMenuBar();
        if (isDragging) ImGui.BeginDisabled();
        BeginDockableBackground(new Vector2(0, ImGui.CalcTextSize("a").Y + ImGui.GetStyle().FramePadding.Y * 2));
        if (Overlays != null) {
            Overlays.ShowHelp = !_disableIntroGuide && !subwindows.Any(s => !IsDefaultWindow(s)) && !SceneManager.HasActiveMasterScene;
        }
        DrawImguiWindows();
        EndDockableBackground();
        if (isDragging) ImGui.EndDisabled();
    }

    protected void ApplyContentPatches(string? outputPath)
    {
        if (workspace == null) {
            Logger.Error("Select a game first!");
            return;
        }

        try {
            var patchWorkspace = new ContentWorkspace(workspace.Env, workspace.Config, workspace.BundleManager);
            var patcher = new Patcher(patchWorkspace);
            patcher.OutputFolder = outputPath;
            patcher.Execute();
        } catch (Exception e) {
            Logger.Error(e, "Failed to execute patcher");
        }
    }

    protected void RevertContentPatches()
    {
        if (workspace == null) {
            Logger.Error("Select a game first!");
            return;
        }

        try {
            var patchWorkspace = new ContentWorkspace(workspace.Env, workspace.Config, workspace.BundleManager);
            var patcher = new Patcher(patchWorkspace);
            patcher.RevertPreviousPatch();
        } catch (Exception e) {
            Logger.Error(e, "Failed to revert patches");
        }
    }

    protected override bool RequestAllowClose()
    {
        if (HasUnsavedChanges && workspace != null) {
            if (HasSubwindow<SaveFileConfirmation>(out _)) return false;
            AddSubwindow(new SaveFileConfirmation(
                "Unsaved changes",
                $"Some files have unsaved changes.\nAre you sure you wish to close the window?",
                workspace.ResourceManager.GetModifiedResourceFiles(),
                this,
                () => {
                    workspace.ResourceManager.CloseAllFiles();
                    _window.Close();
                }
            ));
            return false;
        }
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (env != null) WorkspaceManager.Instance.Release(env);
        env = null;
        SceneManager?.Dispose();
        base.Dispose(disposing);
    }
}