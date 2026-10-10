using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ContentEditor.App;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.BackgroundTasks;

public class RszJsonGeneratorTask(ContentWorkspace workspace, RszJsonGeneratorTaskWindow config) : IBackgroundTask
{
    public override string ToString() => $"Generating RSZ file";

    public string? Status { get; private set; }
    public float Progress { get; private set; } = -1;

    public TaskStatus TaskStatus { get; set; }
    public string[] AdditionalRszFiles { get; set; } = [];

    public List<string> OutputLines = new();

    [Flags]
    public enum Options
    {
        None = 0,
        RegenerateBaseFile = 1,
        CleanupKnownNames = 2,
    }

    public Task Execute(CancellationToken token = default)
    {
        string? currentRszBackupPath = null;
        string? currentStrippedBackupPath = null;
        if (config.options.HasFlag(Options.RegenerateBaseFile)) {
            var emuDumperPath = Path.Combine(config.reframeworkPath, "reversing/rsz/emulation-dumper.py");
            var nonNativeDumperPath = Path.Combine(config.reframeworkPath, "reversing/rsz/non-native-dumper.py");
            var scriptsDir = Path.GetDirectoryName(emuDumperPath)!;
            var il2cpp = Path.Combine(Path.GetDirectoryName(config.exePath)!, "il2cpp_dump.json");
            if (!ExecutePython("REF: Installing requirements", scriptsDir, "-m", "pip", "install", "-r", "requirements.txt")) {
                return Task.CompletedTask;
            }
            if (!ExecutePython("Emulation dumper", scriptsDir,
                emuDumperPath,
                $"--p=\"{config.exePath}\"",
                $"--il2cpp_path=\"{il2cpp}\"",
                "--test-mode=False"
            )) {
                return Task.CompletedTask;
            }
            if (!ExecutePython("Non-native dumper", scriptsDir,
                nonNativeDumperPath,
                $"--out_postfix=\"{workspace.Game.name.ToLowerInvariant()}\"",
                $"--natives_path=./native_layouts_{Path.GetFileName(config.exePath)}.json",
                $"--il2cpp_path=\"{il2cpp}\"",
                $"--use_typedefs=False",
                $"--use_hashkeys=True",
                $"--include_parents=True"
            )) {
                return Task.CompletedTask;
            }

            var outputRszPath = Path.Combine(scriptsDir, $"rsz{workspace.Game.name.ToLowerInvariant()}.json");

            if (File.Exists(config.rszJsonPath)) {
                // if we're replacing a previous version's RSZ, make a backup first
                currentRszBackupPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(config.rszJsonPath) + "_backup_" + Guid.NewGuid() + ".json");
                File.Move(config.rszJsonPath, currentRszBackupPath);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(config.rszJsonPath)!);
            File.Copy(outputRszPath, config.rszJsonPath, true);
        }

        if (config.options.HasFlag(Options.CleanupKnownNames)) {
            var dumpDir = Path.Combine(config.reasyPath, "resources/data/dumps");
            var workingRszPath = config.rszJsonPath;
            if (!config.rszJsonPath.StartsWith(dumpDir)) {
                workingRszPath = Path.Combine(dumpDir, Path.GetFileName(config.rszJsonPath));
                File.Copy(config.rszJsonPath, workingRszPath, true);
            }

            var strippedDir = Path.Combine(config.reasyPath, "resources/data/dumps/stripped");
            var fieldPatcherPath = Path.Combine(config.reasyPath, "tools/template_fields_patcher.py");
            var stripperPath = Path.Combine(strippedDir, "stripper_propagator.py");
            if (!ExecutePython("REasy: Installing requirements", dumpDir, "-m", "pip", "install", "fire")) {
                return Task.CompletedTask;
            }

            var filename = Path.GetFileName(workingRszPath);
            var strippedFilename = filename.Replace(".json", "_strip.json");
            var strippedFilepath = Path.Combine(strippedDir, strippedFilename);
            if (File.Exists(strippedFilepath)) {
                currentStrippedBackupPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(filename) + "_backup_" + Guid.NewGuid() + ".json");
                File.Move(strippedFilepath, currentStrippedBackupPath);
            }
            if (!ExecutePython("Stripping base classes", strippedDir, stripperPath, "strip", workingRszPath)) {
                return Task.CompletedTask;
            }
            if (File.Exists(Path.Combine(dumpDir, strippedFilename))) {
                File.Move(Path.Combine(dumpDir, strippedFilename), strippedFilepath, true);
            }

