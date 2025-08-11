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

    public JsonViewer(string json)
    {
        Json = json;
    }

    public JsonViewer(JsonNode json, string pathDescription)
    {
        JsonNode = json;
        this.pathDescription = pathDescription;
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

        ImGui.Checkbox("Show raw JSON data", ref showRaw);
        ImGui.SetNextItemWidth(ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X * 2);
        var height = Math.Max(ImGui.GetWindowSize().Y - ImGui.GetStyle().WindowPadding.Y - ImGui.GetCursorPosY(), 100);
        if (showRaw) {
            if (Json == null) {
                Json = JsonNode!.ToJsonString(new System.Text.Json.JsonSerializerOptions() {
                    WriteIndented = true
                });
            }
            var json = Json;
            ImguiHelpers.TextMultilineAutoResize("JSON", ref json, ImGui.CalcItemWidth(), height, UI.FontSize, maxLen: (uint)json.Length, ImGuiInputTextFlags.ReadOnly);
        } else {
            if (JsonNode == null) {
                JsonNode = JsonSerializer.Deserialize<JsonNode>(Json!)!;
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