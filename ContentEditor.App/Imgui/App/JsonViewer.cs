using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App;

public class JsonViewer : IWindowHandler
{
    public string HandlerName => "Json viewer";

    public bool HasUnsavedChanges => false;
    public string? Json { get; private set; }
    public JsonNode? JsonNode { get; private set; }
    private string? jsonPathList;

    private bool showRaw;

    private UIContext context = null!;
    private readonly string? pathDescription;

    private FileHandle? targetFile;

    private static readonly JsonSerializerOptions jsonOptions = new System.Text.Json.JsonSerializerOptions() {
        WriteIndented = true
    };

    public JsonViewer(string json)
    {
        Json = json;
    }

    public JsonViewer(Stream jsonStream)
    {
        var reader = new StreamReader(jsonStream);
        Json = reader.ReadToEnd();
    }

    public JsonViewer(JsonNode json, string pathDescription, FileHandle? handle = null)
    {
        JsonNode = json;
        this.pathDescription = pathDescription;
        this.targetFile = handle;
    }

    public void SetTargetFile(FileHandle file)
    {
        targetFile = file;
    }

    public void Init(UIContext context)
    {
        this.context = context;
    }

    public void OnIMGUI()
    {
        if (pathDescription != null) {
            ImGui.Text(pathDescription);
        }

        if (targetFile != null) {
            ImGui.TextColored(Colors.Faded, targetFile.Filepath);
            if ((!string.IsNullOrEmpty(Json) || JsonNode != null) && targetFile.DiffHandler != null && targetFile.HandleType is FileHandleType.Bundle or FileHandleType.Disk or FileHandleType.LooseFile) {
                Json ??= JsonNode!.ToJsonString(jsonOptions);
                if (ImGui.Button("Apply JSON patch")) {
                    if (Json.TryDeserializeJson<JsonNode>(out var newJsonNoo, out var error)) {
                        try {
                            targetFile.DiffHandler.ApplyDiff(targetFile, newJsonNoo);
                            targetFile.Modified = true;

                            EditorWindow.CurrentWindow?.Overlays.ShowTooltip("Patch applied.\nIn case of UI issues, re-open the existing file's editor.", 3f);
                            JsonNode = targetFile.DiffHandler.FindDiff(targetFile);
                            if (JsonNode == null) JsonNode = new JsonObject();
                            Json = JsonNode?.ToJsonString(jsonOptions);
                            jsonPathList = null;
                        } catch (Exception e) {
                            Logger.Error("Failed to apply patch: " + e.Message);
                        }
                    } else {
                        Logger.Error("Invalid JSON: " + error);
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Load patch from file ...")) {
                    PlatformUtils.ShowFileDialog((files) => {
                        if (files[0].TryDeserializeJsonFile<JsonNode>(out var newJson, out var error)) {
                            JsonNode = newJson;
                            Json = null;
                            jsonPathList = null;
                        } else {
                            Logger.Error("Failed to load JSON file: " + error);
                        }
                    }, null, "JSON (*.json)|*.json");
                }
                ImGui.SameLine();
                if (ImGui.Button("Compare to file ...")) {
                    var wnd = EditorWindow.CurrentWindow;
                    PlatformUtils.ShowFileDialog((files) => {
                        var file = files[0];
                        FileHandle? handle = null;
                        try {
                            var ws = context.GetWorkspace();
                            if (true == ws?.ResourceManager.TryLoadUniqueFile(file, out handle)) {
                                if (handle.Loader != targetFile.Loader) {
                                    Logger.Error($"File format {handle.Format} does not match the source file {targetFile.Format}!");
                                } else {
                                    var diff = handle.DiffHandler!.FindDiff(targetFile);
                                    if (diff == null) {
                                        wnd?.InvokeFromUIThread(() => wnd?.Overlays.ShowTooltip("No difference could be found", 3f));
                                    } else {
                                        JsonNode = diff;
                                        Json = null;
                                        jsonPathList = null;
                                    }
                                }
                            }
                        } catch (Exception e) {
                            Logger.Error("Failed to compare files: " + e.Message);
                        } finally {
                            handle?.Dispose();
                        }
                    });
                }
            }
        }

        ImGui.Checkbox("Show raw JSON data", ref showRaw);
        ImGui.SetNextItemWidth(ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X * 2);
        var height = Math.Max(ImGui.GetWindowSize().Y - ImGui.GetStyle().WindowPadding.Y - ImGui.GetCursorPosY(), 100);
        if (showRaw) {
            if (Json == null) {
                Json = JsonNode!.ToJsonString(jsonOptions);
            }
            var json = Json;
            var maxlen = (uint)(targetFile == null ? json.Length : json.Length + 1000);
            if (ImguiHelpers.TextMultilineAutoResize("JSON", ref json, ImGui.CalcItemWidth(), height, UI.FontSize, maxLen: maxlen, targetFile == null ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None)) {
                JsonNode = null;
                Json = json;
            }
        } else {
            if (JsonNode == null) {
                try {
                    JsonNode = JsonSerializer.Deserialize<JsonNode>(Json!)!;
                } catch (Exception e) {
                    Logger.Error("Invalid JSON: " + e.Message);
                    JsonNode = new JsonObject();
                }
            }
            if (jsonPathList == null) {
                jsonPathList = DiffHandler.GetDiffTree(JsonNode);
            }

            ImguiHelpers.TextMultilineAutoResize("JSON", ref jsonPathList, ImGui.CalcItemWidth(), height, UI.FontSize, maxLen: (uint)jsonPathList.Length, ImGuiInputTextFlags.ReadOnly);
        }
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    public bool RequestClose()
    {
        return false;
    }
}