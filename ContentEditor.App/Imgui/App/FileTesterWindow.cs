using System.Collections.Concurrent;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Common;

namespace ContentEditor.App;

public class FileTesterWindow : IWindowHandler
{
    public string HandlerName => "File Tester";
    public bool HasUnsavedChanges => false;
    int IWindowHandler.FixedID => -2312;

    private KnownFileFormats format;
    private string? formatFilter;
    private bool allVersions;

    private string? hashTest;
    private uint testedHash;

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

    private readonly HashSet<GameIdentifier> hiddenGames = new();
    private string? wordlistFilepath;

    public unsafe void OnIMGUI()
    {
        if (ImGui.TreeNode("Tools")) {
            hashTest ??= "";
            if (ImGui.TreeNode("Hash calculation")) {
                ImGui.PushItemWidth(ImGui.CalcItemWidth() / 4);
                ImGui.InputText("Hash test", ref hashTest, 300);
                ImGui.SameLine();
                ImGui.Text("UTF16 hash: " + MurMur3HashUtils.GetHash(hashTest));
                if (ImGui.IsItemClicked()) EditorWindow.CurrentWindow?.CopyToClipboard(MurMur3HashUtils.GetHash(hashTest).ToString());
                ImGui.SameLine();
                ImGui.Text("Ascii hash: " + MurMur3HashUtils.GetAsciiHash(hashTest));
                if (ImGui.IsItemClicked()) EditorWindow.CurrentWindow?.CopyToClipboard(MurMur3HashUtils.GetAsciiHash(hashTest).ToString());
                ImGui.SameLine();
                ImGui.Text("UTF8 hash: " + MurMur3HashUtils.GetUTF8Hash(hashTest));
                if (ImGui.IsItemClicked()) EditorWindow.CurrentWindow?.CopyToClipboard(MurMur3HashUtils.GetUTF8Hash(hashTest).ToString());
                ImGui.PopItemWidth();
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Hash reversing")) {
                if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Will attempt to match the given UTF16 hash with a wordlist (lowercase, uppercase, capital case variants are attempted)");
                AppImguiHelpers.InputFilepath("Wordlist filepath", ref wordlistFilepath);
                var v = testedHash;
                if (ImGui.InputScalar("Tested hash", ImGuiDataType.U32, (IntPtr)(&v))) {
                    testedHash = v;
                }
                if (!string.IsNullOrEmpty(wordlistFilepath) && ImGui.Button("Find")) {
                    var words = File.ReadAllLines(wordlistFilepath);
                    // var testedHash = MurMur3HashUtils.GetHash(hashTest);
                    if (testedHash == 2180083513) {
                        Logger.Info("Requested hash is an empty string's hash!");
                    }
                    var found = false;
                    foreach (var word in words) {
                        if (word == "") continue;

                        if (MurMur3HashUtils.GetHash(word) == testedHash) {
                            Logger.Info($"Found hash match: {word} (UTF16 hash {testedHash})");
                            found = true;
                        }
                        if (MurMur3HashUtils.GetHash(word.ToUpperInvariant()) == testedHash) {
                            Logger.Info($"Found hash match: {word.ToUpperInvariant()} (UTF16 hash {testedHash})");
                            found = true;
                        }
                        if (MurMur3HashUtils.GetHash(char.ToUpper(word[0]) + word.Substring(1)) == testedHash) {
                            Logger.Info($"Found hash match: {char.ToUpper(word[0]) + word.Substring(1)} (UTF16 hash {testedHash})");
                            found = true;
                        }
                    }
                    Logger.Info(!found ? $"Hash lookup finished, no matches found." : "Hash lookup finished.");
                }
                ImGui.TreePop();
            }

            ImGui.TreePop();
        }
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
            if (cancellationTokenSource != null && ImGui.Button("Stop")) {
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear results")) {
                results.Clear();
            }
            ImGui.SameLine();
            if (ImGui.Button("Copy result summary")) {
                var str = string.Join("\n", results.OrderBy(r => r.Key.ToString()).Select(data => $"{data.Key}: {data.Value.success}/{data.Value.success + data.Value.fails.Count}"));
                EditorWindow.CurrentWindow?.CopyToClipboard(str, "Copied!");
            }
            foreach (var (game, results) in results) {
                ImGui.SeparatorText($"{game.name}: {results.success}/{results.success + results.fails.Count}");
                if (ImGui.IsItemClicked()) {
                    if (!hiddenGames.Add(game)) hiddenGames.Remove(game);
                }
                if (!hiddenGames.Contains(game) && !results.fails.IsEmpty) {
                    ImGui.TextColored(Colors.Warning, "List of files that failed to read:");
                    foreach (var file in results.fails) {
                        ImGui.Text(file);
                        if (ImGui.IsItemClicked()) {
                            EditorWindow.CurrentWindow!.CopyToClipboard(file, "Copied file path!");
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
                var token = cancellationTokenSource.Token;
                foreach (var env in GetWorkspaces(workspace)) {
                    var rm = new ResourceManager(new PatchDataContainer("!"));
                    var success = 0;
                    var fails = new ConcurrentBag<string>();
                    results[env.Game] = (success, fails);

                    foreach (var (path, stream) in GetFileList(env, format)) {
                        if (token.IsCancellationRequested) return;
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

    private static IEnumerable<(string path, MemoryStream stream)> GetFileList(ContentWorkspace env, KnownFileFormats format)
    {
        var exts = env.Env.GetFileExtensionsForFormat(format);
        if (PakUtils.ScanPakFiles(env.Env.Config.GamePath).Count == 0) {
            var extractPath = AppConfig.Instance.GetGameExtractPath(env.Game);
            if (string.IsNullOrEmpty(extractPath) || !Directory.Exists(extractPath)) {
                return [];
            }

            IEnumerable<(string path, MemoryStream stream)> Iterate() {
                foreach (var ext in exts) {
                    if (!env.Env.TryGetFileExtensionVersion(ext, out var version)) {
                        Logger.Info($"Unknown version for {format} file .{ext}");
                        continue;
                    }

                    var memstream = new MemoryStream();
                    foreach (var f in Directory.EnumerateFiles(extractPath, "*." + ext + "." + version, SearchOption.AllDirectories)) {
                        using var fs = File.OpenRead(f);
                        memstream.Seek(0, SeekOrigin.Begin);
                        memstream.SetLength(0);
                        fs.CopyTo(memstream);
                        fs.Close();
                        memstream.Seek(0, SeekOrigin.Begin);
                        yield return (Path.GetRelativePath(extractPath, f), memstream);
                    }
                }
            }

            return Iterate();
        } else {
            return exts.SelectMany(ext => env.Env.GetFilesWithExtension(ext));
        }
    }

    private static bool TryLoadFile(ContentWorkspace env, KnownFileFormats format, string path, Stream stream)
    {
        if (!env.ResourceManager.CanLoadFile(path)) return false;
        try {
            var file = env.ResourceManager.CreateFileHandle(path, path, stream, allowDispose: false, keepFileReference: false);
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