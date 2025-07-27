using System.Numerics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ImGuiNET;

namespace ContentEditor.App;

public class ImguiTestWindow : IWindowHandler
{
    public string HandlerName => "Intro guide";

    public bool HasUnsavedChanges => false;
    public bool ShowHelp { get; set; }
    public int FixedID => -123123;

    private WindowData data = null!;
    protected UIContext context = null!;

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow() => OnIMGUI();
    public void OnIMGUI()
    {
        var show = true;
        ImGui.ShowDemoWindow(ref show);
        if (!show) {
            EditorWindow.CurrentWindow?.CloseSubwindow(this);
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}