using ContentEditor.App.Windowing;
using ContentPatcher;
using ImGuiNET;

namespace ContentEditor.App;

public static class UI
{
    public static void OpenWindow(ContentWorkspace? workspace)
    {
        MainLoop.Instance.OpenSubwindow(new EditorWindow(1, workspace));
    }

    public static int FontSize { get; set; } = 20;

    private static ushort[] PolishRanges = [
        0x0020, 0x00FF,
        0x0100, 0x017F,
        0
    ];

    public unsafe static void ConfigureImgui()
    {
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        var fonts = io.Fonts;
        fonts.Clear();

        ImFontConfig* fontCfg = ImGuiNative.ImFontConfig_ImFontConfig();
        ImFontConfigPtr custom_icons = new(fontCfg) {
            PixelSnapH = true,
        };

        fonts.AddFontFromFileTTF("fonts/NotoSansJP-Regular.ttf", (float)FontSize, custom_icons, fonts.GetGlyphRangesChineseFull());
        fontCfg->MergeMode = 1;
        fonts.AddFontFromFileTTF("fonts/NotoSans-Regular.ttf", (float)FontSize, custom_icons, fonts.GetGlyphRangesDefault());
        fonts.AddFontFromFileTTF("fonts/NotoSans-Regular.ttf", (float)FontSize, custom_icons, fonts.GetGlyphRangesCyrillic());
        fixed (ushort *ranges = PolishRanges) {
            fonts.AddFontFromFileTTF("fonts/NotoSans-Regular.ttf", (float)FontSize, custom_icons, (nint)ranges);
        }

        fonts.Build();
        ImGuiNative.ImFontConfig_destroy(fontCfg);
#if !DEBUG
        io.ConfigErrorRecoveryEnableAssert = false;
#endif
    }
}