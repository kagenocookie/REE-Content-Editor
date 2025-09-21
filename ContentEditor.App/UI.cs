using ContentEditor.App.ImguiHandling;
using ContentEditor.App.ImguiHandling.Efx;
using ContentEditor.App.Windowing;
using ContentEditor.Themes;
using ContentPatcher;
using ContentPatcher.FileFormats;
using ImGuiNET;
using ReeLib;
using ReeLib.Efx;

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

        fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/NotoSansJP-Regular.ttf"), (float)FontSize, custom_icons, fonts.GetGlyphRangesChineseFull());
        fontCfg->MergeMode = 1;
        fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/NotoSans-Regular.ttf"), (float)FontSize, custom_icons, fonts.GetGlyphRangesDefault());
        fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/NotoSans-Regular.ttf"), (float)FontSize, custom_icons, fonts.GetGlyphRangesCyrillic());
        fixed (ushort* ranges = PolishRanges) {
            fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/NotoSans-Regular.ttf"), (float)FontSize, custom_icons, (nint)ranges);
        }
        fixed (ushort* ranges = AppIcons.Range) {
            fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/appicons.ttf"), (float)FontSize, custom_icons, (nint)ranges);
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

        var themePath = Path.Combine(AppContext.BaseDirectory, "styles", theme + ".theme.txt");
        if (!File.Exists(themePath)) {
            Logger.Error("Theme not found: " + theme);
            DefaultThemes.Themes["default"].Invoke();
            return;
        }

        var ini = IniFile.ReadFile(themePath);
        DefaultThemes.ApplyThemeData(ini);
    }
}

public static class AppIcons
{
    public static readonly char EfxEntry = '\ue900';
    public static readonly char EfxAction = '\ue901';
    public static readonly char Prefab = '\ue902';
    public static readonly char Efx = '\ue903';
    public static readonly char Folder = '\ue905';
    public static readonly char FolderLink = '\ue907';
    public static readonly char GameObject = '\ue908';
    public static readonly char Mesh = '\ue909';
    public static readonly char Clip = '\ue90a';
    public static readonly char List = '\ue90b';
    public static readonly char Pencil = '\ue906';
    public static readonly char Image = '\ue90d';
    public static readonly char Copy = '\ue92c';
    public static readonly char Paste = '\ue92d';
    public static readonly char Tag = '\ue935';
    public static readonly char Tags = '\ue936';
    public static readonly char Pin = '\ue946';
    public static readonly char Download = '\ue960';
    public static readonly char Upload = '\ue961';
    public static readonly char Spinner = '\ue984';
    public static readonly char Search = '\ue986';
    public static readonly char ZoomIn = '\ue987';
    public static readonly char ZoomOut = '\ue988';
    public static readonly char Enlarge = '\ue989';
    public static readonly char Shrink = '\ue98a';
    public static readonly char Lock = '\ue98f';
    public static readonly char Unlock = '\ue990';
    public static readonly char Sliders = '\ue992';
    public static readonly char Tree = '\ue9bc';
    public static readonly char Eye = '\ue9ce';
    public static readonly char EyeBlocked = '\ue9d1';
    public static readonly char Bookmark = '\ue9d2';
    public static readonly char Bookmarks = '\ue9d3';
    public static readonly char StarEmpty = '\ue9d7';
    public static readonly char Star = '\ue9d9';
    public static readonly char Play = '\uea1c';
    public static readonly char Pause = '\uea1d';
    public static readonly char Stop = '\uea1e';
    public static readonly char SeekStart = '\uea21';
    public static readonly char Loop = '\uea2d';

    public static readonly ushort[] Range = [(ushort)EfxEntry, (ushort)Loop, 0];

    public static string PrependIcon(this string text, object target)
    {
        var icon = GetIcon(target);
        if (icon == '\0') return text;
        return $"{icon} {text}";
    }

    public static string PrependIcon(this object target, string text)
    {
        var icon = GetIcon(target);
        if (icon == '\0') return text;
        return icon + text;
    }

    public static char GetIcon(object target, object fallback)
    {
        var icon = GetIcon(target);
        if (icon == '\0') icon = GetIcon(fallback);
        return icon;
    }

    public static char GetIcon(object target) => target switch {
        GameObject go => string.IsNullOrEmpty(go.PrefabPath) ? GameObject : Prefab,
        Folder scn => string.IsNullOrEmpty(scn.ScenePath) ? Folder : FolderLink,
        PrefabEditor => Prefab,
        SceneEditor => Folder,
        EfxEditor => Efx,
        EFXAction => EfxAction,
        EFXEntry => EfxEntry,
        _ => '\0',
    };
}