using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Themes;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App;

public class FileTesterWindow : IWindowHandler
{
    public string HandlerName => "File Tester";
    public bool HasUnsavedChanges => false;
    int IWindowHandler.FixedID => -2312;

    private KnownFileFormats format;
    private string? formatFilter;
    private bool allVersions;

    private UIContext context = null!;
    private WindowData data = null!;

    private CancellationTokenSource? cancellationTokenSource;
    private bool SearchInProgress => cancellationTokenSource != null;

    private ConcurrentDictionary<GameIdentifier, (int success, ConcurrentBag<string> fails)> results = new();

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnIMGUI()
    {
        var exec = SearchInProgress;
        if (exec) ImGui.BeginDisabled();
        ImguiHelpers.FilterableCSharpEnumCombo("File type", ref format, ref formatFilter);
        ImGui.Checkbox("Try all configured games", ref allVersions);

        if (format != KnownFileFormats.Unknown) {
            if (ImGui.Button("Execute")) {
                Execute(format, allVersions);
            }
        }
        if (exec) ImGui.EndDisabled();

        if (!results.IsEmpty) {
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) {
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear results")) {
                results.Clear();
            }
            foreach (var (game, results) in results) {
                ImGui.SeparatorText($"{game.name}: {results.success}/{results.success + results.fails.Count}");
                if (!results.fails.IsEmpty) {

                    ImGui.TextColored(Colors.Warning, "List of files that failed to read:");
                    foreach (var file in results.fails) {
                        ImGui.Text($"[{game.name}]: {file}");
                        if (ImGui.IsItemClicked()) {
                            EditorWindow.CurrentWindow!.CopyToClipboard(file);
                            EditorWindow.CurrentWindow.Overlays.ShowTooltip("Copied file path!", 1f);
                        }
                    }
                }
            }
        }
    }

    internal void Execute(KnownFileFormats format, bool allVersions)
    {
        results.Clear();
        var workspace = (data.ParentWindow as IWorkspaceContainer)?.Workspace;
        if (workspace == null) {
            ImGui.TextColored(Colors.Error, "Workspace not configured");
            return;
        }
        var isLoadable = workspace.Env.GetFileExtensionsForFormat(format).Any(ext => workspace.ResourceManager.CanLoadFile("." + ext));
        if (!isLoadable) {
            Logger.Error("File format " + format + " is not supported");
            return;
        }

        cancellationTokenSource?.Cancel();
        cancellationTokenSource ??= new();
        // if (!workspace.ResourceManager.CanLoadFile()) return false;
        Task.Run(() => {
            try {
                foreach (var env in GetWorkspaces(workspace)) {
                    var rm = new ResourceManager(new PatchDataContainer("!"));
                    var exts = env.Env.GetFileExtensionsForFormat(format);
                    var success = 0;
                    var fails = new ConcurrentBag<string>();
                    results[env.Game] = (success, fails);
                    foreach (var (path, stream) in exts.SelectMany(ext => env.Env.GetFilesWithExtension(ext))) {
                        if (TryLoadFile(env, format, path, stream)) {
                            success++;
                            results[env.Game] = (success, fails);
                        } else {
                            fails.Add(path);
                        }
                    }

                    Logger.Info($"Finished {env.Game} {format} test: {success}/{success + fails.Count} files suceeded.");
                }
            } catch (Exception e) {
                Logger.Error(e, "Unexpected error during file test");
            } finally {
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            }
        });
    }

    private static bool TryLoadFile(ContentWorkspace env, KnownFileFormats format, string path, Stream stream)
    {
        if (!env.ResourceManager.CanLoadFile(path)) return false;
        try {
            var file = env.ResourceManager.CreateFileHandle(path, path, stream, allowDispose: false);
            if (file != null) {
                env.ResourceManager.CloseFile(file);
                return true;
            }
            return false;
        } catch (Exception) {
            return false;
        }
    }

    private IEnumerable<ContentWorkspace> GetWorkspaces(ContentWorkspace current)
    {
        yield return current;
        if (!allVersions) yield break;

        foreach (var other in AppConfig.Instance.ConfiguredGames) {
            if (other == current.Game) continue;

            var env = WorkspaceManager.Instance.GetWorkspace(other);
            ContentWorkspace? cw = null;
            try {
                cw = new ContentWorkspace(env, new PatchDataContainer("!"));
                Logger.Info("Starting search for game " + env.Config.Game);
                yield return cw;
            } finally {
                cw?.Dispose();
                WorkspaceManager.Instance.Release(env);
            }
        }
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    public bool RequestClose()
    {
        return false;
    }
}