using System.Globalization;
using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Themes;
using Hexa.NET.ImNodes;

namespace ContentEditor.App;

public class ThemeEditor : IWindowHandler
{
    public string HandlerName => "Themes";

    public bool HasUnsavedChanges => false;
    public bool ShowHelp { get; set; }
    public int FixedID => -123124;

    private WindowData data = null!;
    protected UIContext context = null!;

    private int tab;

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        ImGui.TextWrapped("Change any settings you wish in the below style editor section. You can use Tools > IMGUI test window menu option to preview most components. Once you're satisfied with your changes, press \"Save to file\" and store it in the styles folder.");

        if (ImGui.Button("Save to file ...")) {
            var themeData = DefaultThemes.GetCurrentStyleData() + "\n\n" + UI.GetImNodesThemeStyleData();

            var themePath = Path.Combine(AppContext.BaseDirectory, "styles");
            Directory.CreateDirectory(themePath);
            PlatformUtils.ShowSaveFileDialog((path) => {
                File.WriteAllText(path, themeData);
            }, initialFile: Path.GetFullPath(Path.Combine(themePath, "custom_theme.theme.txt")), filter: ".theme.txt|*.theme.txt");
        }

        ImGui.SeparatorText("Style editor");
        ImguiHelpers.Tabs(["Built-in", "Nodes", "Contextual"], ref tab, true);
        ImGui.BeginChild("Styles");
        if (tab == 0) {
            ImGui.ShowStyleEditor();
        } else if (tab == 1) {
            ShowNodesStyleEditor();
        } else {
            ShowContextualColorEditor();
        }
        ImGui.EndChild();
    }

    private ImNodesContextPtr? nodeCtx;
    private void ShowNodesStyleEditor()
    {
        if (nodeCtx == null) {
            nodeCtx = UI.InitImNodeContext();
            ImNodes.SetCurrentContext(nodeCtx.Value);
            ImNodes.SetNodeEditorSpacePos(0, new Vector2(50, 50));
            ImNodes.SetNodeEditorSpacePos(1, new Vector2(300, 80));
        } else {
            ImNodes.SetCurrentContext(nodeCtx.Value);
        }
        ImGui.SetNextWindowSize(new Vector2(Math.Min(ImGui.GetContentRegionAvail().X / 2, 600 * UI.UIScale), 0));
        ImGui.BeginChild("node colors");

        ImGui.BeginTabBar("Node Styles");
        var style = ImNodes.GetStyle();
        if (ImGui.BeginTabItem("Sizes")) {
            ImGui.SliderFloat(nameof(ImNodesStylePtr.LinkHoverDistance), ref style.LinkHoverDistance, 0, 32);
            ImGui.SliderFloat(nameof(ImNodesStylePtr.LinkLineSegmentsPerLength), ref style.LinkLineSegmentsPerLength, 0, 1);
            ImGui.SliderFloat(nameof(ImNodesStylePtr.LinkThickness), ref style.LinkThickness, 0, 32);

            ImGui.SliderFloat2(nameof(ImNodesStylePtr.MiniMapOffset), ref style.MiniMapOffset, 0, 128);
            ImGui.SliderFloat2(nameof(ImNodesStylePtr.MiniMapPadding), ref style.MiniMapPadding, 0, 32);

            ImGui.SliderFloat(nameof(ImNodesStylePtr.NodeBorderThickness), ref style.NodeBorderThickness, 0, 32);
            ImGui.SliderFloat(nameof(ImNodesStylePtr.NodeCornerRounding), ref style.NodeCornerRounding, 0, 32);
            ImGui.SliderFloat2(nameof(ImNodesStylePtr.NodePadding), ref style.NodePadding, 0, 32);

            ImGui.SliderFloat(nameof(ImNodesStylePtr.PinCircleRadius), ref style.PinCircleRadius, 0, 32);
            ImGui.SliderFloat(nameof(ImNodesStylePtr.PinHoverRadius), ref style.PinHoverRadius, 0, 32);
            ImGui.SliderFloat(nameof(ImNodesStylePtr.PinLineThickness), ref style.PinLineThickness, 0, 32);
            ImGui.SliderFloat(nameof(ImNodesStylePtr.PinOffset), ref style.PinOffset, 0, 32);
            ImGui.SliderFloat(nameof(ImNodesStylePtr.PinQuadSideLength), ref style.PinQuadSideLength, 0, 32);
            ImGui.SliderFloat(nameof(ImNodesStylePtr.PinTriangleSideLength), ref style.PinTriangleSideLength, 0, 32);

            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Colors")) {
            foreach (var col in Enum.GetValues<ImNodesCol>()) {
                if (col == ImNodesCol.Count) continue;

                var color = ImGui.ColorConvertU32ToFloat4(style.Colors[(int)col]);
                if (ImGui.ColorEdit4(col.ToString(), ref color)) {
                    style.Colors[(int)col] = ImGui.ColorConvertFloat4ToU32(color);
                }
            }
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();

        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("node preview");
        ImNodes.BeginNodeEditor();

        ImNodes.BeginNode(0);
            ImNodes.BeginNodeTitleBar(); ImGui.Text("Node Title 1"); ImNodes.EndNodeTitleBar();
            ImGui.Text("Sample text inside node");
            AppImguiHelpers.NodeSeparator(0);
            ImNodes.BeginInputAttribute(0);
            ImGui.Text("Input 1");
            ImNodes.EndInputAttribute();

            ImNodes.BeginOutputAttribute(0);
            using (var _ = ImguiHelpers.ScopedIndent(AppImguiHelpers.NodeContentAvailX(0) - ImGui.CalcTextSize("Output 1"u8).X)) {
                ImGui.Text("Output 1"u8);
            }
            ImNodes.EndOutputAttribute();

            foreach (var shape in Enum.GetValues<ImNodesPinShape>()) {
                var str = shape.ToString();
                ImNodes.BeginOutputAttribute(2 + (int)shape, shape);
                using (var _ = ImguiHelpers.ScopedIndent(AppImguiHelpers.NodeContentAvailX(0) - ImGui.CalcTextSize(str).X)) {
                    ImGui.Text(str);
                }
                ImNodes.EndOutputAttribute();
            }

        ImNodes.EndNode();

        ImNodes.BeginNode(1);
            ImNodes.BeginNodeTitleBar(); ImGui.Text("Node Title 2"); ImNodes.EndNodeTitleBar();
            ImGui.Text("More text");
            ImGui.Text("Line 2");
            AppImguiHelpers.NodeSeparator(1);

            ImNodes.BeginInputAttribute(1);
            ImGui.Text("Input 2");
            ImNodes.EndInputAttribute();

            ImNodes.BeginOutputAttribute(50);
            using (var _ = ImguiHelpers.ScopedIndent(AppImguiHelpers.NodeContentAvailX(1) - ImGui.CalcTextSize("Output 3"u8).X)) {
                ImGui.Text("Output 3"u8);
            }
            ImNodes.EndOutputAttribute();
        ImNodes.EndNode();

        ImNodes.Link(0, 0, 1);

        ImNodes.MiniMap(ImNodesMiniMapLocation.BottomRight);
        ImNodes.EndNodeEditor();

        ImGui.EndChild();
    }

    private void ShowContextualColorEditor()
    {
        foreach (var field in AppColors.ColorFields) {
            var col = (Vector4)field.GetValue(Colors.Current)!;
            if (ImGui.ColorEdit4(field.Name, ref col)) {
                UndoRedo.RecordCallbackSetter(null, field, (Vector4)field.GetValue(Colors.Current)!, col, (f, v) => f.SetValue(Colors.Current, v), field.Name);
            }
        }

        foreach (var field in AppColors.ColorFields) {
            var col = (Vector4)field.GetValue(Colors.Current)!;
            ImGui.TextColored(col, "Lorem ipsum dolor sit amet");
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}