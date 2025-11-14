namespace ContentEditor.App.Windowing;

using System;
using System.Diagnostics;
using System.Numerics;
using ContentEditor.App.FileLoaders;
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
using Silk.NET.Windowing;

public partial class EditorWindow : WindowBase, IWorkspaceContainer
{
    protected readonly List<IWindowHandler> windowHandlers = new();

    /// <summary>
    /// Get the current IMGUI-rendering window. Should be null outside of the imgui callback
    /// </summary>
    public static EditorWindow? CurrentImguiWindow { get; protected set; }

    /// <summary>
    /// Get the currently rendering window. Should never be null during the ImGui render phase, likely to be null in multithreaded contexts.
    /// </summary>
    public static EditorWindow? CurrentWindow => _currentWindow as EditorWindow;

    public ContentWorkspace Workspace => workspace!;

    protected Workspace? env;
    protected ContentWorkspace? workspace;
    public event Action? GameChanged;

    public SceneManager SceneManager { get; }

    private static HashSet<string>? fullSupportedGames;

    private static bool runningRszInference;

    private bool IsDragging => SceneManager.RootMasterScenes.Any(ss => ss.Mouse.IsDragging);

    protected bool isBaseWindowFocused;

    private bool HasUnsavedChanges => workspace?.ResourceManager.GetModifiedResourceFiles().Any() == true;

    public IMouse LastMouse { get; private set; } = null!;
    public IKeyboard LastKeyboard { get; private set; } = null!;

    protected Vector2 viewportOffset;

    internal EditorWindow(int id, ContentWorkspace? workspace = null) : base(id)
    {
        Ready += OnReady;
        this.workspace = workspace;
        env = workspace?.Env;
        SceneManager = new(this);
    }

    public void SetWorkspace(GameIdentifier game, string? bundle) => SetWorkspace(game, bundle, false);
    private void SetWorkspace(GameIdentifier game, string? bundle, bool forceReloadEnv)
    {
        // close all subwindows since they won't necessarily have the correct data anymore
        if (!RequestCloseAllSubwindows()) return;
        if (env != null && (env.Config.Game != game || forceReloadEnv)) {
            WorkspaceManager.Instance.Release(env);
            env = null;
            workspace = null;
        }
        env ??= WorkspaceManager.Instance.GetWorkspace(game);

        var configPath = Path.Combine(AppConfig.Instance.ConfigBasePath, game.name);
        var patchConfig = workspace?.Config ?? new PatchDataContainer(Path.GetFullPath(configPath));

        workspace = new ContentWorkspace(env, patchConfig, workspace?.BundleManager);
        workspace.ResourceManager.SetupFileLoaders(typeof(PrefabLoader).Assembly);
        SetupTypes(workspace);
        workspace.SetBundle(bundle);
        GameChanged?.Invoke();
        SceneManager.ChangeWorkspace(workspace);
        if (bundle != null && workspace.CurrentBundle != null) {
            AppConfig.Instance.AddRecentBundle(bundle);
        }
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
        if (ImGui.IsKeyPressed(ImGuiKey.F12)) {
            if (_window.WindowState != WindowState.Fullscreen) {
                _window.WindowState = WindowState.Fullscreen;
            } else {
                _window.WindowState = WindowState.Maximized;
                _window.WindowState = WindowState.Normal;
                _window.Position = new Vector2D<int>(_window.Position.X, Math.Max(_window.Position.Y, 25));
            }
        }
        LastKeyboard = _inputContext.Keyboards[0];
        if (!ImGui.GetIO().WantCaptureMouse) {
            var wheel = ImGui.GetIO().MouseWheel;
            foreach (var scene in SceneManager.RootMasterScenes) {
                if (!scene.IsActive) continue;

                scene.Mouse.MouseWheelDelta = new Vector2(0, wheel);
            }
        }
        SceneManager.Update(deltaTime);
    }

    protected override void Render(float deltaTime)
    {
        SceneManager.Render(deltaTime);
        DebugUI.Render();
    }

