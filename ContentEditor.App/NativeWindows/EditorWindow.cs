namespace ContentEditor.App.Windowing;

using System;
using System.Diagnostics;
using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using Silk.NET.Maths;

public class EditorWindow : WindowBase, IWorkspaceContainer
{
    protected readonly List<IWindowHandler> windowHandlers = new();

    /// <summary>
    /// Get the current IMGUI-rendering window. Should be null outside of the imgui callback
    /// </summary>
    public static EditorWindow? CurrentImguiWindow { get; protected set; }

    /// <summary>
    /// Get the currently rendering window.
    /// </summary>
    public static EditorWindow? CurrentWindow => _currentRenderingWindow as EditorWindow;

    public ContentWorkspace Workspace => workspace!;

    protected Workspace? env;
    protected ContentWorkspace? workspace;
    public event Action? GameChanged;

    public SceneManager SceneManager { get; } = new();

    private static HashSet<string>? fullSupportedGames;

    private static bool runningRszInference;

    internal EditorWindow(int id, ContentWorkspace? workspace = null) : base(id)
    {
        Ready += OnReady;
        this.workspace = workspace;
        env = workspace?.Env;
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
        SetupUI(workspace);
        workspace.SetBundle(bundle);
        GameChanged?.Invoke();
        SceneManager.ChangeWorkspace(env);
    }

    private static void SetupUI(ContentWorkspace workspace)
    {
        foreach (var (name, cfg) in workspace.Config.Classes) {
            if (cfg.StringFormatter != null) {
                var cls = workspace.Env.RszParser.GetRSZClass(name);
                if (cls == null) continue;

                WindowHandlerFactory.SetClassFormatter(cls, cfg.StringFormatter);
            }
        }
    }

    protected override void Update(float deltaTime)
    {
        SceneManager.Update(deltaTime);
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

    public void AddFileEditor(FileHandle file)
    {
        if (workspace == null) return;

        var handler = WindowHandlerFactory.CreateFileResourceHandler(workspace, file);
        if (handler != null) {
            AppConfig.Instance.AddRecentFile(file.Filepath);
            AddSubwindow(handler);
        } else if (TextureViewer.IsSupportedFileExtension(file.Filepath)) {
            AppConfig.Instance.AddRecentFile(file.Filepath);
            AddSubwindow(new TextureViewer(file));
        } else if (file.Resource is not DummyFileResource) {
            AppConfig.Instance.AddRecentFile(file.Filepath);
            AddSubwindow(new RawDataEditor(workspace, file));
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
            foreach (var game in Enum.GetNames<GameName>()) {
                if (fullSupportedGames.Contains(game) && AppConfig.Instance.GetGamePath(game) != null) {
                    if (ImGui.MenuItem(game)) SetWorkspace(game, null);
                }
            }
            ImGui.Separator();
            foreach (var game in Enum.GetNames<GameName>()) {
                if (game == nameof(GameName.unknown) || fullSupportedGames.Contains(game)) continue;
                if (AppConfig.Instance.GetGamePath(game) == null) continue;
                if (ImGui.MenuItem(game)) SetWorkspace(game, null);
            }
            ImGui.Separator();
            if (ImGui.MenuItem("Configure games ...")) {
                AddUniqueSubwindow(new SettingsWindowHandler());
            }
            ImGui.EndMenu();
        }

        if (workspace != null) {
            if (ImGui.BeginMenu($"Workspace: {workspace.Data.Name ?? "--"}")) {
                if (ImGui.BeginMenu($"Active bundle: {workspace.Data.ContentBundle}")) {
                    if (ImGui.MenuItem("Create new...")) {
                        AddUniqueSubwindow(new BundleManagementUI(workspace.BundleManager));
                    }
                    ImGui.Separator();
                    if (!workspace.BundleManager.IsLoaded) workspace.BundleManager.LoadDataBundles();
                    foreach (var b in workspace.BundleManager.AllBundles) {
                        if (ImGui.MenuItem(b.Name)) {
                            SetWorkspace(workspace.Env.Config.Game, b.Name);
                        }
                    }
                    ImGui.EndMenu();
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

    protected void ShowMainMenuBar()
    {

        ImGui.BeginMainMenuBar();
        var hasUnsavedFiles = workspace?.ResourceManager.GetModifiedResourceFiles().Any() == true;
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
                                            file,
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

        if (hasUnsavedFiles) {
            ImGui.Bullet();
        }
        ShowGameSelectionMenu();
        if (ImGui.BeginMenu("Windows")) {
            if (ImGui.MenuItem("Open new window")) {
                UI.OpenWindow(workspace);
            }
            if (workspace != null) {
                if (ImGui.MenuItem("PAK file browser")) {
                    AddSubwindow(new PakBrowser(workspace.Env, null));
                }
                if (ImGui.MenuItem("Data Search")) {
                    AddUniqueSubwindow(new RszDataFinder());
                }
            }
            ImGui.EndMenu();
        }

        if (workspace != null && workspace.Config.Entities.Any()) {
            if (ImGui.BeginMenu("Content Editor")) {
                if (ImGui.MenuItem("Content editor window")) {
                    AddSubwindow(new AppContentEditorWindow(workspace));
                }
                if (ImGui.MenuItem("Bundle management")) {
                    AddUniqueSubwindow(new BundleManagementUI(workspace.BundleManager));
                }
                ImGui.EndMenu();
            }
        }

        if (ImGui.BeginMenu("Tools")) {
            if (ImGui.MenuItem("Settings")) {
                if (HasSubwindow<SettingsWindowHandler>(out var settings)) {
                    CloseSubwindow(settings);
                } else {
                    AddSubwindow(new SettingsWindowHandler());
                }
            }

            if (ImGui.MenuItem("Rebuild RSZ patch data")) {
                if (workspace != null && !runningRszInference) {
                    runningRszInference = true;
                    Logger.Info("Starting RSZ data scan...");
                    Task.Run(() => {
                        try {
                            var sw = Stopwatch.StartNew();
                            var tools = new ReeLib.Tools.ResourceTools(workspace.Env);
                            tools.BaseOutputPath = Path.Combine(Directory.GetCurrentDirectory(), "rsz-output");
                            tools.Log = (msg) => InvokeFromUIThread(() => Logger.Info(msg));
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
            if (ImGui.MenuItem("IMGUI test window")) {
                AddUniqueSubwindow(new ImguiTestWindow());
            }
            ImGui.EndMenu();
        }

        if (ImGui.MenuItem("Support development (Ko-Fi)")) {
            Process.Start(new ProcessStartInfo("https://ko-fi.com/shadowcookie") { UseShellExecute = true });
        }
        ImGui.EndMainMenuBar();
    }

    protected override void OnIMGUI()
    {
        ShowMainMenuBar();
        BeginDockableBackground(new Vector2(0, ImGui.CalcTextSize("a").Y + ImGui.GetStyle().FramePadding.Y * 2));
        DrawImguiWindows();
        EndDockableBackground();
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

    protected override void Dispose(bool disposing)
    {
        if (env != null) WorkspaceManager.Instance.Release(env);
        env = null;
        base.Dispose(disposing);
    }
}