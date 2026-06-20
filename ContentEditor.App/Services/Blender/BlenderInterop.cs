using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using ContentEditor.Core;

namespace ContentEditor.App.Blender;

public class BlenderInterop
{
    private static bool _hasShownNoBlenderWarning = false;
    private static CancellationTokenSource? cancellationTokenSource;
    private const int BlenderTimeoutMs = 30000;

    public static string GetScript(string name)
    {
        var importScript = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", name));
        return importScript;
    }

    public static async Task<SceneInfo?> GetSceneInfoAsync(string blendFile)
    {
        var script = GetScript("blender_list_contents.py");
        var output = await ExecuteBlenderScriptAsync(blendFile, script, true);
        if (string.IsNullOrEmpty(output)) return null;
        var dumpStart = output.IndexOf("SCENE_INFO_BEGIN");
        var dumpEnd = output.IndexOf("SCENE_INFO_END");
        if (dumpStart == -1 || dumpEnd < dumpStart) return null;

        var jsonRange = output[(dumpStart + "SCENE_INFO_BEGIN".Length)..dumpEnd].Trim();
        if (jsonRange.TryDeserializeJson<SceneInfo>(out var info, out var err)) {
            return info;
        }
        Logger.Error("Failed to parse blender scene info: " + err);
        return null;
    }

    public static async Task<string?> ExecuteBlenderScriptAsync(string blendFile, string script, bool background, string? expectedOutputFilepath = null)
    {
        var blenderPath = AppConfig.Instance.BlenderPath.Get();
        if (string.IsNullOrEmpty(blenderPath)) {
            if (!_hasShownNoBlenderWarning) {
                Logger.Error("Can't import .blend files, Blender is not configured.");
                _hasShownNoBlenderWarning = true;
            }
            return null;
        }
        var baseArgs = background
            ? $"\"{blendFile}\" --background --python-expr \"{script.Replace("\"", "\\\"")}\""
            : $"\"{blendFile}\" --python-expr \"{script.Replace("\"", "\\\"")}\"";
        var (exe, args) = FileSystemUtils.FormatProcessParams(blenderPath, baseArgs);

        var process = new Process() { StartInfo = new ProcessStartInfo() {
            UseShellExecute = false,
            FileName = exe,
            WindowStyle = background ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }};

        if (cancellationTokenSource == null || cancellationTokenSource.IsCancellationRequested) {
            cancellationTokenSource = new();
        }

        var output = new StringBuilder();
        process!.OutputDataReceived += (sender, e) => {
            var data = e.Data;
            if (!string.IsNullOrWhiteSpace(data)) {
                output.AppendLine(data.Trim());
            }
        };
        process.ErrorDataReceived += (sender, e) => {
            var data = e.Data;
            if (!string.IsNullOrWhiteSpace(data)) {
                output.Append("ERROR:").AppendLine(data.Trim());
            }
        };

        var delay = Task.Delay(BlenderTimeoutMs, cancellationTokenSource.Token);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var exitTask = process!.WaitForExitAsync(cancellationTokenSource.Token).ContinueWith(t => {
            if (!t.IsCompletedSuccessfully) {
                Logger.Error("Blender script failed. Output:\n" + output.ToString());
                throw t.Exception ?? new Exception("Process failed");
            }
            if (expectedOutputFilepath != null && !File.Exists(expectedOutputFilepath)) {
                Logger.Error("Blender script failed, the expected file was not created. Output:\n" + output.ToString());
                throw t.Exception ?? new Exception("Process failed");
            }
            Logger.Debug("Blender output", output);
        });
        var completedTask = await Task.WhenAny(exitTask, delay);
        if (completedTask == delay) {
            cancellationTokenSource.Cancel();
            cancellationTokenSource = null;
        }
        return output.ToString();
    }
}

public class SceneInfo
{
    [JsonPropertyName("armatures")]
    public List<ArmatureInfo> Armatures { get; set; } = new();

    [JsonPropertyName("standalone_objects")]
    public List<ObjectInfo> StandaloneObjects { get; set; } = new();

    [JsonPropertyName("materials")]
    public List<string> Materials { get; set; } = new();
}

public class ArmatureInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("objects")]
    public List<ObjectInfo> Objects { get; set; } = new();
}

public class ObjectInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("materials")]
    public List<string> Materials { get; set; } = new();
}