            Progress = 0;
            foreach (var refpath in AdditionalRszFiles) {
                var path = refpath;
                if (refpath == config.rszJsonPath && currentRszBackupPath != null) {
                    path = currentRszBackupPath;
                }
                if (refpath == strippedFilepath && currentStrippedBackupPath != null) {
                    path = currentStrippedBackupPath;
                }
                Progress = (float)AdditionalRszFiles.IndexOf(refpath) / AdditionalRszFiles.Length;
                if (!ExecutePython("Patching field names", strippedDir,
                    fieldPatcherPath,
                    "--source", Path.Combine(strippedDir, strippedFilename),
                    "--patch", path,
                    "--crc",
                    "--force-count-mismatch",
                    "--only-versioned-source-fields",
                    "--no-prune")) {
                    return Task.CompletedTask;
                }
            }

            Progress = -1;
            if (!ExecutePython("Propagating changes", strippedDir, stripperPath, "propagate", strippedFilename)) {
                return Task.CompletedTask;
            }

            if (workingRszPath != config.rszJsonPath) {
                File.Copy(workingRszPath, config.rszJsonPath, true);
            }
        }

        FileSystemUtils.ShowFileInExplorer(config.rszJsonPath);
        Logger.Info($"Generated file saved to {config.rszJsonPath}");
        return Task.CompletedTask;
    }

    private bool ExecutePython(string statusStr, string workingDir, params string[] args)
    {
        Status = statusStr;
        var ps = new ProcessStartInfo(config.pythonExecutable) {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) {
            ps.ArgumentList.Add(arg);
        }

        OutputLines.Add("");
        OutputLines.Add("");
        OutputLines.Add($"Executing: {string.Join(" ", args)}");
        OutputLines.Add("");
        try {
            using var p = Process.Start(ps);
            if (p == null) {
                Logger.Error("Failed to execute command " + string.Join(" ", args));
                return false;
            }
            p.OutputDataReceived += (sender, e) => {
                var data = e.Data;
                if (!string.IsNullOrWhiteSpace(data)) {
                    OutputLines.Add(data.Trim());
                }
            };
            p.ErrorDataReceived += (sender, e) => {
                var data = e.Data;
                if (!string.IsNullOrWhiteSpace(data)) {
                    OutputLines.Add("ERROR: " + data.Trim());
                }
            };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            p.WaitForExit();
            return p.ExitCode == 0;
        } catch (Exception ex) {
            OutputLines.Add("Script execution failed");
            OutputLines.Add(ex.Message);
            return false;
        }
    }
}

public partial class RszJsonGeneratorTaskWindow(ContentWorkspace env) : BaseWindowHandler
{
    public override string HandlerName => "RSZ File Generator";

    public RszJsonGeneratorTask.Options options = RszJsonGeneratorTask.Options.RegenerateBaseFile;
    public string exePath = AppUtils.FindGameExecutable(env.Env.Config.GamePath, env.Game.name) ?? "";
    public string reframeworkPath = AppConfig.Settings.Dev.REFPath ?? "";
    public string reasyPath = AppConfig.Settings.Dev.ReasyPath ?? "";
    public string pythonExecutable = AppConfig.Settings.Dev.PythonPath ?? "";
    public string rszJsonPath = Path.Combine(AppContext.BaseDirectory, "rsz-output", $"rsz{env.Game.name.ToLowerInvariant()}.json");
    public string additonalRszInput = "";
    public List<string> referenceRszList = AppConfig.Settings.Dev.RefRSZList?.ToList() ?? [];

