using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.via;

namespace ContentEditor.App.DD2;

[RszContextAction("")]
public static class SpawnAdditions
{
    private class OpenLayersFeatureItem
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "featurelist";

        [JsonPropertyName("features")]
        public List<OpenLayersPointFeature> Features { get; set; } = new();

        [JsonPropertyName("style")]
        public OpenLayersStyle Style { get; set; } = new();
    }

    private class OpenLayersPointFeature
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "point";

        [JsonPropertyName("position")]
        public float[] Position { get; set; } = [];
    }

    private class OpenLayersStyle
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "circle";

        [JsonPropertyName("radius")]
        public float Radius { get; set; } = 5f;

        [JsonPropertyName("fillColor")]
        public string FillColor { get; set; } = "#ff0000";
    }

    [RszContextAction("app.GenerateRowData", "dd2")]
    [RszContextAction("app.GenerateRowData[]", "dd2")]
    public static bool ShowSpawnCopyMethod(UIContext context)
    {
        if (ImGui.Button("Copy to featurelist json")) {
            var data = new OpenLayersFeatureItem();
            if (context.TryCast<RszInstance>(out var instance)) {
                if (instance.RszClass.name == "app.GenerateRowData") {
                    var pos = (Position)instance.GetFieldValue("_ManualPos")!;
                    data.Features.Add(new OpenLayersPointFeature() {
                        Position = [(float)pos.x, (float)pos.z]
                    });
                }
            } else if (context.TryCast<List<object>>(out var list) && list.Count > 0) {
                foreach (var item in list) {
                    if (item is RszInstance inst) {
                        var pos = (Position)inst.GetFieldValue("_ManualPos")!;
                        data.Features.Add(new OpenLayersPointFeature() {
                            Position = [(float)pos.x, (float)pos.z]
                        });
                    }
                }
            }
            if (data.Features.Count > 0) {
                EditorWindow.CurrentWindow?.CopyToClipboard(JsonSerializer.Serialize(data, JsonConfig.jsonOptionsIncludeFields), "Copied!");
            } else {
                Logger.Warn("No spawn position data found");
            }
            return true;
        }
        return false;
    }
}

/*
{
    "type": "featurelist",
    "features": [
        {
            "type": "point",
            "position": [10, 555]
        },
        {
            "type": "point",
            "position": [77, 255]
        }
    ],
    "style": {
        "type": "circle",
        "radius": 15,
        "fillColor": "red"
    }
}
*/