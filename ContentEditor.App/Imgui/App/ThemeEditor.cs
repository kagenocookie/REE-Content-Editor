using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Themes;
using Hexa.NET.ImNodes;
using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace ContentEditor.App;

public class ThemeEditor : IWindowHandler
{
    public string HandlerName => "Theme Editor";

    public bool HasUnsavedChanges => false;
    public bool ShowHelp { get; set; }
    public int FixedID => -123124;

    private WindowData data = null!;
    protected UIContext context = null!;

    private int tab;
    private bool isColorSortingDone = false;
    private enum StyleGroupID
    {
        General,
        Icons_App,
        Icons_FileType,
        Tags,
    }
    private StyleGroupID selectedTabIDX = StyleGroupID.General;
    private List<(string Name, StyleGroupID ID, List<FieldInfo> Fields)> tabs = new()
    {
        ("General", StyleGroupID.General, new()),
        ("App Icons", StyleGroupID.Icons_App, new()),
        ("File Type Icons", StyleGroupID.Icons_FileType, new()),
        ("Tags", StyleGroupID.Tags, new()),
    };
    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        ImGui.Button($"{AppIcons.SI_GenericInfo}");
        ImguiHelpers.Tooltip("Change any settings you wish in the below style editor section. You can use the IMGUI Test Window to preview most components.\nOnce you're satisfied with your changes, press \"Save to file\" and store it in the styles folder.");
        ImGui.SameLine();
        if (ImGui.Button("Save to file ...")) {
            var themeData = DefaultThemes.GetCurrentStyleData() + "\n\n" + UI.GetImNodesThemeStyleData();

            var themePath = Path.Combine(AppContext.BaseDirectory, "styles");
            Directory.CreateDirectory(themePath);
            PlatformUtils.ShowSaveFileDialog((path) => {
                File.WriteAllText(path, themeData);
            }, initialFile: Path.GetFullPath(Path.Combine(themePath, "custom_theme.theme.txt")), filter: ".theme.txt|*.theme.txt");
        }
        ImGui.SameLine();
        if (ImGui.Button("Open IMGUI Test Window")) {
            EditorWindow.CurrentWindow?.AddUniqueSubwindow(new ImguiTestWindow());
        }
        
        ImGui.SeparatorText("Style Editor");
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
        ImGui.Spacing();
        ImGui.Spacing();
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

    public void ShowContextualColorEditor()
    {
        if (!isColorSortingDone) { SortColorsByName(); }
        ImGui.Spacing();
        ImGui.Spacing();
        if (ImGui.BeginTabBar("ColorEditorTabs")) {
            foreach (var tab in tabs) {
                var flags = (selectedTabIDX == tab.ID) ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;

                if (ImGui.BeginTabItem(tab.Name, flags)) {
                    switch (tab.ID) {
                        case StyleGroupID.General:
                            ShowColorFields(tab.Fields);
                            break;
                        case StyleGroupID.Icons_App:
                            ShowColorFields(tab.Fields);
                            break;
                        case StyleGroupID.Icons_FileType:
                            ShowColorFields(tab.Fields);
                            break;
                        case StyleGroupID.Tags:
                            ShowColorFields(tab.Fields);
                            break;
                        default:
                            ImGui.Text("Lorem Ipsum");
                            break;
                    }
                    ImGui.EndTabItem();
                }

                if (ImGui.IsItemClicked()) {
                    selectedTabIDX = tab.ID;
                }
            }
            ImGui.EndTabBar();
        }
    }

    private void SortColorsByName()
    {
        foreach (var field in AppColors.ColorFields) {
            var tabId = field.Name switch {
                var name when name.StartsWith("Icon", StringComparison.OrdinalIgnoreCase) => StyleGroupID.Icons_App,
                var name when name.StartsWith("FileType", StringComparison.OrdinalIgnoreCase) => StyleGroupID.Icons_FileType,
                var name when name.StartsWith("Tag", StringComparison.OrdinalIgnoreCase) => StyleGroupID.Tags,
                _ => StyleGroupID.General
            };
            tabs.Find(x => x.ID == tabId).Fields.Add(field);
        }
        isColorSortingDone = true;
    }

    private void ShowColorFields(List<FieldInfo> fields)
    {
        ImGui.BeginChild("##ColorFields");
        ImGui.Spacing();
        foreach (var f in fields) { ShowColorEditorField(f); }
        ImGui.Spacing();
        ImGui.EndChild();
    }

    private static void ShowColorEditorField(FieldInfo field)
    {
        var col = (Vector4)field.GetValue(Colors.Current)!;
        string id = $"ColorEditorBox_{field.Name}";

        ImGui.PushStyleColor(ImGuiCol.Border, col);
        Vector2 size = new Vector2(0, ImGui.GetTextLineHeightWithSpacing() * 3.0f);

        if (ImGui.BeginChild(id, size, ImGuiChildFlags.Borders)) {
            if (ImGui.ColorEdit4(field.Name, ref col)) {
                UndoRedo.RecordCallbackSetter(null, field, (Vector4)field.GetValue(Colors.Current)!, col, (f, v) => f.SetValue(Colors.Current, v), field.Name);
            }

            ImGui.Spacing();
            ImGui.TextColored(col, $"Lorem ipsum dolor sit amet | {AppIcons.SI_GenericMagnifyingGlass} | {AppIcons.SI_GenericQmark}");
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.Spacing();
    }

    public bool RequestClose()
    {
        return false;
    }
}