    private void OnReady()
    {
        var console = AddSubwindow(new ConsoleWindow());
        console.Size = new Vector2(Size.X, 200);
        console.Position = new Vector2(0, Size.Y - 200);
        if (AppConfig.Instance.IsFirstTime) {
            var data = AddSubwindow(new FirstTimeSetupHelper());
            data.Size = new Vector2(Math.Min(800, Size.X - 60), Math.Min(400, Size.X - 60));
            data.Position = new Vector2((Size.X - data.Size.X) / 2, 50);
            AppConfig.Instance.IsFirstTime.Set(false);
        }
        _window.Move += OnResize;
        _window.FramebufferResize += OnResize;
    }

    private void OnResize(Vector2D<int> newVec)
    {
        if (this != MainLoop.Instance.MainWindow || _window.WindowState == WindowState.Minimized) return;

        AppConfig.Instance.WindowRect.Set(new Vector4(_window.Position.X, _window.Position.Y, _window.Size.X, _window.Size.Y));
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
                AddSubwindow(new PakBrowser(workspace, filename));
                continue;
            }

            if (workspace.ResourceManager.TryGetOrLoadFile(filename, out var file)) {
                file.Stream.Seek(0, SeekOrigin.Begin);
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
            LastMouse = m;
            if (ImGui.GetIO().WantCaptureMouse) return;
            OnMouseMove(m, vec);
        };
        mouse.MouseDown += (m, btn) => {
            LastMouse = m;
            isBaseWindowFocused = !ImGui.GetIO().WantCaptureMouse;
            if (!isBaseWindowFocused) {
                return;
            }
            OnMouseDown(m, btn, m.Position);
        };
        mouse.MouseUp += (m, btn) => {
            LastMouse = m;
            OnMouseUp(m, btn, m.Position);
        };
    }

    protected virtual void OnMouseDown(IMouse mouse, Silk.NET.Input.MouseButton button, Vector2 position)
    {
        if (button > MouseButton.Middle || button < MouseButton.Left) return;

        foreach (var scene in SceneManager.RootMasterScenes) {
            scene.Mouse.HandleMouseDown((ImGuiMouseButton)button, position);
        }
    }

    protected virtual void OnMouseUp(IMouse mouse, Silk.NET.Input.MouseButton button, Vector2 position)
    {
        if (button > MouseButton.Middle || button < MouseButton.Left) return;
        foreach (var scene in SceneManager.RootScenes) {
            if (!scene.IsActive) continue;

            var allowHandle = ImGui.GetIO().WantCaptureMouse == (scene.OwnRenderContext.RenderTargetTextureHandle != 0);
            if (!allowHandle) continue;

            scene.Mouse.HandleMouseUp(LastMouse, (ImGuiMouseButton)button, position);
        }
    }

    protected virtual void OnMouseMove(IMouse mouse, Vector2 pos)
    {
        foreach (var scene in SceneManager.RootScenes) {
            if (!scene.IsActive) continue;

            scene.Mouse.HandleMouseMove(LastMouse, pos);
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
                AddUniqueSubwindow(new SettingsWindowHandler()).Size = new Vector2(800, 500);
            }
            ImGui.EndMenu();
        }

        if (workspace != null) {
            if (ImGui.BeginMenu($"Workspace: {workspace.Data.Name ?? "--"}")) {
                if (!workspace.BundleManager.IsLoaded) workspace.BundleManager.LoadDataBundles();
                if (ImGui.BeginMenu($"Active bundle: {workspace.Data.ContentBundle}")) {
                    if (ImGui.MenuItem("Create new...")) {
                        ShowBundleManagement();
                    }
                    if (ImGui.MenuItem("Create from PAK file")) {
                        PlatformUtils.ShowFileDialog((pak) => {
                            var reader = new PakReader();
                            reader.AddFiles("modinfo.ini");
                            reader.PakFilePriority = [pak[0]];
                            var modinfo = reader.FindFiles().FirstOrDefault();
                            var initialName = Path.GetFileNameWithoutExtension(pak[0]).Replace(".", "_");
                            if (modinfo.stream != null) {
                                var modData = new StreamReader(modinfo.stream).ReadToEnd().Split('\n');
                                var nameEntry = modData.FirstOrDefault(line => line.StartsWith("name") && line.Contains('='));
                                if (nameEntry != null) initialName = nameEntry.Split('=')[1].Trim();
                            }
                            AddSubwindow(new NameInputDialog(
                                "Bundle creation",
                                "Select name for the bundle to be created from the PAK file:\n" + pak[0],
                                initialName,
                                FilenameRegex(),
                                this,
                                (name) => workspace.CreateBundleFromPAK(name, pak[0])
                            ));
                        }, fileExtension: "PAK file (*.pak)|*.pak", allowMultiple: false);
                    }
                    if (ImGui.MenuItem("Create from loose file mod")) {
                        PlatformUtils.ShowFolderDialog((folder) => {
                            var modinfoPath = Path.Combine(folder, "modinfo.ini");
                            var initialName = Path.GetFileName(folder);
                            if (File.Exists(modinfoPath)) {
                                var modData = File.ReadAllLines(modinfoPath);
                                var nameEntry = modData.FirstOrDefault(line => line.StartsWith("name") && line.Contains('='));
                                if (nameEntry != null) initialName = nameEntry.Split('=')[1].Trim();
                            }
                            AddSubwindow(new NameInputDialog(
                                "Bundle creation",
                                "Select name for the bundle to be created from the PAK file:\n" + folder,
                                initialName,
                                FilenameRegex(),
                                this,
                                (name) => workspace.InitializeUnlabelledBundle(name, folder)
                            ));
                        });
                    }
                    ImGui.Separator();
                    if (workspace.CurrentBundle != null && ImGui.MenuItem("Unload current bundle")) {
                        SetWorkspace(workspace.Env.Config.Game, null);
                    }
                    var foundUnusedBundle = false;
                    foreach (var b in workspace.BundleManager.AllBundles.OrderBy(bb => (uint)AppConfig.Settings.RecentBundles.IndexOf(bb.Name))) {
                        if (!foundUnusedBundle && AppConfig.Settings.RecentBundles.IndexOf(b.Name) == -1) {
                            foundUnusedBundle = true;
                            ImGui.Separator();
                        }
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
                if (!string.IsNullOrEmpty(workspace.Env.Config.GamePath) && ImGui.MenuItem("Open game folder")) {
                    FileSystemUtils.ShowFileInExplorer(workspace.Env.Config.GamePath);
                }
                if (workspace.CurrentBundle != null && ImGui.MenuItem("Publish mod ...")) {
                    AddUniqueSubwindow(new ModPublisherWindow(workspace));
                }
                ImGui.EndMenu();
            }
        }
    }

    internal void ShowBundleManagement()
    {
        if (workspace == null) return;
        AddUniqueSubwindow(new BundleManagementUI(
            workspace.BundleManager,
            workspace.CurrentBundle?.Name,
            (path) => OpenFiles([path]),
            (path, diff) => AddSubwindow(new JsonViewer(diff, path))
        ));
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
                    var recents = AppConfig.Settings.RecentFiles;
                    if (recents == null || recents.Count == 0) {
                        ImGui.MenuItem("No recent files", false);
                    } else {
                        foreach (var file in recents) {
                            if (ImGui.MenuItem(file)) {
                                this.OnFileDrop([file], new Vector2D<int>());
                                break;
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
                if (ImGui.MenuItem("Apply patches (loose file)")) {
                    ApplyContentPatches(null);
                }
                if (ImGui.MenuItem("Apply patches (PAK)")) {
                    ApplyContentPatches("pak");
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
                _window.Close();
            }
            ImGui.EndMenu();
        }

        if (SceneManager.RootMasterScenes.Any() && ImGui.BeginMenu("Scenes")) {
            foreach (var scene in SceneManager.RootMasterScenes) {
                // ImGui.Bullet(); TODO scene.Modified
                if (scene.IsActive) {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered));
                    if (ImGui.MenuItem(scene.Name)) {
                        SceneManager.ChangeMasterScene(null);
                    }
                    ImGui.PopStyleColor();
                } else {
                    if (ImGui.MenuItem(scene.Name)) {
                        SceneManager.ChangeMasterScene(scene);
                        scene.Controller.Keyboard = _inputContext.Keyboards[0];
                        scene.Controller.MoveSpeed = AppConfig.Settings.SceneView.MoveSpeed;
                        scene.AddWidget<SceneVisibilitySettings>();
                        scene.AddWidget<SceneCameraControls>();
                        var data = AddUniqueSubwindow(new SceneView(Workspace, scene));
                        data.Position = new Vector2(0, viewportOffset.Y);
                        data.Size = new Vector2(Size.X, Size.Y - viewportOffset.Y);
                    }
                }
            }
            ImGui.EndMenu();
        }

        ShowGameSelectionMenu();

        if (ImGui.BeginMenu("Windows")) {
            if (ImGui.MenuItem("Open New Workspace")) {
                UI.OpenWindow(workspace);
            }
            ImGui.Separator();
            if (workspace != null) {
                if (workspace.Config.Entities.Any()) {
                    if (ImGui.MenuItem("Entities")) {
                        AddSubwindow(new AppContentEditorWindow(workspace));
                    }
                }
                if (ImGui.MenuItem("PAK File Browser")) {
                    AddSubwindow(new PakBrowser(workspace, null));
                }
                if (ImGui.MenuItem("Data Search")) {
                    AddSubwindow(new RszDataFinder());
                }
                if (ImGui.MenuItem("Texture Channel Packer")) {
                    AddSubwindow(new TextureChannelPacker());
                }
            }
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Tools")) {
            if (ImGui.MenuItem("Settings")) {
                if (HasSubwindow<SettingsWindowHandler>(out var settings)) {
                    CloseSubwindow(settings);
                } else {
                    AddUniqueSubwindow(new SettingsWindowHandler()).Size = new Vector2(800, 500);

                }
            }
            if (ImGui.MenuItem("Theme editor")) {
                AddUniqueSubwindow(new ThemeEditor());
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Retarget Designer")) {
                AddUniqueSubwindow(new RetargetDesigner());
            }
            ImGui.Separator();
            if (workspace != null && ImGui.MenuItem("Check for updated game data cache")) {
                if (RequestCloseAllSubwindows()) {
                    ResourceRepository.ResetCache(workspace.Game);
                    ResourceRepository.Initialize(true);
                    SetWorkspace(workspace.Game, workspace.CurrentBundle?.Name, true);
                }
            }
            if (ImGui.BeginMenu("Data Deneration")) {
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
                if (ImGui.MenuItem("Generate file extension cache")) {
                    if (workspace != null) {
                        try {
                            FileExtensionTools.ExtractAllFileExtensionCacheData(AppConfig.Instance.ConfiguredGames.Select(c => new GameIdentifier(c)));
                        } catch (Exception e) {
                            Logger.Error(e.Message);
                        }
                    }
                    AddUniqueSubwindow(new FileTesterWindow());
                }
                ImGui.EndMenu();
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
        var dragging = IsDragging;
        if (dragging) ImGui.BeginDisabled();

        viewportOffset = new Vector2(0, ImGui.CalcTextSize("a").Y + ImGui.GetStyle().FramePadding.Y * 2);
        BeginDockableBackground(viewportOffset);
        if (Overlays != null) {
            Overlays.ShowHelp = !_disableIntroGuide && !subwindows.Any(s => !IsDefaultWindow(s)) && !SceneManager.HasActiveMasterScene;
        }
        DrawImguiWindows();
        EndDockableBackground();
        if (dragging) ImGui.EndDisabled();
    }

    internal bool ApplyContentPatches(string? outputPath, string? singleBundle = null)
    {
        if (workspace == null) {
            Logger.Error("Select a game first!");
            return false;
        }

        try {
            var manager = singleBundle == null ? workspace.BundleManager : workspace.BundleManager.CreateBundleSpecificManager(singleBundle);
            var patchWorkspace = new ContentWorkspace(workspace.Env, workspace.Config, manager);
            var patcher = new Patcher(patchWorkspace);
            if (outputPath == "pak") {
                patcher.OutputFilepath = patcher.FindActivePatchPak()
                    ?? PakUtils.GetNextPakFilepath(Workspace.Env.Config.GamePath);
            } else {
                patcher.OutputFilepath = outputPath;
            }
            patcher.IsPublishingMod = singleBundle != null;
            return patcher.Execute(singleBundle == null);
        } catch (Exception e) {
            Logger.Error(e, "Failed to execute patcher");
            return false;
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
            var patchPakFilepath = patcher.FindActivePatchPak();
            if (patchPakFilepath != null) {
                File.Delete(patchPakFilepath);
                Logger.Info("Deleted patch PAK: " + patchPakFilepath);
            }
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

    [System.Text.RegularExpressions.GeneratedRegex("^[ a-zA-Z0-9_-]+$")]
    private static partial System.Text.RegularExpressions.Regex FilenameRegex();
}
