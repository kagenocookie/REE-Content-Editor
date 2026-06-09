namespace ContentEditor.App.Windowing;

using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.Github;
using ContentEditor.App.ImguiHandling;
using ContentEditor.BackgroundTasks;
using ContentEditor.Core;
using ContentEditor.Reversing;
using ContentPatcher;
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
    /// Get the currently rendering window. Should never be null during the ImGui render phase, likely to be null in multithreaded contexts.
    /// </summary>
    public static EditorWindow? CurrentWindow {
        get {
            Debug.Assert(MainLoop.IsMainThread);
            return _currentWindow as EditorWindow;
        }
    }

    public ContentWorkspace Workspace => workspace!;

    protected Workspace? env;
    protected ContentWorkspace? workspace;
    public event Action? GameChanged;

    public GameIdentifier LastRequestedGame { get; private set; }

    public SceneManager SceneManager { get; }

    private static HashSet<string>? fullSupportedGames;

    private static bool runningRszInference;

    private bool IsDragging => SceneManager.RootMasterScenes.Any(ss => ss.Mouse.IsDragging);

    protected bool isBaseWindowFocused;

    private bool HasUnsavedChanges => workspace?.ResourceManager.GetModifiedResourceFiles().Any() == true;

    public IMouse LastMouse { get; private set; } = null!;
    public IKeyboard LastKeyboard { get; private set; } = null!;

    protected Vector2 viewportOffset;
    public Vector2 ViewportOffset => viewportOffset;

    private string openFileFilter = "";
    private string recentFileFilter = "";

    private bool _workspaceSetupInProgress;
    private string? _resourceSetupFailure;
    public bool IsReady => !_workspaceSetupInProgress && _resourceSetupFailure == null;
    public string? ResourceSetupFailure => _resourceSetupFailure;

    private enum GameLaunchType
    {
        Normal = 0,
        LooseFiles = 1,
        Pak = 2
    }

    internal EditorWindow(int id, ContentWorkspace? workspace = null) : base(id)
    {
        Ready += OnReady;
        this.workspace = workspace;
        env = workspace?.Env;
        SceneManager = new(this);
        SceneManager.MasterSceneChanged += SetupSceneRender;
    }

    public void SetWorkspace(GameIdentifier game, string? bundle) => SetWorkspace(game, bundle, false);
    internal void SetWorkspace(GameIdentifier game, string? bundle, bool forceReloadEnv)
    {
        LastRequestedGame = game;
        // close all subwindows since they won't necessarily have the correct data anymore
        if (env != null && (env.Config.Game != game || forceReloadEnv)) {
            if (!RequestCloseAllSubwindows(true)) return;
            WorkspaceManager.Instance.Release(env);
            env = null;
            workspace = null;
        } else if (!RequestCloseAllSubwindows(false)) {
            return;
        }

        _workspaceSetupInProgress = false;
        _resourceSetupFailure = null;
        if (workspace != null) {
            ChangeWorkspace(workspace, bundle);
            return;
        }

        if (env != null) {
            ChangeEnv(env, bundle);
            return;
        }

        _workspaceSetupInProgress = true;
        WorkspaceManager.Instance.GetWorkspaceAsync(game).ContinueWith(t => {
            if (game != LastRequestedGame) {
                return;
            }
            if (t.IsCompletedSuccessfully) {
                ChangeEnv(t.Result, bundle);
            } else {
                _resourceSetupFailure = t.Exception?.Message ?? "Unspecified error.";
            }
            _workspaceSetupInProgress = false;
        });
    }

    private void ChangeEnv(Workspace env, string? bundle)
    {
        this.env = env;

        var configPath = Path.Combine(AppConfig.Instance.ConfigBasePath, env.Config.Game.name);
        var patchConfig = this.workspace?.Config ?? new PatchDataContainer(Path.GetFullPath(configPath));

        var workspace = new ContentWorkspace(env, patchConfig, this.workspace?.BundleManager);
        ChangeWorkspace(workspace, bundle);
    }

    private void ChangeWorkspace(ContentWorkspace workspace, string? bundle)
    {
        if (!MainLoop.IsMainThread) {
            MainLoop.Instance.InvokeFromUIThread(() => ChangeWorkspace(workspace, bundle));
            return;
        }

        this.workspace = workspace;

        workspace.UI = new AppUIService(this, workspace);
        workspace.ResourceManager.SetupFileLoaders(typeof(PrefabLoader).Assembly);
        SetupTypes(workspace);
        workspace.SetBundle(bundle);
        GameChanged?.Invoke();
        SceneManager.ChangeWorkspace(workspace);
        if (bundle != null && workspace.CurrentBundle != null) {
            AppConfig.Settings.RecentBundles.AddRecent(workspace.Game, bundle);
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
            if (!IsReady) {
                AddSubwindow(new ErrorModal(Lang.Errors.FileLoad_NotReady_Title, Lang.Errors.FileLoad_NotReady_Message));
            } else {
                AddSubwindow(new ErrorModal(Lang.Errors.FileLoad_GameUnset_Title, Lang.Errors.FileLoad_GameUnset_Message));
            }
            return;
        }

        var paks = filenames.Where(f => f.EndsWith(".pak"));
        if (paks.Any()) {
            var orderedPaks = paks.Order().ToArray();
            foreach (var pak in orderedPaks) AppConfig.Settings.RecentFiles.AddRecent(Workspace.Game, pak);
            AddSubwindow(new PakBrowser(workspace, orderedPaks));
        }

        foreach (var filename in filenames) {
            if (filename.EndsWith(".pak")) {
                continue;
            }

            if (workspace.ResourceManager.TryGetOrLoadFile(filename, out var file)) {
                file.Stream.Seek(0, SeekOrigin.Begin);
                AddFileEditor(file);
            } else if (Path.IsPathRooted(filename) && !File.Exists(filename)) {
                AddSubwindow(new ErrorModal(Lang.Errors.FileLoad_FileNotFound_Title, Lang.Errors.FileLoad_FileNotFound.FormatRef(filename)));
            } else {
                var fmt = PathUtils.ParseFileFormat(filename).format;
                var canLoad = workspace.ResourceManager.CanLoadFile(filename);
                if (!canLoad) {
                    AddSubwindow(new ErrorModal(Lang.Errors.FileLoad_Unsupported_Title, Lang.Errors.FileLoad_UnsupportedFormat.FormatRef((int)fmt, filename)));
                } else if (fmt.IsRSZBasedFormat()) {
                    AddSubwindow(new ErrorModal(Lang.Errors.FileLoad_Unsupported_Title, Lang.Errors.FileLoad_UnsupportedFormatRSZ.FormatRef(filename)));
                } else {
                    AddSubwindow(new ErrorModal(Lang.Errors.FileLoad_Unsupported_Title, Lang.Errors.FileLoad_UnknownError.FormatRef(filename)));
                }
            }
        }
    }

    protected override void HandleGlobalHotkeys()
    {
        base.HandleGlobalHotkeys();

        var cfg = AppConfig.Instance;
        if (cfg.Key_Open.Get().IsPressed()) {
            // showing it immediately here makes it show up twice for some reason, defer to next frame
            MainLoop.Instance.MainWindow.InvokeFromUIThread(ShowFileOpenDialog);
        }
        if (workspace != null && cfg.Key_OpenPakBrowser.Get().IsPressed()) {
            AddSubwindow(new PakBrowser(workspace, null));
        }
        if (workspace != null && cfg.Key_OpenMacroShelf.Get().IsPressed()) {
            AddUniqueSubwindow(new LuaMacroShelf(workspace));
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
        if (!ImGui.GetIO().WantCaptureMouse) return;

        foreach (var scene in SceneManager.RootMasterScenes) {
            scene.Mouse.HandleMouseDown((ImGuiMouseButton)button, position);
        }
    }

    protected virtual void OnMouseUp(IMouse mouse, Silk.NET.Input.MouseButton button, Vector2 position)
    {
        if (button > MouseButton.Middle || button < MouseButton.Left) return;
        if (!ImGui.GetIO().WantCaptureMouse) return;
        foreach (var scene in SceneManager.RootScenes) {
            if (!scene.IsActive) continue;

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
            if (file.HandleType != FileHandleType.Embedded && file.HandleType != FileHandleType.New) {
                AppConfig.Settings.RecentFiles.AddRecent(workspace.Game, file.Filepath);
                AppConfig.Settings.GetRecentForFormat(file.Format.format)?.AddRecent(workspace.Game, file.Filepath);
            }
            AddSubwindow(handler);
        } else {
            workspace.ResourceManager.CloseFile(file);
            AddSubwindow(new ErrorModal(Lang.Errors.FileLoad_Unsupported_Title, Lang.Errors.FileLoad_NotEditable.FormatRef(file.Filepath)));
        }
    }

    private void ChangePlatform(PlatformIdentifier platform)
    {
        if (workspace == null || platform == workspace.Platform) {
            return;
        }

        workspace.Env.Config.Platform = platform;
        AppConfig.Instance.SetGamePlatform(workspace.Game, platform.id);
        // force reload/close anything that needs to know the platform
        foreach (var wnd in subwindows) {
            if (wnd.Handler is PakBrowser pakb) {
                CloseSubwindow(wnd);
            }
        }
        workspace.Env.ResetListFile();
    }

    protected void ShowGameSelectionMenu()
    {
        fullSupportedGames ??= ResourceRepository.RemoteInfo.Resources
            .Where(kv => kv.Value.IsRSZFullySupported)
            .Select(kv => kv.Key)
            .ToHashSet();

        var curGame = (env?.Config.Game ?? LastRequestedGame).name;
        if (ImGui.BeginMenu(Lang.Home.ActiveGame.Format((string.IsNullOrEmpty(curGame) ? "--" : curGame.ToUpper())))) {
            if (env != null) {
                if (ImGui.BeginMenu(Lang.Home.ActivePlatform.Format(env.Config.Platform.ToString()))) {
                    foreach (var otherPlat in PlatformIdentifier.GetAvailableDesktopPlatforms(env.Config.Game)) {
                        if (ImGui.Selectable(otherPlat.ToString(), otherPlat == env.Config.Platform)) {
                            ChangePlatform(otherPlat);
                        }
                    }
                    ImGui.SeparatorText(Lang.Home.OtherPlatforms);
                    foreach (var otherPlat in PlatformIdentifier.NonDesktop) {
                        if (ImGui.Selectable(otherPlat.ToString(), otherPlat == env.Config.Platform)) {
                            ChangePlatform(otherPlat);
                        }
                    }
                    ImGui.EndMenu();
                }
                ImGui.Separator();
            }
            var games = AppConfig.Instance.GetGamelist();
            foreach (var (game, configured) in games) {
                if (configured && fullSupportedGames.Contains(game)) {
                    if (ImGui.MenuItem(Lang.TranslateGame(game))) SetWorkspace(game, null);
                }
            }
            ImGui.Separator();
            foreach (var (game, configured) in games) {
                if (configured && !fullSupportedGames.Contains(game)) {
                    if (ImGui.MenuItem(Lang.TranslateGame(game))) SetWorkspace(game, null);
                }
            }
            ImGui.Separator();
            if (ImGui.MenuItem(Lang.Buttons.ConfigureGames)) {
                AddUniqueSubwindow(new SettingsWindowHandler());
            }
            if (workspace != null && !string.IsNullOrEmpty(workspace.Env.Config.GamePath) && ImGui.MenuItem(Lang.Buttons.Open_GameFolder)) {
                FileSystemUtils.ShowFileInExplorer(workspace.Env.Config.GamePath);
            }
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(Lang.Home.NamedBundle.Format(workspace?.CurrentBundle?.Name ?? "--"), enabled: workspace != null)) {
            if (!workspace!.BundleManager.IsLoaded) workspace.BundleManager.LoadDataBundles();
            if (ImGui.BeginMenu(Lang.Home.ActiveBundle.Format(workspace.Data.ContentBundle ?? ""))) {
                if (ImGui.MenuItem(Lang.Buttons.NewBundle)) {
                    ShowBundleManagement();
                }
                if (ImGui.MenuItem(Lang.Buttons.NewBundleFromPAK)) {
                    PlatformUtils.ShowFileDialog(pak =>
                        CreateBundleFromPakFile(pak[0]),
                        filters: FileFilters.PakFile,
                        allowMultiple: false
                    );
                }
                if (ImGui.MenuItem(Lang.Buttons.NewBundleFromLoose)) {
                    PlatformUtils.ShowFolderDialog(folder => {
                        CreateBundleFromLooseFileFolder(folder);
                    });
                }
                ImGui.Separator();
                if (workspace.CurrentBundle != null && ImGui.MenuItem(Lang.Buttons.BundleUnload)) {
                    SetWorkspace(workspace.Env.Config.Game, null);
                }
                var foundUnusedBundle = false;
                foreach (var b in workspace.BundleManager.AllBundles.OrderBy(bb => (uint)AppConfig.Settings.RecentBundles.FindPrefixedIndex(bb.Name))) {
                    if (!foundUnusedBundle && AppConfig.Settings.RecentBundles.FindPrefixedIndex(b.Name) == -1) {
                        foundUnusedBundle = true;
                        ImGui.Separator();
                    }
                    if (ImGui.MenuItem(b.Name)) {
                        SetWorkspace(workspace.Env.Config.Game, b.Name);
                    }
                }
                ImGui.EndMenu();
            }
            if (ImGui.MenuItem(Lang.Windows.BundleManager)) {
                ShowBundleManagement();
            }
            if (workspace.BundleManager.UninitializedBundleFolders.Count > 0) {
                if (ImGui.BeginMenu(Lang.Home.UninitializedBundles)) {
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
            if (workspace.CurrentBundle != null) {
                ImGui.Separator();
                if (ImGui.MenuItem(Lang.Buttons.Open_CurrentBundleFolder)) {
                    FileSystemUtils.ShowFileInExplorer(workspace.BundleManager.GetBundleFolder(workspace.CurrentBundle));
                }
                if (ImGui.MenuItem(Lang.Buttons.BundleFileRescan)) {
                    workspace.RescanFilesInBundle(workspace.CurrentBundle);
                }
                if (ImGui.MenuItem(Lang.Buttons.BundlePublish)) {
                    AddUniqueSubwindow(new ModPublisherWindow(workspace));
                }
            }
            ImGui.EndMenu();
        }
    }

    internal void ShowBundleManagement()
    {
        if (workspace == null) return;
        AddUniqueSubwindow(new BundleManagementUI(
            workspace.BundleManager,
            workspace.CurrentBundle?.Name,
            (path) => OpenFiles([path]),
            (path, diff) => AddSubwindow(new JsonViewer(diff, path)),
            folder => CreateBundleFromLooseFileFolder(folder),
            pak => CreateBundleFromPakFile(pak)
        ));
    }
    public void CreateBundleFromLooseFileFolder(string folder)
    {
        var modinfoPath = Path.Combine(folder, "modinfo.ini");
        var initialName = Path.GetFileName(folder);
        if (File.Exists(modinfoPath)) {
            var modData = File.ReadAllLines(modinfoPath);
            var nameEntry = modData.FirstOrDefault(line => line.StartsWith("name") && line.Contains('='));
            if (nameEntry != null) {
                initialName = nameEntry.Split('=')[1].Trim();
            }
        }

        AddSubwindow(new NameInputDialog(Lang.Home.BundleDialog_Title, Lang.Home.BundleDialog_Text_Loose.FormatRef(folder),
            initialName, FilenameRegex(), this, name => Workspace.InitializeUnlabelledBundle(name, folder)));
    }
    public void CreateBundleFromPakFile(string pakPath)
    {
        var reader = new PakReader();
        reader.AddFiles("modinfo.ini");
        reader.PakFilePriority = [pakPath];
        var modinfo = reader.FindFiles().FirstOrDefault();
        var initialName = Path.GetFileNameWithoutExtension(pakPath).Replace(".", "_");

        if (modinfo.stream != null) {
            var modData = new StreamReader(modinfo.stream).ReadToEnd().Split('\n');
            var nameEntry = modData.FirstOrDefault(line => line.StartsWith("name") && line.Contains('='));
            if (nameEntry != null) {
                initialName = nameEntry.Split('=')[1].Trim();
            }
        }

        AddSubwindow(new NameInputDialog(Lang.Home.BundleDialog_Title, Lang.Home.BundleDialog_Text_PAK.FormatRef(pakPath),
            initialName, FilenameRegex(), this, name => Workspace.CreateBundleFromPAK(name, pakPath)));
    }

    public void ShowLaunchGameMenu()
    {
        if (workspace == null) return;
        string? activeGamePath = AppConfig.Instance.GetGameExecutablePath(workspace.Game);
        var launchType = (GameLaunchType)AppConfig.Instance.GameLaunchType.Get();
        using (var _ = ImguiHelpers.Disabled(string.IsNullOrEmpty(activeGamePath))) {
            if (ImGui.MenuItem(Lang.Home.LaunchGame)) {
                if (!File.Exists(activeGamePath)) {
                    Logger.Error(Lang.Errors.ExeNotFound.Format(activeGamePath ?? "-"));
                } else {
                    bool isGameAlreadyRunning = false;

                    foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(activeGamePath))) {
                        if (process.MainModule?.FileName == activeGamePath) {
                            isGameAlreadyRunning = true;
                            break;
                        }
                    }

                    if (isGameAlreadyRunning) {
                        Logger.Info($"{Lang.TranslateGame(workspace.Game.name)} is already running.");
                    } else {
                        try {
                            string launchSuffix = "";
                            switch (launchType) {
                                case GameLaunchType.LooseFiles:
                                    ApplyContentPatches(null);
                                    launchSuffix = " with patched loose files";
                                    break;
                                case GameLaunchType.Pak:
                                    ApplyContentPatches("pak");
                                    launchSuffix = " with patched pak files";
                                    break;
                            }
                            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                                Process.Start(activeGamePath);
                            } else {
                                var vdfPath = Directory.EnumerateFiles(Path.GetDirectoryName(activeGamePath)!, "*.vdf").FirstOrDefault();
                                var found = false;
                                if (File.Exists(vdfPath)) {
                                    var runLine = File.ReadAllLines(vdfPath).FirstOrDefault(l => l.Contains("HasRunKey"));
                                    var match = new Regex(@"Apps\\\\(\d+)").Match(runLine ?? "");
                                    if (match.Success && int.TryParse(match.Groups[1].ValueSpan, out var appId)) {
                                        found = true;
                                        Process.Start(new ProcessStartInfo() {
                                            FileName = $"steam://rungameid/{appId}",
                                            UseShellExecute = true,
                                        });
                                    }
                                }
                                if (!found) {
                                    Logger.Error("Game can't be auto-launched from the current platform");
                                }
                            }
                            Logger.Debug($"{Lang.TranslateGame(workspace.Game.name)} launched{launchSuffix}.");
                        } catch (Exception e) {
                            Logger.Error($"Failed to launch {Lang.TranslateGame(workspace.Game.name)}: " + e.Message);
                        }
                    }
                }
            }
        }
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, ImGui.GetStyle().FramePadding.Y));
        if (ImGui.BeginMenu($"{AppIcons.SI_Small_ArrowDown}")) {
            ImGui.PopStyleVar();
            if (ImGui.MenuItem(Lang.Home.LaunchGame_LoosePatch, launchType == GameLaunchType.LooseFiles)) {
                AppConfig.Instance.GameLaunchType.Set(launchType == GameLaunchType.LooseFiles ? (int)GameLaunchType.Normal : (int)GameLaunchType.LooseFiles);
            }
            if (ImGui.MenuItem(Lang.Home.LaunchGame_PakPatch, launchType == GameLaunchType.Pak)) {
                AppConfig.Instance.GameLaunchType.Set(launchType == GameLaunchType.Pak ? (int)GameLaunchType.Normal : (int)GameLaunchType.Pak);
            }
            ImGui.Separator();
            if (ImGui.MenuItem(Lang.Home.ApplyPatches_Loose)) {
                ApplyContentPatches(null);
            }
            if (ImGui.MenuItem(Lang.Home.ApplyPatches_Pak)) {
                ApplyContentPatches("pak");
            }
            if (ImGui.MenuItem(Lang.Home.ApplyPatches_CustomPath)) {
                PlatformUtils.ShowFolderDialog((path) => ApplyContentPatches(path), workspace.Env.Config.GamePath);
            }
            if (ImGui.MenuItem(Lang.Home.ApplyPatches_Revert)) {
                RevertContentPatches();
            }
            ImGui.EndMenu();
        } else {
            ImGui.PopStyleVar();
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

    public void OpenSceneFileEditor(Scene scene)
    {
        if (File.Exists(scene.Name)) {
            OpenFiles([scene.Name]);
            return;
        }
        if (workspace == null) return;

        if (!string.IsNullOrEmpty(scene.ResourcePath)) {
            if (workspace.ResourceManager.TryResolveGameFile(scene.ResourcePath, out var file)) {
                file.Stream.Seek(0, SeekOrigin.Begin);
                AddFileEditor(file);
            } else {
                file = workspace.ResourceManager.GetOpenFiles().FirstOrDefault(ff => ff.ResourcePath == scene.ResourcePath);
                if (file != null) {
                    AddFileEditor(file);
                }
            }
        }
    }

    protected void ShowMainMenuBar()
    {
        ImGui.BeginMainMenuBar();

        bool isHomePageDrawn = HasSubwindow<HomeWindow>(out var homePageData);
        ImGui.PushStyleColor(ImGuiCol.Header, Colors.HomeButtonActive);
        var toggleHome = ImGui.MenuItem($"{AppIcons.REECE_LogoSimple}", isHomePageDrawn) || AppConfig.Instance.Key_HomePage.Get().IsPressed();
        ImGui.PopStyleColor();
        if (toggleHome) {
            if (isHomePageDrawn && homePageData != null) {
                CloseSubwindow(homePageData);
            } else {
                AddUniqueSubwindow(new HomeWindow());
            }
        }
        ImguiHelpers.VerticalSeparator();
        var hasUnsavedFiles = HasUnsavedChanges;
        if (hasUnsavedFiles) {
            ImGui.Bullet();
        }
        if (ImGui.BeginMenu(Lang.Home.Menu_File)) {
            if (ImGui.BeginMenu(Lang.Home.Menu_CreateNew, workspace != null)) {
                if (ImGui.MenuItem("Lua Script")) AddFileEditor(Workspace.ResourceManager.CreateNewFile(LuaFileLoader.Instance, "Script", "lua")!);
                ImGui.EndMenu();
            }
            ImGui.Separator();
            if (ImGui.MenuItem(Lang.Home.Menu_Open, false, workspace != null)) {
                ShowFileOpenDialog();
            }
            if (workspace != null) {
                ImGui.BeginDisabled(!hasUnsavedFiles);
                if (ImGui.MenuItem(Lang.Home.Menu_SaveAll)) {
                    workspace.SaveModifiedFiles();
                }
                if (!hasUnsavedFiles && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetItemTooltip(Lang.Home.Menu_TooltipNoModifiedFiles);
                if (ImGui.MenuItem(Lang.Home.Menu_RevertAll)) {
                    foreach (var file in workspace.ResourceManager.GetModifiedResourceFiles()) {
                        file.Revert(workspace);
                    }
                }
                if (!hasUnsavedFiles && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetItemTooltip(Lang.Home.Menu_TooltipNoModifiedFiles);
                ImGui.EndDisabled();
                var menuRight = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X;
                var maxWidth = menuRight + 600 >= Size.X ? Size.X - 48 * UI.UIScale : Size.X - menuRight - ImGui.GetStyle().WindowPadding.X * 3 - 80 * UI.UIScale;
                if (ImGui.BeginMenu(Lang.Home.Menu_OpenedFiles)) {
                    var files = workspace.ResourceManager.GetOpenFiles();
                    if (!files.Any()) {
                        ImGui.MenuItem(Lang.Home.Menu_NoFilesOpen, false);
                    } else {
                        if (ImGui.Button(Lang.Home.Menu_CloseAll)) {
                            if (HasUnsavedChanges) {
                                if (!HasSubwindow<SaveFileConfirmation>(out _)) {
                                    AddSubwindow(new SaveFileConfirmation(
                                        Lang.General.UnsavedChanges,
                                        Lang.General.UnsavedChangesText,
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
                        ImGui.InputTextWithHint("Filter", $"{AppIcons.Search}", ref openFileFilter, 128);
                        foreach (var file in files) {
                            if (!string.IsNullOrEmpty(openFileFilter) && !file.Filepath.Contains(openFileFilter, StringComparison.InvariantCultureIgnoreCase)) {
                                continue;
                            }

                            if (file.Modified) {
                                ImGui.Bullet();
                            }
                            ImGui.PushID(file.Filepath);
                            if (file.Modified) {
                                if (ImGui.Button(Lang.Buttons.Save)) file.Save(workspace);
                                ImGui.SameLine();
                            }
                            if (!file.References.Any(r => !r.CanClose)) {
                                if (ImGui.Button(Lang.Buttons.Close)) {
                                    if (file.Modified) {
                                        AddSubwindow(new SaveFileConfirmation(
                                            Lang.General.UnsavedChanges,
                                            Lang.General.UnsavedChangesText_SingleFile.FormatRef(file.Filepath),
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
                            var itemstr = file.Filepath;
                            var textsize = ImGui.CalcTextSize(itemstr).X;
                            if (textsize > maxWidth - 60 * UI.UIScale) {
                                itemstr = ImguiHelpers.ElideFilepathString(itemstr, maxWidth - 60 * UI.UIScale);
                            }
                            if (ImGui.MenuItem($"{itemstr} ({file.References.Count})")) {
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
                if (ImGui.BeginMenu(Lang.Home.Menu_RecentFiles)) {
                    var recents = AppConfig.Settings.RecentFiles;
                    if (recents == null || recents.Count == 0) {
                        ImGui.MenuItem(Lang.Home.RecentFiles_None, false);
                    } else {
                        ImGui.InputTextWithHint(Lang.General.FilterInput, $"{AppIcons.Search}", ref recentFileFilter, 128);
                        ImGui.SameLine();
                        if (ImGui.Button("Clear recent files")) {
                            recents.Clear();
                        }
                        int i = 0;
                        foreach (var file in recents) {
                            if (!string.IsNullOrEmpty(recentFileFilter) && !file.Contains(recentFileFilter, StringComparison.InvariantCultureIgnoreCase)) {
                                continue;
                            }
                            ImGui.PushID(i++);

                            var textsize = ImGui.CalcTextSize(file).X;
                            var itemstr = file;
                            if (textsize > maxWidth) {
                                itemstr = ImguiHelpers.ElideFilepathString(file, maxWidth);
                            }
                            if (ImGui.MenuItem(itemstr)) {
                                ImGui.PopID();
                                var fileToOpen = file.GetStringAfterDelimiter('|').ToString();
                                this.OnFileDrop([fileToOpen], default);
                                break;
                            }
                            if (Path.IsPathRooted(file.GetStringAfterDelimiter('|')) && ImGui.BeginPopupContextItem()) {
                                if (ImGui.Selectable(Lang.Buttons.Open_ContainingFolder)) {
                                    FileSystemUtils.ShowFileInExplorer(Path.GetDirectoryName(file.GetStringAfterDelimiter('|').ToString()));
                                }
                                ImGui.EndPopup();
                            }
                            if (textsize > maxWidth) ImguiHelpers.Tooltip(file);
                            ImGui.PopID();
                        }
                    }
                    ImGui.EndMenu();
                }
            }
            ImGui.Separator();
            if (ImGui.MenuItem(Lang.Buttons.Exit)) {
                _window.Close();
            }
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(Lang.Home.Menu_Edit)) {
            ImGui.BeginDisabled(!UndoRedo.CanUndo(this));
            if (ImGui.MenuItem(Lang.Buttons.Undo)) UndoRedo.Undo(this);
            ImGui.EndDisabled();

            ImGui.BeginDisabled(!UndoRedo.CanRedo(this));
            if (ImGui.MenuItem(Lang.Buttons.Redo)) UndoRedo.Redo(this);
            ImGui.EndDisabled();

            ImGui.Separator();
            if (ImGui.MenuItem(Lang.Windows.Settings)) {
                AddUniqueSubwindow(new SettingsWindowHandler());
            }
            if (ImGui.MenuItem(Lang.Windows.ThemeEditor)) {
                AddUniqueSubwindow(new ThemeEditor());
            }
            ImGui.Separator();
            if (workspace != null) {
                if (ImGui.MenuItem(Lang.Windows.RetargetDesigner)) {
                    AddUniqueSubwindow(new RetargetDesigner());
                }
                ImGui.Separator();
            }
            if (workspace != null && ImGui.MenuItem(Lang.Buttons.CheckForDataUpdate)) {
                if (RequestCloseAllSubwindows(true)) {
                    ResourceRepository.ResetCache(workspace.Game);
                    ResourceRepository.Initialize(true);
                    SetWorkspace(workspace.Game, workspace.CurrentBundle?.Name, true);
                }
            }
            if (workspace != null && ImGui.BeginMenu(Lang.Home.Menu_DataGeneration)) {
                if (ImGui.MenuItem("Rebuild RSZ patch data")) {
                    if (!runningRszInference) {
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
                        Logger.Info("Scan already in progress or workspace unset");
                    }
                }

                if (ImGui.MenuItem("Rebuild EFX data")) {
                    if (!runningRszInference) {
                        runningRszInference = true;
                        var outDir = Path.Combine(Directory.GetCurrentDirectory(), "efx_structs");
                        foreach (var game in AppConfig.Instance.ConfiguredGames) {
                            var gameId = new GameIdentifier(game);
                            var efxOutput = Path.Combine(outDir, game, "efx_structs.json");
                            EfxTools.GenerateEFXStructsJson(gameId.ToEfxVersion(), efxOutput);
                        }
                    } else {
                        Logger.Info("Scan already in progress or workspace unset");
                    }
                }

                if (ImGui.MenuItem("Generate file extension cache")) {
                    try {
                        var games = AppConfig.Instance.ConfiguredGames.Select(c => new GameIdentifier(c))
                            .Concat(Enum.GetValues<GameName>().Select(n => new GameIdentifier(n.ToString())))
                            .Distinct()
                            .OrderBy(s => s.name).ToList();
                        FileExtensionTools.ExtractAllFileExtensionCacheData(games, game => {
                            return AppConfig.Instance.GetGameFilelist(game);
                        });
                    } catch (Exception e) {
                        Logger.Error(e.Message);
                    }
                }

                if (ImGui.MenuItem("Generate list file")) {
                    AddSubwindow(new ListFileGeneratorTaskWindow());
                }

                if (ImGui.MenuItem("Generate bookmarks from entities")) {
                    var list = PrefabLister.GenerateFileSets(workspace);
                    if (list != null) {
                        Logger.Info(JsonSerializer.Serialize(list, JsonConfig.configJsonOptions));
                    }
                }
                ImGui.EndMenu();
            }

            if (ImGui.MenuItem("IMGUI Test Window")) {
                AddUniqueSubwindow(new ImguiTestWindow());
            }
            if (ImGui.MenuItem("File testing")) {
                AddUniqueSubwindow(new FileTesterWindow());
            }
            if (ImGui.MenuItem("Icon List")) {
                AddUniqueSubwindow(new IconListWindow());
            }
            if (ImGui.MenuItem("Dump translations")) {
                var json = Lang.GetTranslationsJson();
                PlatformUtils.ShowSaveFileDialog(path => {
                    var bytes = VYaml.Serialization.YamlSerializer.Serialize(json);
                    File.WriteAllBytes(path, bytes.Span);
                }, Path.Combine(AppContext.BaseDirectory, "i18n", Lang.CurrentLanguage.ToString()+".lang.yaml"), FileFilters.LangYamlFile);
            }
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(Lang.Home.Menu_Windows)) {
            if (ImGui.MenuItem(Lang.Buttons.NewWorkspace)) {
                UI.OpenWindow(workspace);
            }
            ImGui.Separator();
            if (workspace != null) {
                if (ImGui.MenuItem(Lang.Windows.PakBrowser)) {
                    AddSubwindow(new PakBrowser(workspace, null));
                }
                if (ImGui.MenuItem(Lang.Windows.BundleManager)) {
                    ShowBundleManagement();
                }
                if (ImGui.MenuItem(Lang.Windows.FileSearch)) {
                    AddSubwindow(new FileSearchWindow());
                }
                if (ImGui.MenuItem(Lang.Windows.TexturePacker)) {
                    AddSubwindow(new TextureChannelPacker()).Size = new Vector2(1280, 800);
                }
                if (ImGui.MenuItem(Lang.Windows.BatchConvert)) {
                    AddSubwindow(new FileConverter()).Size = new Vector2(1280, 800);
                }
                if (workspace.Config.Entities.Any()) {
                    if (ImGui.MenuItem(Lang.Windows.Entities)) {
                        AddSubwindow(new AppContentEditorWindow(workspace));
                    }
                }
                if (ImGui.MenuItem(Lang.Windows.MacroShelf)) {
                    AddUniqueSubwindow(new LuaMacroShelf(workspace));
                }
            }
            ImGui.EndMenu();
        }
        ImguiHelpers.VerticalSeparator();

        ShowGameSelectionMenu();

        if (ImGui.BeginMenu(Lang.Home.Menu_Scenes, enabled: workspace != null)) {
            if (ImGui.Selectable($"{AppIcons.SI_GenericAdd} New scene")) {
                var file = Workspace.ResourceManager.CreateNewFile(KnownFileFormats.Scene);
                if (file != null) {
                    var rawScene = file.GetCustomContent<RawScene>();
                    var root = rawScene?.GetSharedInstance(Workspace.Env);
                    var scene = SceneManager.CreateScene(file, true, null, root);
                    EditorWindow.CurrentWindow?.AddFileEditor(file);
                    SceneManager.ChangeMasterScene(scene);
                }
            }
            if (SceneManager.RootMasterScenes.Any()) ImGui.Separator();
            foreach (var scene in SceneManager.RootMasterScenes) {
                // ImGui.Bullet(); TODO scene.Modified
                if (scene.IsActive) {
                    ImGui.PushStyleColor(ImGuiCol.Text, Colors.TextActive);
                    if (ImGui.MenuItem(scene.Name)) {
                        SceneManager.ChangeMasterScene(null);
                    }
                    ImGui.PopStyleColor();
                } else {
                    if (ImGui.MenuItem(scene.Name)) {
                        SceneManager.ChangeMasterScene(scene);
                    }
                }
            }
            ImGui.EndMenu();
        }

        ShowLaunchGameMenu();

        ImguiHelpers.VerticalSeparator();

        if (ImGui.MenuItem(Lang.Home.SupportDevelopment)) {
            FileSystemUtils.OpenURL("https://ko-fi.com/shadowcookie");
        }

        if (AppConfig.IsOutdatedVersion && AppConfig.Instance.EnableUpdateCheck &&
            ImGui.MenuItem(AppConfig.IsDebugBuild ? Lang.Home.NewVersion_Unspecific : Lang.Home.NewVersion_Specific.Format(AppConfig.Settings.Changelogs.LatestReleaseVersion ?? ""))) {
            FileSystemUtils.OpenURL(GithubApi.MainRepositoryUrl);
        }

        ImGui.EndMainMenuBar();
    }

    private void ShowFileOpenDialog()
    {
        PlatformUtils.ShowFileDialog((files) => {
            MainLoop.Instance.MainWindow.InvokeFromUIThread(() => {
                Logger.Info(string.Join("\n", files));
                OpenFiles(files);
            });
        });
    }

    private void SetupSceneRender(Scene? scene)
    {
        if (scene == null) {
            return;
        }

        scene.Controller.Keyboard = _inputContext.Keyboards[0];
        scene.Controller.MoveSpeed = AppConfig.Settings.SceneView.MoveSpeed;
        scene.AddWidget<SceneVisibilitySettings>();
        var data = AddUniqueSubwindow(new SceneView(Workspace, scene));
        data.Position = new Vector2(0, viewportOffset.Y);
        data.Size = new Vector2(Size.X, Size.Y - viewportOffset.Y);
    }

    protected override void OnIMGUI()
    {
        ShowMainMenuBar();
        var dragging = IsDragging;
        if (dragging) ImGui.BeginDisabled();

        viewportOffset = new Vector2(0, ImGui.CalcTextSize("a").Y + ImGui.GetStyle().FramePadding.Y * 2);
        BeginDockableBackground(viewportOffset);
        if (Overlays != null) {
            Overlays.ShowHelp = !_disableIntroGuide && !subwindows.Any(s => !IsDefaultWindow(s, false)) && !SceneManager.HasActiveMasterScene;
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
                    ?? (Workspace.Env.RequiresSubPaksForTextures ? PakUtils.GetNextSubPakFilepath(Workspace.Env.Config.GamePath) : PakUtils.GetNextPakFilepath(Workspace.Env.Config.GamePath));
            } else {
                patcher.OutputFilepath = outputPath;
            }
            patcher.IsPublishingMod = singleBundle != null;
            patcher.StoreGDeflateTexturesAsSubPak = AppConfig.Instance.UseSubPakForLooseTextures;
            return patcher.Execute(singleBundle == null);
        } catch (Exception e) {
            Logger.Error(e, Lang.Errors.PatchFailed);
            return false;
        }
    }

    public void RevertContentPatches()
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
            Logger.Error(e, Lang.Errors.PatchRevertFailed);
        }
    }

    protected override bool RequestAllowClose()
    {
        if (HasUnsavedChanges && workspace != null) {
            if (HasSubwindow<SaveFileConfirmation>(out _)) return false;
            AddSubwindow(new SaveFileConfirmation(
                Lang.General.UnsavedChanges,
                Lang.General.UnsavedChangesText,
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

    [System.Text.RegularExpressions.GeneratedRegex("^[ a-zA-Z0-9_()-]+$")]
    public static partial System.Text.RegularExpressions.Regex FilenameRegex();
}