    public string manualMergeTarget = AppConfig.Settings.Dev.ManualMergeTarget ?? "";
    public string manualMergeSource = AppConfig.Settings.Dev.ManualMergeSource ?? "";
    private HashSet<string> ignoredManuals = new();

    private RszJsonGeneratorTask? task;
    private List<string>? lastOutputList;

    public override void OnIMGUI()
    {
        var pendingTask = MainLoop.Instance.BackgroundTasks.GetPendingTask<RszJsonGeneratorTask>();
        if (pendingTask != null && pendingTask != task) {
            ImGui.TextColored(Colors.Note, "RSZ JSON file generation is in progress. Please wait for it to finish or restart Content Editor to cancel it.");
            return;
        }

        if (ImGui.TreeNode("Manual Merge"u8)) {
            ShowManualMergeUI();
            ImGui.TreePop();
        }

        if (context.children.Count == 0) {
            context.AddChild("Options", this, new CsharpFlagsEnumFieldHandler<RszJsonGeneratorTask.Options, int>() { HideNumberInput = true }, x => x!.options, (x, v) => x.options = v);
            context.options |= UIOptions.DisableUndoRedo;
        }
        context.ShowChildrenUI();

        ImGui.TextColored(Colors.Note, "Make sure you have a functional python installation."u8);
        ImGui.TextColored(Colors.Note, "This is not a fully self-contained tool! There will still be some manual work to do! This will mostly provide a somewhat reasonable starting point."u8);

        ImGui.Separator();

        AppImguiHelpers.InputFilepath("Target RSZ JSON file path"u8, ref rszJsonPath, FileFilters.JsonFile);
        if (AppImguiHelpers.InputFilepath("Python path"u8, ref pythonExecutable, FileFilters.ExecutableTool)) {
            AppConfig.Settings.Dev.PythonPath = pythonExecutable;
            AppConfig.Instance.SaveJsonConfig();
        }
        if (options.HasFlag(RszJsonGeneratorTask.Options.RegenerateBaseFile)) {
            ImGui.SeparatorText("Base paths");
            ImGui.TextColored(Colors.Note, "You need to clone the REFramework repository for this step!"u8);
            if (ImguiHelpers.SameLine() && ImGui.Button("Open##REF"u8)) {
                FileSystemUtils.OpenURL("https://github.com/praydog/REFramework");
            }
            if (AppImguiHelpers.InputFolder("REFramework repository file path"u8, ref reframeworkPath)) {
                AppConfig.Settings.Dev.REFPath = reframeworkPath;
                AppConfig.Instance.SaveJsonConfig();
            }
            AppImguiHelpers.InputFilepath("Exe path"u8, ref exePath, FileFilters.Executable);
        }

        if (options.HasFlag(RszJsonGeneratorTask.Options.CleanupKnownNames)) {
            ImGui.SeparatorText("Reference RSZ JSON files"u8);
            ImGui.TextColored(Colors.Note, "You need to clone the REasy repository for this step!"u8);
            ImGui.TextColored(Colors.Info, "This will execute CRC-based patching based on previously resolved RSZ JSON files. Make sure to select the stripped files and not the full ones."u8);
            if (ImguiHelpers.SameLine() && ImGui.Button("Open##REAsy"u8)) {
                FileSystemUtils.OpenURL("https://github.com/seifhassine/REasy");
            }
            if (AppImguiHelpers.InputFolder("REasy repository file path"u8, ref reasyPath)) {
                AppConfig.Settings.Dev.ReasyPath = reasyPath;
                AppConfig.Instance.SaveJsonConfig();
            }
            int i = 0;
            foreach (var file in referenceRszList) {
                ImGui.PushID(i++);
                if (ImGui.Button($"{AppIcons.SI_GenericDelete}")) {
                    referenceRszList.Remove(file);
                    AppConfig.Settings.Dev.RefRSZList = referenceRszList.ToArray();
                    AppConfig.Instance.SaveJsonConfig();
                    ImGui.PopID();
                    break;
                }
                ImGui.SameLine();
                ImGui.Text(file);
                if (ImGui.IsItemClicked()) {
                    EditorWindow.CurrentWindow?.CopyToClipboard(file);
                }
                ImGui.PopID();
            }
            if (ImGui.Button($"{AppIcons.SI_GenericAdd}") && !string.IsNullOrEmpty(additonalRszInput)) {
                if (!referenceRszList.Contains(additonalRszInput)) {
                    referenceRszList.Add(additonalRszInput);
                    additonalRszInput = "";
                    AppConfig.Settings.Dev.RefRSZList = referenceRszList.ToArray();
                    AppConfig.Instance.SaveJsonConfig();
                }
            }
            ImGui.SameLine();
            AppImguiHelpers.InputFilepath("Add file"u8, ref additonalRszInput);
        }
        ImGui.Separator();
        if (task == null && ImGui.Button("Generate"u8)) {
            if (options == RszJsonGeneratorTask.Options.None) {
                Logger.Error("Choose a task first");
                return;
            }
            if (options.HasFlag(RszJsonGeneratorTask.Options.RegenerateBaseFile)) {
                if (!File.Exists(exePath)) {
                    Logger.Error("Exe file not found!");
                    return;
                }
                if (!File.Exists(Path.Combine(Path.GetDirectoryName(exePath)!, "il2cpp_dump.json"))) {
                    Logger.Error("Generate the il2cpp.json first with REFramework!");
                    return;
                }
            }
            if (options.HasFlag(RszJsonGeneratorTask.Options.CleanupKnownNames)) {
                foreach (var p in referenceRszList) {
                    if (!File.Exists(p)) {
                        Logger.Error($"File {p} not found!");
                        return;
                    }
                }

                if (!Directory.Exists(reasyPath) || !File.Exists(Path.Combine(reasyPath, "resources/data/dumps/stripped/stripper_propagator.py"))) {
                    Logger.Error("REasy repository path is missing or invalid");
                    return;
                }
            }

            MainLoop.Instance.BackgroundTasks.Queue(task = new RszJsonGeneratorTask(env, this) {
                AdditionalRszFiles = referenceRszList.Distinct().ToArray(),
            });
            ImGui.SameLine();
        }
        if (task != null) {
            if (task.TaskStatus is TaskStatus.Canceled or TaskStatus.Faulted or TaskStatus.RanToCompletion) {
                task = null;
            } else {
                lastOutputList = task.OutputLines;
            }
        }

        var lines = task?.OutputLines ?? lastOutputList;
        if (lines != null) {
            var avail = ImGui.GetContentRegionAvail() - new System.Numerics.Vector2(0, ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().WindowPadding.Y);
            ImGui.BeginChild("RszGenOutput"u8, avail, ImGuiChildFlags.Borders);
            for (int i = 0; i < lines.Count; i++) {
                var line = lines[i];
                ImGui.Text(line);
            }
            ImGui.EndChild();
        }

        if (task != null && ImGui.Button(Lang.Buttons.Cancel)) {
            if (task != null) {
                MainLoop.Instance.BackgroundTasks.CancelTask(task);
            }
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }
        if (lines != null) {
            if (task != null) ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_GenericClear}")) {
                lines?.Clear();
                if (task == null) {
                    lastOutputList = null;
                }
            }
        }
    }

    private Dictionary<string, SafeRszClass>? manualRszSource;
    private Dictionary<string, SafeRszClass>? manualRszTarget;
    private string? manualMergeError;
    private sealed record SafeRszClass(JsonObject obj, string name)
    {
        public required SafeRszField[] fields;
        public HashSet<int> Skips = new();
        public JsonObject ToJson()
        {
            var o = (JsonObject)obj.DeepClone();
            o["fields"] = new JsonArray(fields.Select(f => f.obj.DeepClone()).ToArray());
            return o;
        }
        public override string ToString() => name;
    }
    private sealed record SafeRszField(JsonObject obj)
    {
        public string name = (string)obj!["name"]!;
        public string type = (string)obj["type"]!;
        public bool array = (bool)obj["array"]!;
        public int size = (int)obj["size"]!;
        public int align = (int)obj["align"]!;

        public SafeRszField DeepClone() => new SafeRszField((JsonObject)obj.DeepClone());
        public override string ToString() => $"sz={size:D02} al={align:D02} arr={array}; {type} | {name}";
    }

    private string _newName = "";
    private (string? hash, int fieldIndex) _newStrTarget;
    private bool newIsClassname;
    private bool _startRenameThisFrame;

    private static readonly Dictionary<(int size, int align), RszFieldType[]> probablyTypesForSizes = new() {
        { (1, 1), [ RszFieldType.Bool, RszFieldType.U8, RszFieldType.S8, RszFieldType.S32 ] },
        { (4, 4), [ RszFieldType.S32, RszFieldType.U32, RszFieldType.F32 ] },
        { (8, 4), [ RszFieldType.Float2, RszFieldType.Range, RszFieldType.Uint2 ] },
        { (12, 4), [ RszFieldType.Float3, RszFieldType.Uint3 ] },
        { (16, 4), [ RszFieldType.Float4, RszFieldType.Uint4 ] },
        { (16, 8), [ RszFieldType.GameObjectRef, RszFieldType.Guid ] },
        { (16, 16), [ RszFieldType.Vec3, RszFieldType.Vec2, RszFieldType.Vec4, RszFieldType.Quaternion, RszFieldType.Sphere ] },
        { (32, 16), [ RszFieldType.AABB ] },
        { (80, 16), [ RszFieldType.OBB ] },
    };

    private void ShowManualMergeUI()
    {
        var changed = AppImguiHelpers.InputFilepath("Merge into file"u8, ref manualMergeTarget, FileFilters.JsonFile);
        changed |= AppImguiHelpers.InputFilepath("Reference file"u8, ref manualMergeSource, FileFilters.JsonFile);

        if (changed) {
            manualRszSource = null;
            manualRszTarget = null;

            AppConfig.Settings.Dev.ManualMergeTarget = manualMergeTarget;
            AppConfig.Settings.Dev.ManualMergeSource = manualMergeSource;
            AppConfig.Settings.Save();
        } else if (!string.IsNullOrEmpty(manualMergeError)) {
            ImGui.TextColored(Colors.Error, manualMergeError);
            if (ImGui.Button(Lang.Buttons.Clear)) {
                manualMergeError = null;
            }
            return;
        }

        if (manualRszTarget == null || manualRszSource == null) {
            if (!File.Exists(manualMergeTarget) || !File.Exists(manualMergeSource)) {
                manualMergeError = "RSZ files not found";
                return;
            }
            try {
                var manualRszSource1 = JsonSerializer.Deserialize<Dictionary<string, JsonObject>>(File.ReadAllText(manualMergeSource));
                var manualRszTarget1 = JsonSerializer.Deserialize<Dictionary<string, JsonObject>>(File.ReadAllText(manualMergeTarget));
                if (manualRszTarget1 == null || manualRszSource1 == null) {
                    manualMergeError = "Couldn't read RSZ files";
                    return;
                }

                manualRszSource = new Dictionary<string, SafeRszClass>();
                manualRszTarget = new Dictionary<string, SafeRszClass>();
                foreach (var (k, v) in manualRszSource1) {
                    var fields = v["fields"];
                    if (fields == null) continue;

                    var fields1 = (fields as JsonArray)?.Select(item => new SafeRszField((JsonObject)item!))!.ToArray()!;
                    manualRszSource[k] = new SafeRszClass(v, (string)v["name"]!) { fields = fields1 };
                }
                foreach (var (k, v) in manualRszTarget1) {
                    var fields = v["fields"];
                    if (fields == null) continue;

                    var fields1 = (fields as JsonArray)?.Select(item => new SafeRszField((JsonObject)item!))!.ToArray()!;
                    manualRszTarget[k] = new SafeRszClass(v, (string)v["name"]!) { fields = fields1 };
                }
            } catch (Exception e) {
                manualMergeError = e.Message;
                return;
            }
            if (manualRszTarget == null || manualRszSource == null) {
                manualMergeError = "Couldn't read RSZ files";
                return;
            }
        }

        var unresolved = manualRszTarget.Where(tt =>
            tt.Value.fields.Any(f => IsNumberedFieldNameRegex().IsMatch(f.name)));

        if (!unresolved.Any()) {
            ImGui.TextColored(Colors.Success, "Everything seems to be in order!");
            return;
        }

        ImGui.Text($"Unresolved classes: {unresolved.Count()} (hidden: {ignoredManuals.Count})");

        int limit = 25;
        var sz = ImGui.GetContentRegionAvail();
        // var buttonW = UI.UIScale * 40;
        // var compW = sz.X / 2 - ImGui.GetStyle().CellPadding.X - buttonW - ImGui.GetStyle().FramePadding.X * 2;
        var compW = sz.X / 2 - ImGui.GetStyle().CellPadding.X;
        var h = sz.Y;
        foreach (var (hash, target) in unresolved) {
            if (ignoredManuals.Contains(hash)) continue;
            if (!manualRszSource.TryGetValue(hash, out var src) || src == null) {
                continue;
            }

            // run comparison first
            int lastEqualIndex = -1;
            int lastIdenticalIndex = -1;
            var skipped = 0;
            int j = 0;
            for (int i = 0; i < target.fields.Length; i++) {
                var f = target.fields[i];
                var isSkip = target.Skips.Contains(i);
                if (isSkip) {
                    skipped++;
                    continue;
                }
                var targetI = j++;
                while (src.Skips.Contains(targetI)) {
                    targetI = j++;
                }
                var targetF = src.fields.ElementAtOrDefault(targetI);
                if (lastIdenticalIndex == -1 && targetF?.name != f.name) {
                    lastIdenticalIndex = i;
                }

                if (lastEqualIndex == -1) {
                    if (targetF == null) {
                        lastEqualIndex = i;
                    } else if (targetF.align == f.align && targetF.size == f.size && targetF.array == f.array) {
                        //
                    } else {
                        lastEqualIndex = i;
                        break;
                    }
                }
            }

            if (lastIdenticalIndex == -1 && target.fields.All(f => !IsNumberedFieldNameRegex().IsMatch(f.name))) {
                // already identical and fully resolved, nothing much to do here
                continue;
            }

            var header = $"{hash} {target.name}";

            ImGui.SetNextWindowSize(new System.Numerics.Vector2(compW, h));
            ImGui.BeginChild($"target##{limit}", ImGuiChildFlags.ResizeY|ImGuiChildFlags.AutoResizeY);

            ImGui.Text(header);
            skipped = 0;
            for (int i = 0; i < target.fields.Length; i++) {
                var f = target.fields[i];
                var str = f.ToString();
                var isSkip = target.Skips.Contains(i);
                if (isSkip) {
                    ImGui.TextColored(Colors.Faded, str);
                    skipped++;
                } else if (i < lastIdenticalIndex || lastIdenticalIndex == -1) {
                    ImGui.TextColored(Colors.Success, str);
                } else if (i < lastEqualIndex || lastEqualIndex == -1) {
                    ImGui.TextColored(Colors.Note, str);
                } else {
                    ImGui.Text(str);
                }

                if (ImGui.IsItemClicked()) {
                    if (VirtualClipboard.TryGetFromClipboard<SafeRszField>(out var pasted)) {
                        var ii = i;
                        UndoRedo.RecordCallbackSetter(null, target, f, pasted, (x, v) => x.fields[ii] = v);
                    }
                }
                if (target.fields.Length >= i && ImGui.BeginPopupContextItem(str)) {
                    if (ImGui.Selectable(isSkip ? "Unskip" : "Skip")) {
                        UndoRedo.RecordCallbackSetter(null, target, (skip: isSkip, i), (skip: !isSkip, i), static (c, val) => {
                            if (val.skip) {
                                c.Skips.Add(val.i);
                            } else {
                                c.Skips.Remove(val.i);
                            }
                        });
                    }
                    if (ImGui.Selectable("Change name"u8)) {
                        _newStrTarget = (hash, i);
                        newIsClassname = false;
                        _newName = "";
                        _startRenameThisFrame = true;
                    }
                    if (ImGui.Selectable("Change classname"u8)) {
                        _newStrTarget = (hash, i);
                        newIsClassname = true;
                        _newName = f.obj["original_type"]?.GetValue<string>() ?? "";
                        _startRenameThisFrame = true;
                    }
                    if (probablyTypesForSizes.TryGetValue((f.size, f.align), out var recs)) {
                        foreach (var rec in recs) {
                            if (ImGui.Selectable($"Type: {rec}")) {
                                UndoRedo.RecordCallbackSetter(null, f, f.type, rec.ToString(), static (ff, tt) => {
                                    ff.type = tt;
                                    ff.obj["type"] = tt;
                                });
                            }
                        }
                    }
                    if (ImGui.BeginMenu("Change type >"u8)) {
                        foreach (var newT in Enum.GetValues<RszFieldType>()) {
                            var tstr = newT.ToString();
                            if (ImGui.Selectable(tstr, tstr == f.type)) {
                                UndoRedo.RecordCallbackSetter(null, f, f.type, tstr, static (ff, tt) => {
                                    ff.type = tt;
                                    ff.obj["type"] = tt;
                                });
                            }
                        }
                        ImGui.EndMenu();
                    }
                    ImGui.EndPopup();
                }
                if (_newStrTarget.hash == hash && i == _newStrTarget.fieldIndex) {
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200 * UI.UIScale);
                    if (_startRenameThisFrame) {
                        _startRenameThisFrame = false;
                        ImGui.SetKeyboardFocusHere();
                    }
                    ImGui.InputText("##newname", ref _newName, 80);
                    if (ImGui.Button("Confirm")) {
                        if (newIsClassname) {
                            UndoRedo.RecordCallbackSetter(null, target.fields[i], target.fields[i].obj["original_type"] ?? "", _newName, (f, n) => {
                                f.obj["original_type"] = n;
                            });
                        } else {
                            UndoRedo.RecordCallbackSetter(null, target.fields[i], target.fields[i].name, _newName, (f, n) => {
                                f.name = n;
                                f.obj["name"] = n;
                            });
                        }
                        _newStrTarget = default;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel")) {
                        _newStrTarget = default;
                    }
                }
            }
            ImGui.EndChild();

            ImGui.SameLine();

            ImGui.SetNextWindowSize(new System.Numerics.Vector2(compW, h));
            ImGui.BeginChild($"source##{limit}", ImGuiChildFlags.ResizeY|ImGuiChildFlags.AutoResizeY);
            ImGui.Text(header);

            if (lastIdenticalIndex == -1) {
                if (ImGui.Button("Hide this class")) {
                    UndoRedo.RecordCallbackSetter(null, (ignoredManuals, hash), false, true, (data, ignore) => {
                        if (ignore) {
                            data.ignoredManuals.Add(hash);
                        } else {
                            data.ignoredManuals.Remove(hash);
                        }
                    });
                }
            }

            skipped = 0;
            for (int i = 0; i < src.fields.Length; i++) {
                var f = src.fields[i];
                var str = f.ToString();
                var isSkip = src.Skips.Contains(i);
                var prevSkipped = target.Skips.Count(ss => ss <= i);
                if (isSkip) {
                    ImGui.TextColored(Colors.Faded, str);
                    skipped++;
                } else if (i + prevSkipped - skipped < lastIdenticalIndex || lastIdenticalIndex == -1) {
                    ImGui.TextColored(Colors.Success, str);
                } else if (i + prevSkipped - skipped < lastEqualIndex || lastEqualIndex == -1) {
                    ImGui.TextColored(Colors.Note, str);
                } else {
                    ImGui.Text(str);
                }
                if (ImGui.IsItemClicked()) {
                    VirtualClipboard.CopyToClipboard(f.DeepClone());
                }
                if (ImGui.BeginPopupContextItem(str)) {
                    if (ImGui.Selectable(isSkip ? "Unskip" : "Skip")) {
                        UndoRedo.RecordCallbackSetter(null, src, (skip: isSkip, i), (skip: !isSkip, i), static (c, val) => {
                            if (val.skip) {
                                c.Skips.Add(val.i);
                            } else {
                                c.Skips.Remove(val.i);
                            }
                        });
                    }

                    if (target.fields.Length >= i && ImGui.Selectable("Transfer all unskipped up to (including) this field")) {
                        var newRange = src.fields.Where((ff, ii) => ii <= i && !src.Skips.Contains(ii)).Select(x => x.DeepClone()).ToArray();
                        var oldRange = target.fields.Where((ff, ii) => !target.Skips.Contains(ii)).Take(newRange.Length).Select(x => x.DeepClone()).ToArray();
                        if (newRange.Length == src.fields.Length && oldRange.Length == target.fields.Length) {
                            // full replace
                            UndoRedo.RecordCallbackSetter(null, target, target.fields, src.fields, (tt, fs) => {
                                tt.fields = fs;
                            });
                        } else {
                            UndoRedo.RecordCallbackSetter(null, target, oldRange, newRange, static (list, range) => {
                                int j = 0;
                                for (int i = 0; i < range.Length; i++) {
                                    if (list.Skips.Contains(i + j)) {
                                        j++;
                                        i--;
                                        continue;
                                    }
                                    list.fields[i + j] = range[i];
                                }
                            });
                        }
                    }

                    if (ImGui.Selectable("Fully replace all target fields"u8)) {
                        UndoRedo.RecordCallbackSetter(null, target, target.fields, src.fields, (tt, fs) => {
                            tt.fields = fs;
                        });
                    }

                    if (ImGui.Selectable("Hide this class"u8)) {
                        UndoRedo.RecordCallbackSetter(null, (ignoredManuals, hash), false, true, (data, ignore) => {
                            if (ignore) {
                                data.ignoredManuals.Add(hash);
                            } else {
                                data.ignoredManuals.Remove(hash);
                            }
                        });
                    }
                    ImGui.EndPopup();
                }
            }

            ImGui.EndChild();

            ImGui.Separator();

            if (limit-- <= 0) break;
        }

        if (ImGui.Button("Save changes to ...")) {
            var updatedJson = manualRszTarget.ToDictionary(kv => kv.Key, kv => kv.Value.ToJson());

            PlatformUtils.ShowSaveFileDialog((outPath) => {
                using var fs = File.Create(outPath);
                JsonSerializer.Serialize(fs, updatedJson, JsonConfig.rszJsonOptions);
            }, manualMergeTarget, FileFilters.JsonFile);
        }
        ImGui.SameLine();
        if (ImGui.Button("Unhide all")) {
            var bckp = ignoredManuals;
            UndoRedo.RecordCallbackSetter(null, this, ignoredManuals, new(), (data, newSet) => {
                data.ignoredManuals = newSet;
            });
        }
    }

    [GeneratedRegex("^v\\d+$")]
    private static partial Regex IsNumberedFieldNameRegex();
}