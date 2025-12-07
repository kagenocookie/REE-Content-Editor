using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentEditor.Editor;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App;

public class TextViewer : IWindowHandler
{
    public string HandlerName => " Text Viewer ";

    public bool HasUnsavedChanges => false;
    public string? Text { get; private set; }
    public string Subtitle { get; }
    public bool ReadOnly { get; }

    private UIContext context = null!;

    public TextViewer(string text, string subtitle, bool readOnly)
    {
        Text = text;
        Subtitle = subtitle;
        ReadOnly = readOnly;
    }

    public TextViewer(Stream stream, string subtitle, bool readOnly)
    {
        Text = new StreamReader(stream).ReadToEnd();
        Subtitle = subtitle;
        ReadOnly = readOnly;
    }

    public void Init(UIContext context)
    {
        this.context = context;
    }

    public void OnIMGUI()
    {
        if (!string.IsNullOrEmpty(Subtitle)) {
            ImGui.Text(Subtitle);
        }

        ImGui.SetNextItemWidth(ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X * 2);
        var height = Math.Max(ImGui.GetWindowSize().Y - ImGui.GetStyle().WindowPadding.Y - ImGui.GetCursorPosY(), 100);
        if (!ReadOnly) {
            if (ImGui.Button("Save as ...")) {
                PlatformUtils.ShowSaveFileDialog((path) => {
                    File.WriteAllText(path, Text);
                });
            }
        }

        var text = Text ?? "";
        var maxlen = (uint)(ReadOnly ? text.Length : text.Length + 1000);
        if (ImguiHelpers.TextMultilineAutoResize("Content", ref text, ImGui.CalcItemWidth(), height, UI.FontSize, maxLen: maxlen, ReadOnly ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None)) {
            Text = text;
        }
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    public bool RequestClose()
    {
        return false;
    }
}