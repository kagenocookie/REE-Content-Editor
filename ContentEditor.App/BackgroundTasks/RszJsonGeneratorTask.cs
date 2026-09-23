using System.Diagnostics;
using ContentEditor.App;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;

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
                    "--only-versioned-source-fields")) {
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

public class RszJsonGeneratorTaskWindow(ContentWorkspace env) : BaseWindowHandler
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

    private RszJsonGeneratorTask? task;
    private List<string>? lastOutputList;

    public override void OnIMGUI()
    {
        var pendingTask = MainLoop.Instance.BackgroundTasks.GetPendingTask<RszJsonGeneratorTask>();
        if (pendingTask != null && pendingTask != task) {
            ImGui.TextColored(Colors.Note, "RSZ JSON file generation is in progress. Please wait for it to finish or restart Content Editor to cancel it.");
            return;
        }

        if (context.children.Count == 0) {
            context.AddChild("Options", this, new CsharpFlagsEnumFieldHandler<RszJsonGeneratorTask.Options, int>() { HideNumberInput = true }, x => x!.options, (x, v) => x.options = v);
            context.options |= UIOptions.DisableUndoRedo;
        }
        context.ShowChildrenUI();

        ImGui.TextColored(Colors.Note, "Make sure you have a functional python installation.");
        ImGui.TextColored(Colors.Note, "This is not a fully self-contained tool! There will still be some manual work to do! This will mostly provide a somewhat reasonable starting point.");

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
}