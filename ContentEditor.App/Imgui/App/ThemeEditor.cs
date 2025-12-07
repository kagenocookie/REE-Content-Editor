using System.Globalization;
using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Themes;

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
            var themeData = DefaultThemes.GetCurrentStyleData();

            var themePath = Path.Combine(AppContext.BaseDirectory, "styles");
            Directory.CreateDirectory(themePath);
            PlatformUtils.ShowSaveFileDialog((path) => {
                File.WriteAllText(path, themeData);
            }, initialFile: Path.GetFullPath(Path.Combine(themePath, "custom_theme.theme.txt")), filter: ".theme.txt|*.theme.txt");
        }

        ImGui.SeparatorText("Style editor");
        ImguiHelpers.Tabs(["Built-in", "Contextual"], ref tab, true);
        ImGui.BeginChild("Styles");
        if (tab == 0) {
            ImGui.ShowStyleEditor();
        } else {
            ShowContextualColorEditor();
        }
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