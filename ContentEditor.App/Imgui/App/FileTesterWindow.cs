using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Aimp;
using ReeLib.Bhvt;
using ReeLib.Clip;
using ReeLib.Common;
using ReeLib.Efx;
using ReeLib.Efx.Structs.Common;
using ReeLib.Mdf;
using ReeLib.Mesh;
using ReeLib.Mot;
using ReeLib.Motlist;
using ReeLib.Rcol;
using ReeLib.Tml;

namespace ContentEditor.App;

public partial class FileTesterWindow : IWindowHandler
{
    public string HandlerName => "File Tester";
    public bool HasUnsavedChanges => false;
    int IWindowHandler.FixedID => -2312;

    private KnownFileFormats format = AppConfig.Settings.Dev.LastFileTestFormat;
    private string formatFilter = "";
    private bool allVersions;
    private bool testRewrite;
    private bool smokeTest;

    private const int SmokeTestFileLimit = 25;

    private string hashGuessFormat = "It{ID}";
    private int hashGuessMaxValue = 99999;

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
    private int wordlistHashType;
    private Dictionary<uint, string>? wordlistCache;
    private ContentWorkspace? Workspace => (data.ParentWindow as IWorkspaceContainer)?.Workspace;

    private static readonly KnownFileFormats[] AllFormatValues = Enum.GetValues<KnownFileFormats>().Distinct().ToArray();
    private KnownFileFormats[] Formats = [];
    private string[] FormatLabels = [];

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

            if (ImGui.TreeNode("Hash bruteforce")) {
                if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Will attempt to match the given UTF16 hash with a wordlist (lowercase, uppercase, capital case variants are attempted)");
                if (AppImguiHelpers.InputFilepath("Wordlist filepath", ref wordlistFilepath)) {
                    wordlistCache = null;
                }
                if (ImguiHelpers.InlineRadioGroup(["UTF-16", "Ascii", "UTF-8"], [0, 1, 2], ref wordlistHashType)) {
                    wordlistCache = null;
                }
                ImguiHelpers.InputScalar<uint>("Tested hash", ImGuiDataType.U32, ref testedHash);
                if (!string.IsNullOrEmpty(wordlistFilepath) && ImGui.Button("Find")) {
                    if (testedHash == 2180083513) {
                        Logger.Info("Requested hash is an empty string's hash!");
                    } else {
                        if (wordlistCache == null) {
                            var words = File.ReadAllLines(wordlistFilepath);
                            wordlistCache = new(words.Length * 3);
                            Func<string, uint> hasher = wordlistHashType switch { 1 => MurMur3HashUtils.GetAsciiHash, 2 => MurMur3HashUtils.GetUTF8Hash, _ => MurMur3HashUtils.GetHash };
                            foreach (var word in words) {
                                if (word == "") continue;
                                var upper = word.ToUpperInvariant();
                                var capital = char.ToUpper(word[0]) + word.Substring(1);
                                wordlistCache[hasher(word)] = word;
                                wordlistCache[hasher(upper)] = upper;
                                wordlistCache[hasher(capital)] = capital;
                            }
                        }
                        if (wordlistCache.TryGetValue(testedHash, out var match)) {
                            Logger.Info($"Found hash match (UTF-16): {match}");
                        } else {
                            Logger.Info("Hash lookup finished, no matches found.");
                        }
                    }
                }
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Enum string hash reversing")) {
                ImguiHelpers.Tooltip("Attempt to find a hash based on a fixed string (intended for unnamed enum strings)");
                ImguiHelpers.InputScalar<uint>("Hash", ImGuiDataType.U32, ref testedHash);
                ImGui.InputText("String Format", ref hashGuessFormat, 100);
                ImguiHelpers.Tooltip("Use the {ID} placeholder where a 0-padded integer should be (e.g. a fixed length number like 00531)");
                ImGui.DragInt("Max integer ID", ref hashGuessMaxValue);
                ImguiHelpers.Tooltip("The highest ID number the string can have. All numbers from 0 to max will be attempted.");

                if (ImGui.Button("Execute")) {
                    var maxDigits = (int)Math.Ceiling(Math.Log10(hashGuessMaxValue));
                    var numFormatString = $"D0{maxDigits}";
                    var found = false;
                    for (int i = 0; i < hashGuessMaxValue; ++i) {
                        var str = hashGuessFormat.Replace("{ID}", i.ToString(numFormatString));
                        var hash1 = MurMur3HashUtils.GetHash(str);
                        if (hash1 == testedHash) {
                            Logger.Info($"Found hash match (UTF-16): {str} == {testedHash}");
                            found = true;
                            break;
                        }
                    }
                    if (!found) Logger.Info($"String not found for hash {testedHash}");
                }
                ImGui.TreePop();
            }

