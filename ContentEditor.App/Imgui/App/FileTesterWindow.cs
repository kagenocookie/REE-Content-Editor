using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using ContentEditor.App.FileLoaders;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Clip;
using ReeLib.Common;
using ReeLib.Mesh;
using ReeLib.Motlist;
using ReeLib.Rcol;

namespace ContentEditor.App;

public class FileTesterWindow : IWindowHandler
{
    public string HandlerName => "File Tester";
    public bool HasUnsavedChanges => false;
    int IWindowHandler.FixedID => -2312;

    private KnownFileFormats format;
    private string? formatFilter;
    private bool allVersions;
    private bool testRewrite;
    private bool smokeTest;

    private const int SmokeTestFileLimit = 25;

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
    private ContentWorkspace? Workspace => (data.ParentWindow as IWorkspaceContainer)?.Workspace;

    private static readonly KnownFileFormats[] AllFormatValues = Enum.GetValues<KnownFileFormats>();
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

        ImguiHelpers.FilterableCombo("File type", FormatLabels, Formats, ref format, ref formatFilter);
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
                        } catch (Exception) {
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
        return format == KnownFileFormats.Mesh && filepath.StartsWith("natives/stm/streaming");
    }

    private static IEnumerable<(string path, MemoryStream stream)> GetFileList(ContentWorkspace env, KnownFileFormats format)
    {
        var exts = env.Env.GetFileExtensionsForFormat(format);
        if (PakUtils.ScanPakFiles(env.Env.Config.GamePath).Count > 0) {
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
            case KnownFileFormats.Timeline: return VerifyRewriteEquality<TmlFile>(source.GetFile<TmlFile>(), env);
            case KnownFileFormats.GUI: return VerifyRewriteEquality<GuiFile>(source.GetFile<GuiFile>(), env);
            default: return null;
        }
    }

    private static string? VerifyRewriteEquality<TFile>(TFile file, ContentWorkspace workspace) where TFile : BaseFile
    {
        var newfile = file.RewriteClone(workspace);
        if (!newfile.Read()) return "read/write error";

        return CompareValues(file, newfile);
    }

    private static string? CompareValues(object originalValue, object newValue)
    {
        var type = originalValue.GetType();
        if (type != newValue.GetType())
        {
            return "type mismatch " + type + " and " + newValue.GetType();
        }

        if (type.IsValueType) {
            if (type.GetInterface(nameof(IComparable)) != null) {
                if (((IComparable)originalValue).CompareTo(newValue) == 0) return null;
                return "Values are not equal: " + originalValue + " => " + newValue;
            }
        }
        if (type == typeof(string)) {
            return (string)originalValue == (string)newValue ? null : "string changed " + originalValue + " => " + newValue;
        }
        if (type == typeof(float)) {
            return (MathF.Abs((float)originalValue - (float)newValue) > 0.0001f) ? null : "float changed " + originalValue + " => " + newValue;
        }

        if (!comparedValueMappers.TryGetValue(type, out var mapper)) {
            if (originalValue is IList) {
                comparedValueMappers[type] = mapper = (obj) => ((IList)obj).Cast<object?>();
            } else {
                var fields = type.GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance);
                var props = type.GetProperties(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance).Where(p => p.CanRead && p.GetMethod!.GetParameters().Length == 0 && p.GetMethod.GetBaseDefinition().DeclaringType != typeof(BaseModel));
                comparedValueMappers[type] = mapper = (obj) => fields.Select(f => f.GetValue(obj)).Concat(props.Select(p => p.GetValue(obj)));
            }
        }

        int index = 0;
        var list1 = mapper.Invoke(originalValue);
        using var list2 = mapper.Invoke(newValue).GetEnumerator();
        foreach (var org in list1)
        {
            if (!list2.MoveNext()) {
                return "missing values after index " + index;
            }

            var newVal = list2.Current;
            if ((org == null) != (newVal == null)) {
                return $"{type.Name}[{index}].{(newVal == null ? "NULL" : "NOT NULL")}";
            }
            if (org == null || newVal == null) continue;

            var comparison = CompareValues(org, newVal);
            if (comparison != null) {
                return $"{type.Name}[{index}].{comparison}";
            }
            index++;
        }
        if (list2.MoveNext()) {
            return "too many values after index " + index;
        }

        return null;
    }

    static FileTesterWindow()
    {
        AddCompareMapper<MeshFile>((m) => [
            // 0-9
            m.Header.flags, m.Header.nameCount, m.Header.uknCount, m.Header.ukn, m.Header.ukn1, m.Header.wilds_unkn1, m.Header.wilds_unkn2, m.Header.wilds_unkn3, m.Header.wilds_unkn4, m.Header.wilds_unkn5,
            // 10+
            m.BoneData, m.MeshBuffer, m.MaterialNames, m.StreamingInfo, m.MeshData, m.ShadowMesh, m.OccluderMesh, m.BlendShapes, m.FloatData, m.StreamingBuffers, m.NormalRecalcData
        ]);
        AddCompareMapper<MeshBone>((m) => [m.boundingBox, m.index, m.childIndex, m.nextSibling, m.symmetryIndex, m.remapIndex]);
        AddCompareMapper<MeshBuffer>((m) => [
            m.Positions.Length, m.Normals.Length, m.Tangents.Length, m.UV0.Length, m.UV1.Length, m.Weights.Length, m.Colors.Length, m.Faces.Length,
            m.elementCount, m.totalElementCount, m.Headers, m.uknSize1, m.uknSize2]);
        AddCompareMapper<MeshData>((m) => [m.uvCount, m.skinWeightCount, m.totalMeshCount, m.lodCount, m.materialCount, m.boundingBox, m.boundingSphere, m.LODs]);
        AddCompareMapper<ShadowMesh>((m) => [m.uvCount, m.skinWeightCount, m.totalMeshCount, m.lodCount, m.materialCount, m.LODs]);
        AddCompareMapper<MeshLOD>((m) => [m.VertexCount, m.IndexCount, m.PaddedIndexCount, m.MeshGroups, m.lodFactor, m.vertexFormat]);
        AddCompareMapper<MeshGroup>((m) => [m.groupId, m.indicesCount, m.submeshCount, m.vertexCount, m.Submeshes]);
        AddCompareMapper<Submesh>((m) => [m.materialIndex, m.bufferIndex, m.ukn2, m.indicesCount]);
        AddCompareMapper<MeshStreamingInfo>((m) => [m.Entries]);

        AddCompareMapper<MotlistFile>((m) => [m.Header.MotListName, m.Header.BaseMotListPath, m.MotFiles, m.Motions]);
        AddCompareMapper<MotFile>((m) => [m.Name]);
        AddCompareMapper<MotTreeFile>((m) => [m.Name, m.MotionIDRemaps, m.expandedMotionsCount, m.uknCount1, m.uknCount2]);
        AddCompareMapper<MotIndex>((m) => m.data.Cast<object>().Concat(new object[] { m.extraClipCount, m.motNumber }));

        AddCompareMapper<RszInstance>((m) => m.Values.Append(m.RszClass.crc));
        AddCompareMapper<RSZFile>((m) => []);

        AddCompareMapper<RcolFile>((m) => [m.Header, m.Groups, m.RequestSets, m.IgnoreTags, m.AutoGenerateJointDescs]);
        AddCompareMapper<GroupInfo>((m) => [m.guid, m.LayerGuid, m.LayerIndex, m.MaskBits, m.NameHash, m.NumShapes, m.NumMaskGuids, m.NumExtraShapes]);

        AddCompareMapper<ReeLib.Rcol.Header>((m) => [m.numGroups, m.numIgnoreTags, m.numRequestSets, m.numShapes, m.numUserData, m.maxRequestSetId, m.userDataSize, m.status, m.uknRe3_A, m.uknRe3_B, m.ukn1, m.ukn2, m.uknRe3]);
        AddCompareMapper<TmlFile>((m) => [m.Tracks, m.Header, m.HermiteData, m.SpeedPointData, m.Bezier3DData, m.ClipInfo]);
        AddCompareMapper<ReeLib.Tml.Header>((m) => [
            m.version, m.magic, m.totalFrames, m.guid,
            m.rootNodeCount, m.trackCount, m.nodeGroupCount, m.nodeReorderCount, m.nodeCount, m.propertyCount, m.keyCount]);

        AddCompareMapper<GuiFile>((m) => [m.Containers, m.RootViewElement, m.AttributeOverrides, m.Resources, m.LinkedGUIs, m.Parameters, m.ParameterReferences, m.ParameterOverrides]);
        AddCompareMapper<ReeLib.Gui.ContainerInfo>((m) => [m.guid, m.Name, m.ClassName]);
        AddCompareMapper<ReeLib.Gui.Element>((m) => [m.Name, m.ClassName, m.ID, m.ContainerId, m.guid3, m.Attributes, m.ExtraAttributes, m.ElementData]);
        AddCompareMapper<ReeLib.Gui.Attribute>((m) => [m.propertyType, m.OrderIndex, m.uknInt, m.Value]);
        AddCompareMapper<ReeLib.Gui.GuiClip>((m) => [m.guid, m.IsDefault, m.name, m.clip]);
        AddCompareMapper<EmbeddedClip>((m) => [m.Header, m.Bezier3DData, m.ClipInfoList, m.ClipKeys, m.ExtraPropertyData, m.FrameCount, m.Guid, m.HermiteData, m.SpeedPointData, m.Tracks]);
        AddCompareMapper<ClipHeader>((m) => [m.numFrames, m.numKeys, m.numNodes, m.numProperties, m.guid]);
        AddCompareMapper<ExtraPropertyInfo>((m) => [m.count, m.flags, m.propertyUTF16Hash, m.values]);
    }

    private static Dictionary<Type, Func<object, IEnumerable<object?>>> comparedValueMappers = new();
    private static void AddCompareMapper<T>(Func<T, IEnumerable<object?>> mapper)
    {
        comparedValueMappers[typeof(T)] = (o) => mapper.Invoke((T)o);
    }
}