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

    public JsonViewer(string json)
    {
        Json = json;
    }

    public JsonViewer(JsonNode json)
    {
        JsonNode = json;
    }

    private UIContext context = null!;
    public void Init(UIContext context)
    {
        this.context = context;
    }

    public void OnIMGUI()
    {
        ImGui.Checkbox("Show raw JSON data", ref showRaw);
        if (showRaw) {
            if (Json == null) {
                Json = JsonNode!.ToJsonString(new System.Text.Json.JsonSerializerOptions() {
                    WriteIndented = true
                });
            }
            var json = Json;
            ImguiHelpers.TextMultilineAutoResize("JSON", ref json, ImGui.CalcItemWidth(), 600, UI.FontSize, maxLen: (uint)json.Length, ImGuiInputTextFlags.ReadOnly);
        } else {
            if (JsonNode == null) {
                JsonNode = JsonSerializer.Deserialize<JsonNode>(Json!)!;
            }
            if (jsonPathList == null) {
                jsonPathList = DiffHandler.GetDiffTree(JsonNode);
            }

            ImguiHelpers.TextMultilineAutoResize("JSON", ref jsonPathList, ImGui.CalcItemWidth(), 600, UI.FontSize, maxLen: (uint)jsonPathList.Length, ImGuiInputTextFlags.ReadOnly);
        }
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    public bool RequestClose()
    {
        return false;
    }
}