using ContentEditor.App.Windowing;
using ContentEditor.Themes;
using ContentPatcher;
using ContentPatcher.FileFormats;
using ImGuiNET;

namespace ContentEditor.App;

public static class UI
{
    private static int NextWindowId = 1;

    public static EditorWindow OpenWindow(ContentWorkspace? workspace)
    {
        var window = new EditorWindow(NextWindowId++, workspace);
        MainLoop.Instance.OpenNewWindow(window);
        return window;
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
        fixed (ushort* ranges = PolishRanges) {
            fonts.AddFontFromFileTTF("fonts/NotoSans-Regular.ttf", (float)FontSize, custom_icons, (nint)ranges);
        }

        fonts.Build();
        ImGuiNative.ImFontConfig_destroy(fontCfg);
#if !DEBUG
        io.ConfigErrorRecoveryEnableAssert = false;
#endif
    }

    public unsafe static void ApplyTheme(string theme)
    {
        if (DefaultThemes.Themes.TryGetValue(theme, out var themeCallback)) {
            themeCallback.Invoke();
            return;
        }

        var themePath = Path.Combine("styles", theme + ".theme.txt");
        if (!File.Exists(themePath)) {
            Logger.Error("Theme not found: " + theme);
            DefaultThemes.Themes["default"].Invoke();
            return;
        }

        var ini = IniFile.ReadFile(themePath);
        DefaultThemes.ApplyThemeData(ini);
    }
}