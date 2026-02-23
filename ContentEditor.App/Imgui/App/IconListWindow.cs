using System.Numerics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;

namespace ContentEditor.App;

public class IconListWindow : IWindowHandler
{
    public string HandlerName => "Icon List";

    public bool HasUnsavedChanges => false;
    public bool ShowHelp { get; set; }
    public int FixedID => -123163;

    private WindowData data = null!;
    protected UIContext context = null!;
    private string filter = "";

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow() => this.ShowDefaultWindow(context);
    private static char[]? Icons;
    private static string[]? IconNames;
    private static char[][]? IconsMulti;
    private static string[]? IconNamesMulti;

    private Vector4[] ColorSet = [Colors.IconPrimary, Colors.IconSecondary, Colors.IconTertiary, Colors.IconSecondary, Colors.IconPrimary, Colors.IconTertiary,Colors.IconPrimary, Colors.IconSecondary];

    public void OnIMGUI()
    {
        if (Icons == null || IconNames == null) {
            var iconFields = typeof(AppIcons)
                .GetFields(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public).Where(f => f.FieldType == typeof(char));
            Icons = iconFields.Select(f => (char)f.GetValue(null)!).ToArray();
            IconNames = iconFields.Select(f => f.Name).ToArray();
        }
        if (IconsMulti == null || IconNamesMulti == null) {
            var iconFields = typeof(AppIcons)
                .GetFields(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public).Where(f => f.FieldType == typeof(char[]));
            IconsMulti = iconFields.Select(f => (char[])f.GetValue(null)!).ToArray();
            IconNamesMulti = iconFields.Select(f => f.Name).ToArray();
        }

        ImGui.InputText("Filter", ref filter, 30);
        var count = 0;
        for (int i = 0; i < Icons.Length; i++) {
            var name = IconNames[i];
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.InvariantCultureIgnoreCase)) continue;
            if (count++%20 != 0) ImGui.SameLine();
            if (ImGui.Button(Icons[i].ToString())) {
                EditorWindow.CurrentWindow?.CopyToClipboard(name);
            }
            ImguiHelpers.Tooltip(name);
        }

        ImGui.SeparatorText("Multi-color");
        count = 0;
        for (int i = 0; i < IconsMulti.Length; i++) {
            var name = IconNamesMulti[i];
            if (!string.IsNullOrEmpty(filter) && !name.Contains(filter, StringComparison.InvariantCultureIgnoreCase)) continue;
            if (count++%20 != 0) ImGui.SameLine();
            var icons = IconsMulti[i];
            if (ImguiHelpers.ButtonMultiColor(icons, ColorSet)) {
                EditorWindow.CurrentWindow?.CopyToClipboard(name);
            }
            ImguiHelpers.Tooltip(name);
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}