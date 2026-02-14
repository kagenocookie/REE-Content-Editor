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

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow() => this.ShowDefaultWindow(context);
    private static char[]? Icons;
    private static string[]? IconNames;
    public void OnIMGUI()
    {
        if (Icons == null || IconNames == null) {
            var iconFields = typeof(AppIcons)
                .GetFields(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public).Where(f => f.FieldType == typeof(char));
            Icons = iconFields.Select(f => (char)f.GetValue(null)!).ToArray();
            IconNames = iconFields.Select(f => f.Name).ToArray();
        }

        for (int i = 0; i < Icons.Length; i++) {
            if (i%20 != 0) ImGui.SameLine();
            if (ImGui.Button(Icons[i].ToString())) {
                EditorWindow.CurrentWindow?.CopyToClipboard(IconNames[i]);
            }
            ImguiHelpers.Tooltip(IconNames[i]);
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}