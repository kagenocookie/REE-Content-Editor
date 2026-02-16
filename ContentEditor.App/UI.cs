using System.Globalization;
using System.Numerics;
using System.Reflection;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.ImguiHandling.Efx;
using ContentEditor.App.Windowing;
using ContentEditor.Themes;
using ContentPatcher;
using ContentPatcher.FileFormats;
using Hexa.NET.ImNodes;
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

    public static float FontSize { get; set; } = 20;
    public static float FontSizeLarge { get; set; } = 20 * FontSizeLargeMultiplier;

    public const float FontSizeLargeMultiplier = 3;

    public static float UIScale => FontSize / 20f;

    public unsafe static void ConfigureImgui()
    {
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        if (AppConfig.Instance.EnableKeyboardNavigation.Get()) {
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        }

        var fonts = io.Fonts;
        fonts.Clear();

        var normalIcons = ImGui.ImFontConfig();

        fonts.AddFontFromFileTTF(Path.Combine(AppContext.BaseDirectory, "fonts/NotoSans-Regular.ttf"), normalIcons);
        normalIcons.MergeMode = true;

        foreach (var fontFile in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "fonts"), "*.ttf")) {
            var fn = Path.GetFileName(fontFile);
            if (fn.Equals("NotoSans-Regular.ttf", StringComparison.InvariantCultureIgnoreCase)) {
                continue;
            }

            fonts.AddFontFromFileTTF(fontFile, normalIcons);
        }

        fonts.AddFontDefault();
        normalIcons.Destroy();
        io.ConfigErrorRecoveryEnableAssert = false;
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

    internal static readonly PropertyInfo[] NodesThemeFields = [
        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.LinkHoverDistance))!,
        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.LinkLineSegmentsPerLength))!,
        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.LinkThickness))!,

        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.MiniMapOffset))!,
        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.MiniMapPadding))!,

        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.NodeBorderThickness))!,
        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.NodeCornerRounding))!,
        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.NodePadding))!,

        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.PinCircleRadius))!,
        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.PinHoverRadius))!,
        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.PinLineThickness))!,
        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.PinOffset))!,
        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.PinQuadSideLength))!,
        typeof(ImNodesStylePtr).GetProperty(nameof(ImNodesStylePtr.PinTriangleSideLength))!,
    ];

    public static string GetImNodesThemeStyleData()
    {
        var style = ImNodes.GetStyle();
        var themeData = "";
        var colors = Enum.GetValues<ImNodesCol>();
        foreach (var col in colors) {
            if (col == ImNodesCol.Count) continue;
            var color = style.Colors[(int)col];
            themeData += "nodes_col " + col + " = " + color.ToString("X", CultureInfo.InvariantCulture) + "\n";
        }

        foreach (var field in NodesThemeFields) {
            var value = field.GetValue(style);
            string valueStr;
            switch (value) {
                case float flt:
                    valueStr = flt.ToString(CultureInfo.InvariantCulture);
                    break;
                case Vector2 vec2:
                    valueStr = vec2.X.ToString(CultureInfo.InvariantCulture) + ", " + vec2.Y.ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    continue;
            }
            themeData += "nodes_prop " + field.Name + " = " + valueStr + "\n";
        }
        return themeData;
    }

    public static ImNodesContextPtr InitImNodeContext()
    {
        var ctx = ImNodes.CreateContext();
        ImNodes.SetImGuiContext(ImGui.GetCurrentContext());
        ImNodes.SetCurrentContext(ctx);
        var theme = AppConfig.Instance.Theme.Get();
        var themePath = Path.Combine(AppContext.BaseDirectory, "styles", theme + ".theme.txt");
        if (File.Exists(themePath)) {
            var ini = IniFile.ReadFile(themePath);
            ApplyImNodesTheme(ini);
        }
        return ctx;
    }

    public static unsafe void ApplyImNodesTheme(IEnumerable<(string key, string value, string? group)> ini)
    {
        var style = ImNodes.GetStyle();
        foreach (var kv in ini) {
            if (kv.key.StartsWith("nodes_col ")) {
                var name = kv.key.Replace("nodes_col ", "");
                if (uint.TryParse(kv.value, System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var colU32) && Enum.TryParse<ImNodesCol>(name, out var col)) {
                    style.Colors[(int)col] = colU32;
                }
            } else if (kv.key.StartsWith("nodes_prop ")) {
                var name = kv.key.Replace("nodes_prop ", "");
                var prop = NodesThemeFields.FirstOrDefault(ff => ff.Name == name);
                if (prop == null) continue;

                var type = prop.PropertyType.GetElementType();

                if (type == typeof(float)) {
                    if (float.TryParse(kv.value, CultureInfo.InvariantCulture, out var flt)) {
                        ref var val = ref ((floatGetter)Delegate.CreateDelegate(typeof(floatGetter), style, prop.GetMethod!))();
                        val = flt;
                    }
                } else if (type == typeof(Vector2)) {
                    var vals = kv.value.Split(',');
                    if (float.TryParse(vals[0], CultureInfo.InvariantCulture, out var v1) && float.TryParse(vals[1], CultureInfo.InvariantCulture, out var v2)) {
                        ref var val = ref ((Vector2Getter)Delegate.CreateDelegate(typeof(Vector2Getter), style, prop.GetMethod!))();
                        val = new Vector2(v1, v2);
                    }
                }
            }
        }
    }

    private delegate ref Vector2 Vector2Getter();
    private delegate ref float floatGetter();
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
    public static readonly char[] SIC_BookmarkHide = { '\ued63', '\ued64', '\ued65' };
    public static readonly char[] SIC_BookmarkCustomHide = { '\ued66', '\ued67', '\ued68' };
    public static readonly char[] SIC_BookmarkCustomClear = { '\ued69', '\ued6a' };
    public static readonly char[] SIC_FilterClear = { '\ued6b', '\ued6c' };
    public static readonly char[] SIC_BookmarkAdd = { '\ued6d', '\ued6e' };
    public static readonly char[] SIC_BookmarkRemove = { '\ued6f', '\ued70' };
    public static readonly char[] SIC_FileJumpTo = { '\ued71', '\ued72', '\ued73' };
    public static readonly char SI_GenericClose = '\ued74';
    public static readonly char SI_Log = '\ued75';
    public static readonly char SI_LogCompact = '\ued76';
    public static readonly char SI_Save = '\ued77';
    public static readonly char[] SIC_SaveAs = { '\ued78', '\ued79', '\ued7a', '\ued7b', '\ued7c' };
    public static readonly char[] SIC_SaveCopy = { '\ued7d', '\ued7e', '\ued7f' };
    public static readonly char[] SIC_LogCopyAll = { '\ued80', '\ued81', '\ued82', '\ued83' };
    public static readonly char SI_LogDebug = '\ued84';
    public static readonly char SI_GenericClear = '\ued85';
    public static readonly char SI_TagMisc = '\ued86';
    public static readonly char SI_Settings = '\ued87';
    public static readonly char SI_Settings2 = '\ued88';
    public static readonly char SI_Folder = '\ued89';
    public static readonly char SI_FolderEmpty = '\ued8a';
    public static readonly char SI_FolderOpen = '\ued8b';
    public static readonly char[] SIC_FileExtractTo = { '\ued8c', '\ued8d', '\ued8e' };
    public static readonly char[] SIC_FolderOpenFileExplorer = { '\ued91', '\ued92', };
    public static readonly char[] SIC_FolderFile = { '\ued93', '\ued94', };
    public static readonly char[] SIC_FolderContain = { '\ued95', '\ued96', };
    public static readonly char[] SIC_FolderContain2 = { '\ued97', '\ued98', '\ued99' };
    public static readonly char[] SIC_IgnoreRootMotion = { '\ued9a', '\ued9b', '\ued9c' };
    public static readonly char SI_Animation = '\ued9d';
    public static readonly char SI_Animation2 = '\ued9e';
    public static readonly char SI_FileType_MSG = '\ueda0';
    public static readonly char SI_FileType_MOTCAM = '\ueda1';
    public static readonly char SI_FileType_MCAMLIST = '\ueda2';
    public static readonly char SI_FileType_MCAMBANK = '\ueda3';
    public static readonly char SI_FileType_AIMAP = '\ueda4';
    public static readonly char SI_FileType_HF = '\ueda5';
    public static readonly char SI_FileType_CHF = '\ueda6';
    public static readonly char SI_FileType_BHVT = '\ueda7';
    public static readonly char SI_FileType_JMAP = '\ueda8';
    public static readonly char SI_FileType_SDFTEX = '\ueda9';
    public static readonly char SI_FileType_SDF = '\uedaa';
    public static readonly char SI_GenericAdd = '\uedab';
    public static readonly char SI_GenericDelete = '\uedac';
    public static readonly char SI_Folder2 = '\uedad';
    public static readonly char SI_FolderLink = '\uedae';
    public static readonly char SI_FileSource = '\uedaf';
    public static readonly char SI_FileChanges = '\uedb0';
    public static readonly char SI_Bundle = '\uedb1';
    public static readonly char SI_Copy = '\uedb2';
    public static readonly char SI_Paste = '\uedb3';
    public static readonly char[] SIC_FileDuplicate = { '\uedb4', '\uedb5', '\uedb6' };
    public static readonly char[] SIC_BundleAdd = { '\uedb7', '\uedb8', '\uedb9', '\uedba' };
    public static readonly char[] SIC_BundleRemove = { '\uedbb', '\uedbc', '\uedbd', '\uedbe' };
    public static readonly char[] SIC_BundleDelete = { '\uedbf', '\uedc0', '\uedc1', '\uedc2' };
    public static readonly char[] SIC_BundleContain = { '\uedc3', '\uedc4', '\uedc5' };
    public static readonly char[] SIC_BundleSaveTo = { '\uedc6', '\uedc7', '\uedc8', '\uedc9', '\uedca' };
    public static readonly char[] SIC_Copy = { '\uedcb', '\uedcc', '\uedcd', '\uedce', '\uedcf', '\uedd0' };
    public static readonly char[] SIC_Paste = { '\uedd1', '\uedd2', '\uedd3', '\uedd4', '\uedd5', '\uedd6', '\uedd7', '\uedd8' };
    public static readonly char[] SIC_BundleFromLooseFiles = { '\uedd9', '\uedda', '\ueddb', '\ueddc', '\ueddd'};
    public static readonly char[] SIC_BundleFolder = { '\uedde', '\ueddf', '\uede0', '\uede1'};
    public static readonly char[] SIC_BundleFolderEmpty = { '\uede2', '\uede3', '\uede4', '\uede5' };
    public static readonly char[] SIC_BundleFromPakFile = { '\uede6', '\uede7', '\uede8', '\uede9', '\uedea' };
    public static readonly char SI_GenericDelete2 = '\uedeb';
    public static readonly char SI_FileType_PAK = '\uedec';
    public static readonly char SI_SortSizeDown = '\ueded';
    public static readonly char SI_SortSizeUp = '\uedee';
    public static readonly char SI_BundleSortDown = '\uedef';
    public static readonly char SI_GenericDragDropHandle = '\uedf0';
    public static readonly char SI_BundleSortUp = '\uedf1';
    public static readonly char SI_BundleLoadOrder = '\uedf2';
    public static readonly char SI_PatchTo = '\uedf3';
    public static readonly char[] SIC_PatchLooseFiles = { '\uedf4', '\uedf5', '\uedf6' };
    public static readonly char[] SIC_PatchPakFile = { '\uedf8', '\uedf9', '\uedfa' };
    public static readonly char[] SIC_InfoPAK = { '\uedfb', '\uedfc', '\uedfd' };
    public static readonly char[] SIC_InfoBundle = { '\uedfe', '\uedff', '\uee00', '\uee01' };
    public static readonly char SI_GenericReorder = '\uee02';
    public static readonly char[] SIC_BookmarkClear = { '\uee03', '\uee04' };
    public static readonly char[] SIC_MaterialAdd = { '\uee05', '\uee06' };
    public static readonly char[] SIC_MaterialPaste = { '\uee07', '\uee08', '\uee09' };
    public static readonly char SI_TagArmor = '\uee0a';
    public static readonly char SI_FileType_DLG = '\uee0b';
    public static readonly char SI_FileType_JCNS = '\uee0c';
    public static readonly char SI_FileType_OFT = '\uee0d';
    public static readonly char SI_FileType_IFT = '\uee0e';
    public static readonly char SI_FileType_FSLT = '\uee0f';
    public static readonly char SI_FileType_RTMR = '\uee10';
    public static readonly char SI_FileType_DLGCF = '\uee11';
    public static readonly char SI_FileType_DLGLIST = '\uee12';
    public static readonly char SI_FileType_DLGTML = '\uee13';
    public static readonly char SI_FileType_DLGTMLLIST = '\uee14';
    public static readonly char SI_FileType_STAR = '\uee15';
    public static readonly char SI_FileType_RMESH = '\uee16';
    public static readonly char SI_FileType_MOV = '\uee17';
    public static readonly char SI_FileType_STS = '\uee18';
    public static readonly char SI_FileType_NCF = '\uee19';
    public static readonly char SI_FileType_VSDF = '\uee1a';
    public static readonly char[] REECE_LogoFull = { '\uee1b', '\uee1c', '\uee1d', '\uee1e', '\uee1f' };

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
        GameObject go => string.IsNullOrEmpty(go.PrefabPath) ? SI_SceneGameObject2 : SI_FileType_PFB,
        Folder scn => string.IsNullOrEmpty(scn.ScenePath) ? SI_Folder : SI_FolderLink,
        PrefabEditor => SI_FileType_PFB,
        SceneEditor => SI_FileType_SCN,
        EfxEditor => Efx,
        EFXAction => EfxAction,
        EFXEntry => EfxEntry,
        _ => '\0',
    };
    public static (char icon, Vector4 color) GetIcon(KnownFileFormats format) => format switch {
        KnownFileFormats.Prefab => (SI_FileType_PFB, Colors.FileTypePFB),
        KnownFileFormats.Scene => (SI_FileType_SCN, Colors.FileTypeSCN),
        KnownFileFormats.Mesh => (SI_FileType_MESH, Colors.FileTypeMESH),
        KnownFileFormats.CollisionMesh => (SI_FileType_MCOL, Colors.FileTypeMCOL),
        KnownFileFormats.RequestSetCollider => (SI_FileType_RCOL, Colors.FileTypeRCOL),
        KnownFileFormats.MeshMaterial => (SI_FileType_MDF, Colors.FileTypeMDF),
        KnownFileFormats.MasterMaterial => (SI_FileType_MMTR, Colors.FileTypeMMTR),
        KnownFileFormats.Texture => (SI_FileType_TEX, Colors.IconPrimary),
        KnownFileFormats.UVSequence => (SI_FileType_UVS, Colors.FileTypeUVS),
        KnownFileFormats.UserData => (SI_FileType_USER,Colors.FileTypeUSER),
        KnownFileFormats.UserVariables => (SI_FileType_UVAR, Colors.FileTypeUVAR),
        KnownFileFormats.CompositeCollision => (SI_FileType_COCO, Colors.FileTypeCOCO),
        KnownFileFormats.Motion => (SI_FileType_MOT, Colors.FileTypeMOT),
        KnownFileFormats.MotionTree => (SI_FileType_MOTTREE, Colors.FileTypeMOTTREE),
        KnownFileFormats.MotionList => (SI_FileType_MOTLIST, Colors.FileTypeMOTLIST),
        KnownFileFormats.MotionBank => (SI_FileType_MOTBANK, Colors.FileTypeMOTBANK),
        KnownFileFormats.GpuMotionList => (SI_FileType_GPUMOTLIST, Colors.FileTypeGPUMOTLIST),
        KnownFileFormats.CollisionFilter => (SI_FileType_CFIL, Colors.FileTypeCFIL),
        KnownFileFormats.CollisionDefinition => (SI_FileType_CDEF, Colors.FileTypeCDEF),
        KnownFileFormats.CollisionMaterial => (SI_FileType_CMAT, Colors.FileTypeCMAT),
        KnownFileFormats.Foliage => (SI_FileType_FOL,Colors.FileTypeFOL),
        KnownFileFormats.ByteBuffer => (SI_FileType_GPBF, Colors.FileTypeGPBF),
        KnownFileFormats.GpuCloth => (SI_FileType_GPUC, Colors.FileTypeGPUC),
        KnownFileFormats.RenderTexture => (SI_FileType_RTEX, Colors.FileTypeRTEX),
        KnownFileFormats.Effect => (SI_FileType_EFX, Colors.FileTypeEFX),
        KnownFileFormats.Timeline => (SI_FileType_TML, Colors.FileTypeTML),
        KnownFileFormats.Clip => (SI_FileType_CLIP, Colors.FileTypeCLIP),
        KnownFileFormats.GUI => (SI_FileType_GUI, Colors.FileTypeGUI),
        KnownFileFormats.GUIColorPreset => (SI_FileType_GCP, Colors.FileTypeGCP),
        KnownFileFormats.GUIStyleList => (SI_FileType_GSTY, Colors.FileTypeGSTY),
        KnownFileFormats.GUIConfig => (SI_FileType_GCF, Colors.FileTypeGCF),
        KnownFileFormats.Fsm or KnownFileFormats.Fsm2 => (SI_FileType_FSM, Colors.FileTypeFSM),
        KnownFileFormats.MotionFsm or KnownFileFormats.MotionFsm2 => (SI_FileType_MOTFSM, Colors.FileTypeMOTFSM),
        KnownFileFormats.TimelineFsm2 => (SI_FileType_TMLFSM2, Colors.FileTypeTMLFSM2),
        KnownFileFormats.Chain or KnownFileFormats.Chain2 => (SI_FileType_CHAIN, Colors.FileTypeCHAIN),
        KnownFileFormats.UserCurve => (SI_FileType_UCURVE, Colors.FileTypeUCURVE),
        KnownFileFormats.UserCurveList => (SI_FileType_UCURVELIST, Colors.FileTypeUCURVELIST),
        KnownFileFormats.Message => (SI_FileType_MSG, Colors.FileTypeMSG),
        KnownFileFormats.MotionCamera => (SI_FileType_MOTCAM, Colors.FileTypeMOTCAM),
        KnownFileFormats.MotionCameraList => (SI_FileType_MCAMLIST, Colors.FileTypeMCAMLIST),
        KnownFileFormats.MotionCameraBank => (SI_FileType_MCAMBANK, Colors.FileTypeMCAMBANK),
        KnownFileFormats.AIMap => (SI_FileType_AIMAP, Colors.FileTypeAIMAP),
        KnownFileFormats.HeightField => (SI_FileType_HF, Colors.FileTypeHF),
        KnownFileFormats.CollisionHeightField => (SI_FileType_CHF, Colors.FileTypeCHF),
        KnownFileFormats.BehaviorTree => (SI_FileType_BHVT, Colors.FileTypeBHVT),
        KnownFileFormats.JointMap => (SI_FileType_JMAP, Colors.FileTypeJMAP),
        KnownFileFormats.SDFTexture => (SI_FileType_SDFTEX, Colors.FileTypeSDFTEX),
        KnownFileFormats.Shader => (SI_FileType_SDF, Colors.FileTypeSDF),
        KnownFileFormats.JointConstraints => (SI_FileType_JCNS, Colors.FileTypeJCNS),
        KnownFileFormats.RayTraceMaterialRedirect => (SI_FileType_RTMR, Colors.FileTypeRTMR),
        KnownFileFormats.OutlineFont => (SI_FileType_OFT, Colors.FileTypeOFT),
        KnownFileFormats.IconFont => (SI_FileType_IFT, Colors.FileTypeIFT),
        KnownFileFormats.FontSlot => (SI_FileType_FSLT, Colors.FileTypeFSLT),
        KnownFileFormats.Dialogue => (SI_FileType_DLG, Colors.FileTypeDLG),
        KnownFileFormats.DialogueConfig => (SI_FileType_DLGCF, Colors.FileTypeDLGCF),
        KnownFileFormats.DialogueList => (SI_FileType_DLGLIST, Colors.FileTypeDLGLIST),
        KnownFileFormats.DialogueTimeline => (SI_FileType_DLGTML, Colors.FileTypeDLGTML),
        KnownFileFormats.DialogueTimelineList => (SI_FileType_DLGTMLLIST, Colors.FileTypeDLGTMLLIST),
        KnownFileFormats.StarCatalogue => (SI_FileType_STAR, Colors.FileTypeSTAR),
        KnownFileFormats.RigidBodyMesh => (SI_FileType_RMESH, Colors.FileTypeRMESH),
        KnownFileFormats.Movie => (SI_FileType_MOV, Colors.FileTypeMOV),
        KnownFileFormats.ShaderTimer => (SI_FileType_STS, Colors.FileTypeSTS),
        KnownFileFormats.NetworkConfig => (SI_FileType_NCF, Colors.FileTypeNCF),
        KnownFileFormats.VfxShader => (SI_FileType_VSDF, Colors.FileTypeVSDF),
        _ => ('\0', Vector4.One),
    };
}