            if (ImGui.Button("Test RSZ field overrides")) {
                Logger.Info("Starting RSZ override test...");

                Task.Run(() => {
                    foreach (var env in GetWorkspaces(Workspace!, true)) {
                        Logger.Info("Loading RSZ Parser " + env.Game);
                        _ = env.Env.RszParser;
                    }
                    Logger.Info("RSZ override test finished");
                });
            }
            ImGui.TreePop();
        }
        var exec = SearchInProgress;
        if (exec) ImGui.BeginDisabled();
        if (FormatLabels.Length == 0 && Workspace != null) {
            var list = new List<(string, KnownFileFormats)>();
            foreach (var fmt in AllFormatValues) {
                var exts = Workspace.Env.GetFileExtensionsForFormat(fmt);
                if (!exts.Any()) continue;

                list.Add((string.Join(", ", exts.Select(ee => "." + ee)), fmt));
            }
            Formats = list.Select(kv => kv.Item2).ToArray();
            FormatLabels = list.Select(kv => $"{kv.Item2} ({kv.Item1})").ToArray();
        }

        if (ImguiHelpers.FilterableCombo("File type", FormatLabels, Formats, ref format, ref formatFilter)) {
            AppConfig.Settings.Dev.LastFileTestFormat = format;
            AppConfig.Settings.Save();
        }
        ImGui.Checkbox("Try all configured games", ref allVersions);
        ImGui.SameLine();
        ImGui.Checkbox("Execute read/write test", ref testRewrite);
        ImGui.SameLine();
        ImGui.Checkbox("Smoke test", ref smokeTest);

        if (format != KnownFileFormats.Unknown) {
            if (ImGui.Button("Execute")) {
                if (testRewrite) {
                    ExecuteWriteTest(format, allVersions);
                } else {
                    Execute(format, allVersions);
                }
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

    internal void ExecuteWriteTest(KnownFileFormats format, bool allVersions)
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

        var wnd = EditorWindow.CurrentWindow!;
        Task.Run(() => {
            try {
                var token = cancellationTokenSource.Token;
                var timer = Stopwatch.StartNew();
                foreach (var env in GetWorkspaces(workspace, allVersions)) {
                    if (token.IsCancellationRequested) break;

                    var success = 0;
                    var fails = new ConcurrentBag<string>();
                    results[env.Game] = (success, fails);

                    foreach (var (path, stream) in GetFileList(env, format)) {
                        if (token.IsCancellationRequested) return;
                        if (ShouldSkipFile(format, path)) continue;

                        FileHandle? file = null;
                        try {
                            file = env.ResourceManager.CreateFileHandle(path, path, stream, allowDispose: false, keepFileReference: false);
                            var diffDesc = ExecuteRewriteTest(env, format, file);
                            if (diffDesc == null) {
                                success++;
                                results[env.Game] = (success, fails);
                            } else {
                                Logger.Error("Rewrite mismatch: " + path + " - " + diffDesc);
                                fails.Add(path);
                            }
                        } catch (Exception e) {
                            Logger.Error("Read failed: " + e.Message);
                            fails.Add(path);
                        } finally {
                            if (file != null) env.ResourceManager.CloseFile(file);
                        }
                        if (smokeTest && success + fails.Count >= SmokeTestFileLimit) break;
                    }

                    Logger.Info($"Finished {env.Game} {format} test: {success}/{success + fails.Count} files suceeded.");
                }

                wnd.InvokeFromUIThread(() => Logger.Info("Test finished in: " + timer.ElapsedMilliseconds + " ms"));
            } catch (Exception e) {
                Logger.Error(e, "Unexpected error during file test");
            } finally {
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            }
        });
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
        var wnd = EditorWindow.CurrentWindow!;
        Task.Run(async () => {
            try {
                var tasks = new List<Task>();
                var token = cancellationTokenSource.Token;
                var timer = Stopwatch.StartNew();
                foreach (var env in GetWorkspaces(workspace, allVersions)) {
                    if (token.IsCancellationRequested) break;
                    while (tasks.Count >= 4) {
                        foreach (var subtask in tasks) {
                            if (subtask.IsCompleted) {
                                tasks.Remove(subtask);
                                break;
                            }
                        }
                        await Task.Delay(250);
                    }

                    tasks.Add(Task.Run(() => {
                        var success = 0;
                        var fails = new ConcurrentBag<string>();
                        results[env.Game] = (success, fails);

                        foreach (var (path, stream) in GetFileList(env, format)) {
                            if (token.IsCancellationRequested) return;
                            if (ShouldSkipFile(format, path)) continue;

                            if (TryLoadFile(env, format, path, stream)) {
                                success++;
                                results[env.Game] = (success, fails);
                            } else {
                                fails.Add(path);
                            }

                            if (smokeTest && success + fails.Count >= SmokeTestFileLimit)
                            {
                                break;
                            }
                        }

                        Logger.Info($"Finished {env.Game} {format} test: {success}/{success + fails.Count} files suceeded.");
                    }));
                }

                while (tasks.Any(t => !t.IsCompleted)) {
                    await Task.Delay(500);
                }
                wnd.InvokeFromUIThread(() => Logger.Info("Test finished in: " + timer.ElapsedMilliseconds + " ms"));
            } catch (Exception e) {
                Logger.Error(e, "Unexpected error during file test");
            } finally {
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            }
        });
    }

    private static bool ShouldSkipFile(KnownFileFormats format, string filepath)
    {
        // streaming mesh data is pure buffers and not a proper file format, no point in checking those
        // if (filepath != "") return true;
        return format == KnownFileFormats.Mesh && filepath.StartsWith("natives/stm/streaming");
    }

    internal IEnumerable<(string path, Stream file)> GetExecutableFiles(GameIdentifier game, string extension, CancellationToken token = default)
    {
        Debug.Assert(Workspace != null);

        var env = Workspace.Env;
        if (game != Workspace.Game) {
            try {
                env = WorkspaceManager.Instance.GetWorkspace(game);
            } catch (Exception) {
                Logger.Warn("Failed to get workspace for " + game);
                yield break;
            }
        }
        ContentWorkspace? cw = null;
        try {
            cw = new ContentWorkspace(env, new PatchDataContainer("!"));
            cw.ResourceManager.SetupFileLoaders(typeof(MeshLoader).Assembly);
            try {
                cw.Env.PakReader.EnableConsoleLogging = false;
            } catch {
                // ignore
            }
            var format = env.GetFileFormatFromExtension(extension);
            foreach (var (path, stream) in GetFileList(cw, [extension], format)) {
                if (token.IsCancellationRequested) yield break;
                if (ShouldSkipFile(format, path)) continue;

                yield return (path, stream);
            }
        } finally {
            if (env != Workspace.Env) {
                WorkspaceManager.Instance.Release(env);
                cw?.Dispose();
            }
        }
    }

    internal static IEnumerable<(string path, MemoryStream stream)> GetFileList(ContentWorkspace env, KnownFileFormats format)
    {
        var exts = env.Env.GetFileExtensionsForFormat(format).ToArray();
        return GetFileList(env, exts, format);
    }

    internal static IEnumerable<(string path, MemoryStream stream)> GetFileList(ContentWorkspace env, string[] exts, KnownFileFormats format)
    {
        if (!string.IsNullOrEmpty(env.Env.Config.GamePath) && PakUtils.ScanPakFiles(env.Env.Config.GamePath).Count > 0) {
            return exts.SelectMany(ext => env.Env.GetFilesWithExtension(ext));
        }

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

    private static IEnumerable<ContentWorkspace> GetWorkspaces(ContentWorkspace current, bool allVersions)
    {
        yield return current;
        if (!allVersions) yield break;

        foreach (var other in AppConfig.Instance.ConfiguredGames) {
            if (other == current.Game) continue;
            // if (other == "re7") continue;

            var env = WorkspaceManager.Instance.GetWorkspace(other);
            ContentWorkspace? cw = null;
            try {
                cw = new ContentWorkspace(env, new PatchDataContainer("!"));
                cw.ResourceManager.SetupFileLoaders(typeof(MeshLoader).Assembly);
                Logger.Info("Starting search for game " + env.Config.Game);
                cw.Env.PakReader.EnableConsoleLogging = false;
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

    private static string? ExecuteRewriteTest(ContentWorkspace env, KnownFileFormats format, FileHandle source)
    {
        switch (format) {
            case KnownFileFormats.MotionList: return VerifyRewriteEquality<MotlistFile>(source.GetFile<MotlistFile>(), env);
            case KnownFileFormats.Mesh: return VerifyRewriteEquality<MeshFile>(source.GetResource<CommonMeshResource>().NativeMesh, env);
            case KnownFileFormats.RequestSetCollider: return VerifyRewriteEquality<RcolFile>(source.GetFile<RcolFile>(), env);
            case KnownFileFormats.Clip:
            case KnownFileFormats.UserCurve:
            case KnownFileFormats.Timeline: return VerifyRewriteEquality<ClipFile>(source.GetFile<ClipFile>(), env);
            case KnownFileFormats.GUI: return VerifyRewriteEquality<GuiFile>(source.GetFile<GuiFile>(), env);
            case KnownFileFormats.CollisionMesh: return VerifyRewriteEquality<McolFile>(source.GetFile<McolFile>(), env);
            case KnownFileFormats.CompositeCollision: return VerifyRewriteEquality<CocoFile>(source.GetFile<CocoFile>(), env);
            case KnownFileFormats.AIMap: return VerifyRewriteEquality<AimpFile>(source.GetFile<AimpFile>(), env);
            case KnownFileFormats.CollisionFilter: return VerifyRewriteEquality<CfilFile>(source.GetFile<CfilFile>(), env);
            case KnownFileFormats.BehaviorTree: return VerifyRewriteEquality<BhvtFile>(source.GetFile<BhvtFile>(), env);
            case KnownFileFormats.Fsm2: return VerifyRewriteEquality<BhvtFile>(source.GetFile<BhvtFile>(), env);
            case KnownFileFormats.MotionFsm2: return VerifyRewriteEquality<Motfsm2File>(source.GetFile<Motfsm2File>(), env);
            case KnownFileFormats.TimelineFsm2: return VerifyRewriteEquality<Tmlfsm2File>(source.GetFile<Tmlfsm2File>(), env);
            case KnownFileFormats.Effect: return VerifyRewriteEquality<EfxFile>(source.GetFile<EfxFile>(), env);
            case KnownFileFormats.MeshMaterial: return VerifyRewriteEquality<MdfFile>(source.GetFile<MdfFile>(), env);
            case KnownFileFormats.MotionBank: return VerifyRewriteEquality<MotbankFile>(source.GetFile<MotbankFile>(), env);
            case KnownFileFormats.MotionCameraBank: return VerifyRewriteEquality<McamBankFile>(source.GetFile<McamBankFile>(), env);
            case KnownFileFormats.MotionCameraList: return VerifyRewriteEquality<McamlistFile>(source.GetFile<McamlistFile>(), env);
            case KnownFileFormats.MotionCamera: return VerifyRewriteEquality<MotcamFile>(source.GetFile<MotcamFile>(), env);
            case KnownFileFormats.JointMap: return VerifyRewriteEquality<JmapFile>(source.GetFile<JmapFile>(), env);
            default: return null;
        }
    }

    private static string? VerifyRewriteEquality<TFile>(TFile file, ContentWorkspace workspace) where TFile : BaseFile
    {
        if (file.FileHandler.Stream.Length == 0) return null; // we don't support writing of mply as mply; just pretend it went fine
        var newfile = RewriteCloneRawStream(file, workspace);
        if (!newfile.Read()) return "read/write error";

        return CompareValues(file, newfile);
    }

    private static TFile RewriteCloneRawStream<TFile>(TFile file, ContentWorkspace workspace) where TFile : BaseFile
    {
        // hard clone the file stream to ensure the original file stays intact in case of any destructive modifications in the file writer
        var stream = new MemoryStream((int)file.FileHandler.Stream.Length);
        var handler = new FileHandler(stream, file.FileHandler.FilePath) { FileVersion = file.FileHandler.FileVersion };
        file.FileHandler.Seek(0);
        file.FileHandler.Stream.CopyTo(stream);
        handler.Seek(0);
        var newFile = DefaultFileLoader<TFile>.GetFileConstructor().Invoke(workspace, handler);
        newFile.Read();
        handler.Seek(0);
        newFile.Write();
        handler.Seek(0);
        return newFile;
    }

    private static string? CompareValues(object originalValue, object newValue)
    {
        var type = originalValue.GetType();
        if (type != newValue.GetType())
        {
            return "type mismatch " + type + " and " + newValue.GetType();
        }

        if (type == typeof(string)) {
            return (string)originalValue == (string)newValue ? null : " string changed " + originalValue + " => " + newValue;
        }
        if (type == typeof(float)) {
            if (float.IsInfinity((float)originalValue)) {
                return float.IsInfinity((float)newValue) ? null : "float changed " + originalValue + " => " + newValue;
            } else {
                return (MathF.Abs((float)originalValue - (float)newValue) < 0.0001f) ? null : "float changed " + originalValue + " => " + newValue;
            }
        }
        if (type.IsValueType) {
            if (type.GetInterface(nameof(IComparable)) != null) {
                if (((IComparable)originalValue).CompareTo(newValue) == 0) return null;
                return " Values are not equal: " + originalValue + " => " + newValue;
            }
        }

        if (!comparedValueMappers.TryGetValue(type, out var mapper)) {
            if (originalValue is IList) {
                comparedValueMappers[type] = mapper = (obj) => ((IList)obj).Cast<object?>();
                mapperNameLookups[type] = index => index.ToString();
            } else {
                var fields = type.GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance);
                var props = type.GetProperties(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance).Where(p => p.CanRead && p.GetMethod!.GetParameters().Length == 0 && p.GetMethod.GetBaseDefinition().DeclaringType != typeof(BaseModel) && p.GetMethod.GetBaseDefinition().DeclaringType != typeof(BaseFile));
                comparedValueMappers[type] = mapper = (obj) => fields.Select(f => f.GetValue(obj)).Concat(props.Select(p => p.GetValue(obj)));
                var names = fields.Select(f => f.Name).Concat(props.Select(p => p.Name)).ToArray();
                mapperNameLookups[type] = (index) => names[index];
            }
        }

        int index = 0;
        var list1 = mapper.Invoke(originalValue);
        using var list2 = mapper.Invoke(newValue).GetEnumerator();
        foreach (var org in list1)
        {
            var name = mapperNameLookups[type](index++);
            if (!list2.MoveNext()) {
                return "missing values after index " + name;
            }

            var newVal = list2.Current;
            if ((org == null) != (newVal == null)) {
                // return $"{type.Name}[{name}].{(newVal == null ? "NULL" : "NOT NULL")}";
                return $"[{name}].{(newVal == null ? "NULL" : "NOT NULL")}";
            }
            if (org == null || newVal == null) {
                continue;
            }

            var comparison = CompareValues(org, newVal);
            if (comparison != null) {
                // Log.Warn($"[{name}].{comparison}");
                return $"[{name}].{comparison}";
            }
        }
        if (list2.MoveNext()) {
            return "too many values after index " + index;
        }

        return null;
    }

    static FileTesterWindow()
    {
        AddCompareMapper<NChild>((m) => [m.ChildNode?.ID, m.Condition]);
        AddCompareMapper<MeshFile>((m) => [
            // 0-9
            m.Header.flags, m.Header.nameCount, m.Header.uknCount, m.Header.ukn, m.Header.ukn1, m.Header.wilds_unkn1, m.Header.wilds_unkn2, m.Header.wilds_unkn3, m.Header.wilds_unkn4, m.Header.wilds_unkn5,
            // 10+
            m.BoneData, m.MeshBuffer, m.MaterialNames, m.StreamingInfo, m.MeshData, m.ShadowMesh, m.OccluderMesh, m.BlendShapes, m.FloatData, m.StreamingBuffers, m.NormalRecalcData,
        ]);
        AddCompareMapper<MeshBone>((m) => [m.boundingBox, m.index, m.childIndex, m.nextSibling, m.symmetryIndex, m.remapIndex]);
        AddCompareMapper<MeshBuffer>((m) => [
            m.Positions.Length, m.Normals.Length, m.Tangents.Length, m.UV0.Length, m.UV1.Length, m.Weights.Length, m.Colors.Length, m.Faces?.Length, m.IntegerFaces?.Length,
            m.elementCount, m.totalElementCount, m.Headers, m.shadowFaceBufferOffset, m.occFaceBufferOffset]);
        AddCompareMapper<MeshData>((m) => [m.skinWeightCount, m.totalMeshCount, m.lodCount, m.materialCount, m.boundingBox, m.boundingSphere, m.LODs]);
        AddCompareMapper<ShadowMesh>((m) => [m.skinWeightCount, m.totalMeshCount, m.lodCount, m.materialCount, m.LODs]);
        AddCompareMapper<MeshLOD>((m) => [m.VertexCount, m.IndexCount, m.PaddedIndexCount, m.MeshGroups, m.lodFactor, m.vertexFormat]);
        AddCompareMapper<MeshGroup>((m) => [m.groupId, m.indicesCount, m.submeshCount, m.vertexCount, m.Submeshes]);
        AddCompareMapper<Submesh>((m) => [m.materialIndex, m.bufferIndex, m.ukn2, m.indicesCount]);
        AddCompareMapper<MeshStreamingInfo>((m) => [m.Entries]);

        AddCompareMapper<McamlistFile>((m) => [m.Header.Name, m.Header.BaseMcamlistPath, m.Header.numMots, m.Header.uknNum, m.Header.uknValue, m.MotFiles, m.Motions]);
        AddCompareMapper<MotcamFile>((m) => [m.Header.blending, m.Header.frameCount, m.Header.uknFloat, m.Header.uknExtra, m.Clip1, m.Clip2]);
        AddCompareMapper<MotlistFile>((m) => [m.Header.MotListName, m.Header.BaseMotListPath, m.Header.numMots, m.Header.uknNum, m.Header.uknValue, m.MotFiles, m.Motions]);
        AddCompareMapper<MotFile>((m) => [m.Name, m.MotPropertyTracks, m.BoneClips, m.Clips, m.Bones,
            // ignoring these two for now - end clips because the read is inconsistent, tree because it's massive and also works fine
            // m.EndClips, m.PropertyTree
        ]);
        AddCompareMapper<MotTreeFile>((m) => [m.Name, m.MotionIDRemaps, m.expandedMotionsCount, m.uknCount1, m.uknCount2]);
        AddCompareMapper<MotBone>((m) => [m.boneName, m.boneHash, m.Children.Count, m.Index, m.quaternion, m.translation, m.Parent?.boneName, m.uknValue1, m.uknValue2]);
        AddCompareMapper<Track>((m) => [m.translations, m.rotations, m.flags, m.floats, m.frameIndexes, m.keyCount, m.maxFrame, m.frameRate]);
        AddCompareMapper<BoneClipHeader>((m) => [m.boneHash, m.boneIndex, m.boneName, m.trackFlags, m.uknFloat, m.uknIndex]);
        AddCompareMapper<MotIndex>((m) => m.data.Cast<object>().Concat(new object[] { m.extraClipCount, m.motNumber }), true);

        AddCompareMapper<RszInstance>((m) => m.Values.Append(m.RszClass.crc), true);
        AddCompareMapper<RSZFile>((m) => []);

        AddCompareMapper<RcolFile>((m) => [m.Header, m.Groups, m.RequestSets, m.IgnoreTags, m.AutoGenerateJointDescs, m.RSZ.ObjectList]);
        AddCompareMapper<GroupInfo>((m) => [m.guid, m.LayerGuid, m.LayerIndex, m.MaskBits, m.MaskGuids, m.Name, m.NameHash]);

        AddCompareMapper<UVarFile>((m) => [m.Header.embedCount, m.Header.magic, m.Header.name, m.Header.ukn, m.Header.UVARhash, m.Header.variableCount, m.EmbeddedUVARs, m.Variables]);
        AddCompareMapper<ReeLib.UVar.Variable>((m) => [m.guid, m.Value, m.Expression, m.flags, m.Name, m.nameHash]);

        AddCompareMapper<ReeLib.Rcol.Header>((m) => [m.numGroups, m.numIgnoreTags, m.numRequestSets, m.numShapes, m.numUserData, m.maxRequestSetId, m.status, m.uknRe3_A, m.uknRe3_B, m.ukn1, m.ukn2, m.uknRe3]);
        AddCompareMapper<ClipFile>((m) => [m.Header, m.TrackGroups, m.RootTracks, m.Sections, m.Clip]);
        AddCompareMapper<ReeLib.Tml.ClipHeader>((m) => [
            m.version, m.magic, m.numFrames, m.guid,
            m.rootTrackCount, m.trackCount, m.sectionCount, m.nodeReorderCount, m.trackCount, m.propertyCount, m.keysCount]);
        AddCompareMapper<ReeLib.Clip.PropertyInfo>((m) => [m.arrayIndex, m.ChildMembershipCount, m.ChildStartIndex, m.DataType, m.endFrame, m.startFrame, m.FunctionName, m.nameAsciiHash, m.nameUtf16Hash]);
        AddCompareMapper<TimelineTrack>((m) => [m.Name, m.pragmataHash, m.nameHash1, m.nameUtf16Hash, m.startFrame, m.endFrame, m.guid1, m.guid2, m.Tag, m.nodeType, m.uknByte, m.uknByte2, m.TimelineChildTracks]);

        AddCompareMapper<GuiFile>((m) => [m.Containers, m.RootViewElement, m.AttributeOverrides, m.Resources, m.LinkedGUIs, m.Parameters, m.ParameterReferences, m.ParameterOverrides]);
        AddCompareMapper<ReeLib.Gui.ContainerInfo>((m) => [m.ID, m.Name, m.ClassName]);
        AddCompareMapper<ReeLib.Gui.Element>((m) => [m.Name, m.ClassName, m.ID, m.ContainerID, m.guid3, m.Attributes, m.ExtraAttributes, m.ElementData]);
        AddCompareMapper<ReeLib.Gui.Attribute>((m) => [m.propertyType, m.OrderIndex, m.uknInt, m.Value]);
        AddCompareMapper<ReeLib.Gui.GuiClip>((m) => [m.ID, m.IsDefault, m.name, m.clip]);
        AddCompareMapper<EmbeddedClip>((m) => [m.Header, m.Bezier3DData, m.ClipInfoList, m.Properties.Count, m.NormalKeys.Count, m.ExtraPropertyData, m.FrameCount, m.Guid, m.SpeedPointData, m.Tracks]);
        AddCompareMapper<Property>((m) => [
            m.Info.FunctionName, m.Info.arrayIndex, m.Info.ChildMembershipCount, m.ChildProperties, m.Keys,
            m.Info.DataType, m.Info.startFrame, m.Info.endFrame,
            m.Info.nameAsciiHash, m.Info.nameUtf16Hash, m.Info.speedPointNum,  m.Info.timelineUkn, m.Info.timelineUkn2, m.Info.uknByte, m.Info.flags, m.Info.uknCount, m.Info.uknRE7_2, m.Info.uknRE7_3, m.Info.uknRE7_4]);
        AddCompareMapper<ClipBaseHeader>((m) => [m.numFrames, m.keysCount, m.trackCount, m.propertyCount, m.guid]);
        AddCompareMapper<ExtraPropertyInfo>((m) => [m.count, m.flags, m.propertyUTF16Hash, m.values]);

        AddCompareMapper<McolFile>((m) => [m.bvh]);
        AddCompareMapper<CocoFile>((m) => [m.CollisionMeshPaths, m.Trees]);
        AddCompareMapper<ReeLib.Aimp.AimpHeader>((m) => [m.agentRadWhenBuild, m.hash, m.mapId.guid, m.mapType, m.name, m.uknId, m.sectionType]);

        AddCompareMapper<Motfsm2File>((m) => [m.BhvtFile, m.TransitionDatas, m.TransitionMaps]);
        AddCompareMapper<BhvtFile>((m) => [m.Header.hash, m.RootNode, m.UserVariables, m.SubVariables, m.GameObjectReferences,
            // 5
            m.ActionRsz.ObjectList.Count, m.StaticActionRsz.ObjectList.Count,
            m.ExpressionTreeConditionsRsz.ObjectList.Count, m.StaticExpressionTreeConditionsRsz.ObjectList.Count,

            // note: not comparing below counts because some files have duplicate RSZ ObjectList entries for the same instance
            // would need a major refactor of how RSZ objects work to handle "correctly"
            // leaving it like this probably shouldn't cause issues

            // m.SelectorCallerRsz.ObjectList.Count, m.StaticSelectorCallerRsz.ObjectList.Count,
            // m.TransitionEventRsz.ObjectList.Count, m.StaticTransitionEventRsz.ObjectList.Count,
            // m.ConditionsRsz.ObjectList.Count, m.StaticConditionsRsz.ObjectList.Count,
            // m.SelectorRsz.ObjectList.Count
        ]);
        AddCompareMapper<BHVTNode>((m) => [
            // 0+
            m.ID, m.isEnd, m.isBranch, m.mNameHash, m.mFullnameHash, m.ParentID, m.Priority, m.SelectorCallerConditionID, m.unknownAI, m.WorkFlags, m.AI_Path, m.Attributes, m.ReferenceTree,
            // 12+
            m.Name, m.Children, m.Selector, m.SelectorCallerCondition, m.SelectorCallers, m.Tags, m.States.States, m.AllStates.AllStates, m.Actions.Actions]);
        AddCompareMapper<NState>((m) => [m.TargetNode?.ID, m.TransitionEvents, m.stateEx, m.TransitionData, m.Condition]);
        AddCompareMapper<NAllState>((m) => [m.TargetNode?.ID, m.TransitionData, m.Condition, m.transitionAttributes]);
        AddCompareMapper<NTransition>((m) => [m.StartNode?.ID, m.Condition, m.transitionEvents]);

        AddCompareMapper<Tmlfsm2File>((m) => [m.BehaviorTrees, m.Clips]);

        AddCompareMapper<EfxFile>((m) => [m.Header, m.Entries, m.Strings, m.BoneRelations, m.Bones, m.ExpressionParameters, m.FieldParameterValues, m.UvarGroups, m.parentFile != null]);
        // AddCompareMapper<EfxHeader>((m) => [m.actionCount, m.boneAttributeEntryCount, m.boneCount, m.effectGroupsCount, m.effectGroupsLength, m.entryCount, m.expressionParameterCount, m.fieldParameterCount, m.dimensionType, m.propBindingIndexCount]);
        // we _should_ be safe to ignore some of the counts not being identical becaus we're deduplicting names
        AddCompareMapper<EfxHeader>((m) => [m.actionCount, m.boneCount, m.entryCount, m.expressionParameterCount, m.fieldParameterCount, m.dimensionType, m.propBindingIndexCount]);
        AddCompareMapper<EFXEntry>((m) => [m.Attributes, m.entryAssignment, m.index, m.name, m.nameHash, m.Groups.Order()]);
        AddCompareMapper<ReeLib.Efx.Strings>((m) => [m.ActionNames, m.BoneNames, m.EfxNames, m.ExpressionParameterNames, m.FieldParameterNames, m.GroupNames.Order()]);
        AddCompareMapper<EFXExpressionParameter>((m) => [m.expressionParameterNameUTF16Hash, m.expressionParameterNameUTF8Hash, m.name, m.type, m.ToString()]);
        AddCompareMapper<EFXExpressionList>((m) => [m.ExpressionCount, m.Expressions]);
        AddCompareMapper<EFXMaterialExpressionList>((m) => [m.ExpressionCount, m.Expressions, m.indexCount, m.indices]);

        AddCompareMapper<MaterialHeader>((m) => [m.alphaFlags, m.gpbfDataCount, m.gpbfNameCount, m.matName, m.mmtrPath, m.pragmataUkn, m.texCount, m.paramCount, m.shaderType]);
        AddCompareMapper<TexHeader>((m) => [m.asciiHash, m.hash, m.texPath, m.texType]);
        AddCompareMapper<ParamHeader>((m) => [m.asciiHash, m.hash, m.componentCount, m.paramName, m.parameter]);
        AddCompareMapper<Quaternion>((m) => [m.X, m.Y, m.Z, m.W]);

        AddCompareMapper<NodeInfo>((m) => [m.index, m.groupIndex, m.localIndex, m.attributes, m.flags, m.Links]);
        AddCompareMapper<LinkInfo>((m) => [m.attributes, m.sourceNodeIndex, m.targetNodeIndex, m.edgeIndex, m.ukn]);
    }

    private static Dictionary<Type, Func<object, IEnumerable<object?>>> comparedValueMappers = new();
    private static Dictionary<Type, Func<int, string>> mapperNameLookups = new();
    private static void AddCompareMapper<T>(Func<T, IEnumerable<object?>> mapper, bool noLookup = false, [CallerArgumentExpression(nameof(mapper))] string expr = null!)
    {
        var fields = NameLookupRegex().Matches(expr).Select(mm => mm.Groups[1].Value).ToArray();
        comparedValueMappers[typeof(T)] = (o) => mapper.Invoke((T)o);
        if (!noLookup) mapperNameLookups[typeof(T)] = index => fields[index];
        else mapperNameLookups[typeof(T)] = index => index.ToString();
    }

    [GeneratedRegex("[\\s,.\\[][a-zA-Z]+\\.([a-zA-Z0-9_?\\.\\?]+?)(?=[, (\\]]|$)")]
    private static partial Regex NameLookupRegex();
}