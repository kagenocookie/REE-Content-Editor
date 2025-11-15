using System.Numerics;
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
    public static int FontSizeLarge { get; set; } = 60;

    public static ImFontPtr LargeIconFont { get; private set; }

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

        ImFontConfigPtr normalIcons = new(ImGuiNative.ImFontConfig_ImFontConfig()) {
            PixelSnapH = true,
        };

        fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/NotoSansJP-Regular.ttf"), (float)FontSize, normalIcons, fonts.GetGlyphRangesChineseFull());
        normalIcons.MergeMode = true;
        fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/NotoSans-Regular.ttf"), (float)FontSize, normalIcons, fonts.GetGlyphRangesDefault());
        fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/NotoSans-Regular.ttf"), (float)FontSize, normalIcons, fonts.GetGlyphRangesCyrillic());
        fixed (ushort* ranges = PolishRanges) {
            fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/NotoSans-Regular.ttf"), (float)FontSize, normalIcons, (nint)ranges);
        }
        fixed (ushort* ranges = AppIcons.Range) {
            fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/appicons.ttf"), (float)FontSize, normalIcons, (nint)ranges);
        }
        fixed (ushort* ranges = AppIcons.Range) {
            fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/silver_icons.ttf"), (float)FontSize, normalIcons, (nint)ranges);
        }

        ImFontConfigPtr largeIcons = new(ImGuiNative.ImFontConfig_ImFontConfig()) {
            PixelSnapH = true,
        };

        largeIcons.MergeMode = false;
        fixed (ushort* ranges = AppIcons.Range) {
            LargeIconFont = fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/appicons.ttf"), (float)FontSizeLarge, largeIcons, (nint)ranges);
        }
        largeIcons.MergeMode = true;
        fixed (ushort* ranges = AppIcons.Range) {
            fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/silver_icons.ttf"), (float)FontSizeLarge, largeIcons, (nint)ranges);
        }

        fonts.Build();
        ImGuiNative.ImFontConfig_destroy(normalIcons);
        ImGuiNative.ImFontConfig_destroy(largeIcons);
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
    public static readonly char Previous = '\uea23';
    public static readonly char Next = '\uea24';
    public static readonly char Loop = '\uea2d';
    public static readonly char SI_File = '\ued00';
    public static readonly char SI_FileNew = '\ued01';
    public static readonly char SI_FileRead = '\ued02';
    public static readonly char SI_FileExtractTo = '\ued03';
    public static readonly char SI_GenericWarning = '\ued04';
    public static readonly char SI_GenericError = '\ued05';
    public static readonly char SI_GenericInfo = '\ued06';
    public static readonly char SI_GenericQmark = '\ued07';
    public static readonly char SI_ArchiveExtractTo = '\ued08';
    public static readonly char SI_GenericTag = '\ued09';
    public static readonly char SI_Bookmark = '\ued0a';
    public static readonly char SI_BookmarkAdd = '\ued0b';
    public static readonly char SI_BookmarkRemove = '\ued0c';
    public static readonly char SI_Bookmarks = '\ued0d';
    public static readonly char SI_BookmarkClear = '\ued0e';
    public static readonly char SI_Reset = '\ued0f';
    public static readonly char SI_ResetCamera = '\ued10';
    public static readonly char SI_ResetMaterial = '\ued11';
    public static readonly char SI_ObjectMove = '\ued12';
    public static readonly char SI_ObjectScale = '\ued13';
    public static readonly char SI_ObjectRotate = '\ued14';
    public static readonly char SI_FileJumpTo = '\ued15';
    public static readonly char SI_WindowOpenNew = '\ued16';
    public static readonly char SI_FileCopyPath = '\ued17';
    public static readonly char SI_BookmarkHide = '\ued18';
    public static readonly char SI_Filter = '\ued19';
    public static readonly char SI_FilterClear = '\ued1a';
    public static readonly char SI_FileOpenPreview = '\ued1b';
    public static readonly char SI_ViewEnabled = '\ued1c';
    public static readonly char SI_ViewDisabled = '\ued1d';
    public static readonly char SI_PathShort = '\ued1e';
    public static readonly char SI_ViewList = '\ued1f';
    public static readonly char SI_ViewGridBig = '\ued20';
    public static readonly char SI_ViewGridSmall = '\ued21';
    public static readonly char SI_FileType_MDF = '\ued22';
    public static readonly char SI_FileType_MMTR = '\ued23';
    public static readonly char SI_FileType_TEX = '\ued24';
    public static readonly char SI_FileType_UVS = '\ued25';
    public static readonly char SI_FileType_USER = '\ued26';
    public static readonly char SI_FileType_SCN = '\ued2a';
    public static readonly char SI_FileType_MESH = '\ued2b';
    public static readonly char SI_FileType_PFB = '\ued2c';
    public static readonly char SI_FileType_MCOL = '\ued2d';
    public static readonly char SI_FileType_RCOL = '\ued2e';
    public static readonly char SI_FileType_COCO = '\ued2f';
    public static readonly char SI_GenericMagnifyingGlass = '\ued30';
    public static readonly char SI_SceneGameObject = '\ued31';
    public static readonly char SI_SceneGameObject2 = '\ued32';
    public static readonly char SI_SceneGameObject3 = '\ued33';
    public static readonly char SI_SceneParentGameObject = '\ued34';
    public static readonly char SI_SceneBoundingBox = '\ued35';
    public static readonly char SI_FileType_UVAR = '\ued36';
    public static readonly char SI_FileType_MOT = '\ued37';
    public static readonly char SI_FileType_MOTTREE = '\ued38';
    public static readonly char SI_FileType_MOTLIST = '\ued39';
    public static readonly char SI_FileType_MOTBANK = '\ued3a';
    public static readonly char SI_FileType_GPUMOTLIST = '\ued3b';
    public static readonly char SI_FileType_CFIL = '\ued3c';
    public static readonly char SI_FileType_CDEF = '\ued3d';
    public static readonly char SI_FileType_CMAT = '\ued3e';
    public static readonly char SI_FileType_FOL = '\ued3f';
    public static readonly char SI_FileType_GPBF = '\ued40';
    public static readonly char SI_FileType_GPUC = '\ued41';
    public static readonly char SI_FileType_RTEX = '\ued42';
    public static readonly char SI_FileType_EFX = '\ued43';
    public static readonly char SI_FileType_TML = '\ued44';
    public static readonly char SI_FileType_CLIP = '\ued45';
    public static readonly char SI_FileType_SceneEffect = '\ued46';
    public static readonly char SI_FileType_GUI = '\ued47';
    public static readonly char SI_FileType_GCP = '\ued48';
    public static readonly char SI_FileType_GSTY = '\ued49';
    public static readonly char SI_FileType_GCF = '\ued4a';
    public static readonly char SI_FileType_FSM = '\ued4b';
    public static readonly char SI_FileType_MOTFSM = '\ued4c';
    public static readonly char SI_FileType_TMLFSM2 = '\ued4d';
    public static readonly char SI_FileType_CHAIN = '\ued4e';
    public static readonly char SI_FileType_CHAIN2 = '\ued4f';
    public static readonly char SI_FileType_UCURVE = '\ued50';
    public static readonly char SI_FileType_UCURVELIST = '\ued51';
    public static readonly char SI_SceneGameObject4 = '\ued52';
    public static readonly char SI_Generic3Axis = '\ued53';
    public static readonly char SI_GenericIO = '\ued54';
    public static readonly char SI_GenericExport = '\ued55';
    public static readonly char SI_GenericImport = '\ued56';
    public static readonly char SI_GenericConvert = '\ued57';
    public static readonly char SI_GenericCamera = '\ued58';
    public static readonly char SI_MeshViewerMeshGroup = '\ued59';
    public static readonly char SI_BookmarkCustomHide = '\ued5a';
    public static readonly char SI_BookmarkCustomClear = '\ued5b';
    public static readonly char SI_Update = '\ued5c';
    public static readonly char SI_UpdateTexture = '\ued5d';
    public static readonly char SI_TagDLC = '\ued5e';
    public static readonly char SI_TagItem = '\ued5f';
    public static readonly char SI_TagWeapon = '\ued60';
    public static readonly char SI_TagCharacter = '\ued61';
    public static readonly char SI_GenericMatchCase = '\ued62';

    public static readonly ushort[] Range = [(ushort)EfxEntry, (ushort)SI_GenericMatchCase, 0];

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
    public static (char icon, Vector4 color) GetIcon(KnownFileFormats format) => format switch {
        KnownFileFormats.Prefab => (SI_FileType_PFB, Colors.PFB),
        KnownFileFormats.Scene => (SI_FileType_SCN, Colors.SCN),
        KnownFileFormats.Mesh => (SI_FileType_MESH, Colors.MESH),
        KnownFileFormats.CollisionMesh => (SI_FileType_MCOL, Colors.MCOL),
        KnownFileFormats.RequestSetCollider => (SI_FileType_RCOL, Colors.RCOL),
        KnownFileFormats.MaterialDefinition => (SI_FileType_MDF, Colors.MDF),
        KnownFileFormats.MasterMaterial => (SI_FileType_MMTR, Colors.MDF),
        KnownFileFormats.Texture => (SI_FileType_TEX, Vector4.One),
        KnownFileFormats.UVSequence => (SI_FileType_UVS, Vector4.One),
        KnownFileFormats.UserData => (SI_FileType_USER, Vector4.One),
        KnownFileFormats.UserVariables => (SI_FileType_UVAR, Vector4.One),
        KnownFileFormats.CompositeCollision => (SI_FileType_COCO, Vector4.One),
        KnownFileFormats.Motion => (SI_FileType_MOT, Vector4.One),
        KnownFileFormats.MotionTree => (SI_FileType_MOTTREE, Vector4.One),
        KnownFileFormats.MotionList => (SI_FileType_MOTLIST, Vector4.One),
        KnownFileFormats.MotionBank => (SI_FileType_MOTBANK, Vector4.One),
        KnownFileFormats.GpuMotionList => (SI_FileType_GPUMOTLIST, Vector4.One),
        KnownFileFormats.CollisionFilter => (SI_FileType_CFIL, Vector4.One),
        KnownFileFormats.CollisionDefinition => (SI_FileType_CDEF, Vector4.One),
        KnownFileFormats.CollisionMaterial => (SI_FileType_CMAT, Vector4.One),
        KnownFileFormats.Foliage => (SI_FileType_FOL, Vector4.One),
        KnownFileFormats.ByteBuffer => (SI_FileType_GPBF, Vector4.One),
        KnownFileFormats.GpuCloth => (SI_FileType_GPUC, Vector4.One),
        KnownFileFormats.RenderTexture => (SI_FileType_RTEX, Vector4.One),
        KnownFileFormats.Effect => (SI_FileType_EFX, Vector4.One),
        KnownFileFormats.Timeline => (SI_FileType_TML, Vector4.One),
        KnownFileFormats.Clip => (SI_FileType_CLIP, Vector4.One),
        KnownFileFormats.GUI => (SI_FileType_GUI, Vector4.One),
        KnownFileFormats.GUIColorPreset => (SI_FileType_GCP, Vector4.One),
        KnownFileFormats.GUIStyleList => (SI_FileType_GSTY, Vector4.One),
        KnownFileFormats.GUIConfig => (SI_FileType_GCF, Vector4.One),
        KnownFileFormats.Fsm or KnownFileFormats.Fsm2 => (SI_FileType_FSM, Vector4.One),
        KnownFileFormats.MotionFsm or KnownFileFormats.MotionFsm2 => (SI_FileType_MOTFSM, Vector4.One),
        KnownFileFormats.TimelineFsm2 => (SI_FileType_TMLFSM2, Vector4.One),
        KnownFileFormats.Chain => (SI_FileType_CHAIN, Vector4.One),
        KnownFileFormats.Chain2 => (SI_FileType_CHAIN2, Vector4.One),
        KnownFileFormats.UserCurve => (SI_FileType_UCURVE, Vector4.One),
        KnownFileFormats.UserCurveList => (SI_FileType_UCURVELIST, Vector4.One),
        _ => ('\0', Vector4.One),
    };
}
