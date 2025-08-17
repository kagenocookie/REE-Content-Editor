using System.Globalization;
using System.Numerics;
using System.Reflection;
using ImGuiNET;

namespace ContentEditor.Themes;

public class DefaultThemes
{
    public static readonly Dictionary<string, Action> Themes = new() {
        { "default", SetupRoundedVS },
        { "cherry", SetupCherry },
        { "darkCyan", SetupComfortableDarkCyan },
        { "light", SetupDefaultLight },
        { "unreal", SetupUnreal },
        { "dark", SetupDark },
    };

    private static string[]? _availableThemes;
    public static string[] AvailableThemes => _availableThemes ??= RefreshAvailableThemes();

    public static string[] RefreshAvailableThemes()
    {
        var themePath = Path.GetFullPath("styles");
        var list = new List<string>(Themes.Keys.Order());
        if (!Directory.Exists(themePath)) return _availableThemes = list.ToArray();

        foreach (var file in Directory.EnumerateFiles(themePath, "*.theme.txt")) {
            var theme = file.Replace(themePath, "").Replace(".theme.txt", "").Replace("\\", "");
            list.Add(theme);
        }
        return _availableThemes = list.Distinct().ToArray();
    }

    private static readonly PropertyInfo[] ThemeFields = [
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.WindowPadding))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.FramePadding))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.ItemSpacing))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.ItemInnerSpacing))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.TouchExtraPadding))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.IndentSpacing))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.ScrollbarSize))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.GrabMinSize))!,

        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.WindowBorderSize))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.ChildBorderSize))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.PopupBorderSize))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.FrameBorderSize))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.TabBorderSize))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.TabBarBorderSize))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.TabBarOverlineSize))!,

        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.WindowRounding))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.ChildRounding))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.PopupRounding))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.ScrollbarRounding))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.GrabRounding))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.TabRounding))!,

        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.CellPadding))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.TableAngledHeadersAngle))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.TableAngledHeadersTextAlign))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.WindowTitleAlign))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.WindowMenuButtonPosition))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.ButtonTextAlign))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.SelectableTextAlign))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.SeparatorTextBorderSize))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.SeparatorTextAlign))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.SeparatorTextPadding))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.LogSliderDeadzone))!,

        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.DockingSeparatorSize))!,

        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.HoverFlagsForTooltipMouse))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.HoverFlagsForTooltipNav))!,

        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.DisplayWindowPadding))!,
        typeof(ImGuiStylePtr).GetProperty(nameof(ImGuiStylePtr.DisplaySafeAreaPadding))!,
    ];

    public static void SetupCherry()
    {
        // Cherry style from ImThemes
        var style = ImGuiNET.ImGui.GetStyle();

        style.Alpha = 1.0f;
        style.DisabledAlpha = 0.6000000238418579f;
        style.WindowPadding = new Vector2(6.0f, 3.0f);
        style.WindowRounding = 0.0f;
        style.WindowBorderSize = 1.0f;
        style.WindowMinSize = new Vector2(32.0f, 32.0f);
        style.WindowTitleAlign = new Vector2(0.5f, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir.Left;
        style.ChildRounding = 0.0f;
        style.ChildBorderSize = 1.0f;
        style.PopupRounding = 0.0f;
        style.PopupBorderSize = 1.0f;
        style.FramePadding = new Vector2(5.0f, 1.0f);
        style.FrameRounding = 3.0f;
        style.FrameBorderSize = 1.0f;
        style.ItemSpacing = new Vector2(8.0f, 4.0f);
        style.ItemInnerSpacing = new Vector2(4.0f, 4.0f);
        style.CellPadding = new Vector2(4.0f, 2.0f);
        style.IndentSpacing = 21.0f;
        style.ColumnsMinSpacing = 6.0f;
        style.ScrollbarSize = 13.0f;
        style.ScrollbarRounding = 16.0f;
        style.GrabMinSize = 20.0f;
        style.GrabRounding = 2.0f;
        style.TabRounding = 4.0f;
        style.TabBorderSize = 1.0f;
        style.TabMinWidthForCloseButton = 0.0f;
        style.ColorButtonPosition = ImGuiDir.Right;
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0.0f, 0.0f);

        style.Colors[(int)ImGuiCol.Text] = new Vector4(0.8588235378265381f, 0.929411768913269f, 0.886274516582489f, 0.8799999952316284f);
        style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.8588235378265381f, 0.929411768913269f, 0.886274516582489f, 0.2800000011920929f);
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.1294117718935013f, 0.1372549086809158f, 0.168627455830574f, 1.0f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.2000000029802322f, 0.2196078449487686f, 0.2666666805744171f, 0.9f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.5372549295425415f, 0.47843137383461f, 0.2549019753932953f, 0.1620000004768372f);
        style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.2000000029802322f, 0.2196078449487686f, 0.2666666805744171f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 0.7799999713897705f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.2313725501298904f, 0.2000000029802322f, 0.2705882489681244f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.501960813999176f, 0.07450980693101883f, 0.2549019753932953f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.2000000029802322f, 0.2196078449487686f, 0.2666666805744171f, 0.75f);
        style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.2000000029802322f, 0.2196078449487686f, 0.2666666805744171f, 0.4699999988079071f);
        style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.2000000029802322f, 0.2196078449487686f, 0.2666666805744171f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.08627451211214066f, 0.1490196138620377f, 0.1568627506494522f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 0.7799999713897705f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 1.0f);
        style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.2599999904632568f, 0.5899999737739563f, 0.9800000190734863f, 1.0f);
        style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.239999994635582f, 0.5199999809265137f, 0.8799999952316284f, 1.0f);
        style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.2599999904632568f, 0.5899999737739563f, 0.9800000190734863f, 1.0f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.4666666686534882f, 0.7686274647712708f, 0.8274509906768799f, 0.1400000005960464f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 0.8600000143051147f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 1.0f);
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 0.7599999904632568f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 0.8600000143051147f);
        style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.501960813999176f, 0.07450980693101883f, 0.2549019753932953f, 1.0f);
        style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.4274509847164154f, 0.4274509847164154f, 0.4980392158031464f, 0.5f);
        style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.09803921729326248f, 0.4f, 0.7490196228027344f, 0.7799999713897705f);
        style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.09803921729326248f, 0.4f, 0.7490196228027344f, 1.0f);
        style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.4666666686534882f, 0.7686274647712708f, 0.8274509906768799f, 0.03999999910593033f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 0.7799999713897705f);
        style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 1.0f);
        style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.1764705926179886f, 0.3490196168422699f, 0.5764706134796143f, 0.8619999885559082f);
        style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.8f);
        style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.196078434586525f, 0.407843142747879f, 0.6784313917160034f, 1.0f);
        style.Colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.06666667014360428f, 0.1019607856869698f, 0.1450980454683304f, 0.9724000096321106f);
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.1333333402872086f, 0.2588235437870026f, 0.4235294163227081f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.8588235378265381f, 0.929411768913269f, 0.886274516582489f, 0.6299999952316284f);
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.8588235378265381f, 0.929411768913269f, 0.886274516582489f, 0.6299999952316284f);
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 1.0f);
        style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.1882352977991104f, 0.1882352977991104f, 0.2000000029802322f, 1.0f);
        style.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.3098039329051971f, 0.3098039329051971f, 0.3490196168422699f, 1.0f);
        style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.2274509817361832f, 0.2274509817361832f, 0.2470588237047195f, 1.0f);
        style.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1.0f, 1.0f, 1.0f, 0.05999999865889549f);
        style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.4549019634723663f, 0.196078434586525f, 0.2980392277240753f, 0.4300000071525574f);
        style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(1.0f, 1.0f, 0.0f, 0.9f);
        style.Colors[(int)ImGuiCol.NavCursor] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 1.0f);
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.0f, 1.0f, 1.0f, 0.7f);
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.2000000029802322f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.3499999940395355f);
    }

    public static void SetupComfortableDarkCyan()
    {
        // Comfortable Dark Cyan styleSouthCraftX from ImThemes
        var style = ImGuiNET.ImGui.GetStyle();

        style.Alpha = 1.0f;
        style.DisabledAlpha = 1.0f;
        style.WindowPadding = new Vector2(20.0f, 20.0f);
        style.WindowRounding = 11.5f;
        style.WindowBorderSize = 0.0f;
        style.WindowMinSize = new Vector2(20.0f, 20.0f);
        style.WindowTitleAlign = new Vector2(0.5f, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir.None;
        style.ChildRounding = 20.0f;
        style.ChildBorderSize = 1.0f;
        style.PopupRounding = 17.4f;
        style.PopupBorderSize = 1.0f;
        style.FramePadding = new Vector2(20.0f, 3.4f);
        style.FrameRounding = 11.9f;
        style.FrameBorderSize = 0.0f;
        style.ItemSpacing = new Vector2(8.9f, 13.4f);
        style.ItemInnerSpacing = new Vector2(7.1f, 1.8f);
        style.CellPadding = new Vector2(12.1f, 9.2f);
        style.IndentSpacing = 0.0f;
        style.ColumnsMinSpacing = 8.7f;
        style.ScrollbarSize = 11.6f;
        style.ScrollbarRounding = 15.9f;
        style.GrabMinSize = 3.7f;
        style.GrabRounding = 20.0f;
        style.TabRounding = 9.8f;
        style.TabBorderSize = 0.0f;
        style.TabMinWidthForCloseButton = 0.0f;
        style.ColorButtonPosition = ImGuiDir.Right;
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0.0f, 0.0f);

        style.Colors[(int)ImGuiCol.Text] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.2745098173618317f, 0.3176470696926117f, 0.4509803950786591f, 1.0f);
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.0784313753247261f, 0.08627451211214066f, 0.1019607856869698f, 1.0f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.09411764889955521f, 0.1019607856869698f, 0.1176470592617989f, 1.0f);
        style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.0784313753247261f, 0.08627451211214066f, 0.1019607856869698f, 1.0f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.1568627506494522f, 0.168627455830574f, 0.1921568661928177f, 1.0f);
        style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.0784313753247261f, 0.08627451211214066f, 0.1019607856869698f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.1137254908680916f, 0.125490203499794f, 0.1529411822557449f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.1568627506494522f, 0.168627455830574f, 0.1921568661928177f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.1568627506494522f, 0.168627455830574f, 0.1921568661928177f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.0470588244497776f, 0.05490196123719215f, 0.07058823853731155f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.0470588244497776f, 0.05490196123719215f, 0.07058823853731155f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.0784313753247261f, 0.08627451211214066f, 0.1019607856869698f, 1.0f);
        style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.09803921729326248f, 0.105882354080677f, 0.1215686276555061f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.0470588244497776f, 0.05490196123719215f, 0.07058823853731155f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.1176470592617989f, 0.1333333402872086f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.1568627506494522f, 0.168627455830574f, 0.1921568661928177f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.1176470592617989f, 0.1333333402872086f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.0313725508749485f, 0.9490196108818054f, 0.843137264251709f, 1.0f);
        style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.0313725508749485f, 0.9490196108818054f, 0.843137264251709f, 1.0f);
        style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.6000000238418579f, 0.9647058844566345f, 0.0313725508749485f, 1.0f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.1176470592617989f, 0.1333333402872086f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.1803921610116959f, 0.1882352977991104f, 0.196078434586525f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.1529411822557449f, 0.1529411822557449f, 0.1529411822557449f, 1.0f);
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.1411764770746231f, 0.1647058874368668f, 0.2078431397676468f, 1.0f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.105882354080677f, 0.105882354080677f, 0.105882354080677f, 1.0f);
        style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.0784313753247261f, 0.08627451211214066f, 0.1019607856869698f, 1.0f);
        style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.1294117718935013f, 0.1490196138620377f, 0.1921568661928177f, 1.0f);
        style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.1568627506494522f, 0.1843137294054031f, 0.250980406999588f, 1.0f);
        style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.1568627506494522f, 0.1843137294054031f, 0.250980406999588f, 1.0f);
        style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1450980454683304f, 1.0f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.0313725508749485f, 0.9490196108818054f, 0.843137264251709f, 1.0f);
        style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.0784313753247261f, 0.08627451211214066f, 0.1019607856869698f, 1.0f);
        style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.1176470592617989f, 0.1333333402872086f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.1176470592617989f, 0.1333333402872086f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.0784313753247261f, 0.08627451211214066f, 0.1019607856869698f, 1.0f);
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.125490203499794f, 0.2745098173618317f, 0.572549045085907f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.5215686559677124f, 0.6000000238418579f, 0.7019608020782471f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(0.03921568766236305f, 0.9803921580314636f, 0.9803921580314636f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.0313725508749485f, 0.9490196108818054f, 0.843137264251709f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.1568627506494522f, 0.1843137294054031f, 0.250980406999588f, 1.0f);
        style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.0470588244497776f, 0.05490196123719215f, 0.07058823853731155f, 1.0f);
        style.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.0470588244497776f, 0.05490196123719215f, 0.07058823853731155f, 1.0f);
        style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
        style.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.1176470592617989f, 0.1333333402872086f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(0.09803921729326248f, 0.105882354080677f, 0.1215686276555061f, 1.0f);
        style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.9372549057006836f, 0.9372549057006836f, 0.9372549057006836f, 1.0f);
        style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(0.4980392158031464f, 0.5137255191802979f, 1.0f, 1.0f);
        style.Colors[(int)ImGuiCol.NavCursor] = new Vector4(0.2666666805744171f, 0.2901960909366608f, 1.0f, 1.0f);
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(0.4980392158031464f, 0.5137255191802979f, 1.0f, 1.0f);
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.196078434586525f, 0.1764705926179886f, 0.5450980663299561f, 0.501960813999176f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.196078434586525f, 0.1764705926179886f, 0.5450980663299561f, 0.501960813999176f);
    }

    public static void SetupDefaultLight()
    {
        // Light styledougbinks from ImThemes
        var style = ImGuiNET.ImGui.GetStyle();

        style.Alpha = 1.0f;
        style.DisabledAlpha = 0.6000000238418579f;
        style.WindowPadding = new Vector2(8.0f, 8.0f);
        style.WindowRounding = 0.0f;
        style.WindowBorderSize = 1.0f;
        style.WindowMinSize = new Vector2(32.0f, 32.0f);
        style.WindowTitleAlign = new Vector2(0.0f, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir.Left;
        style.ChildRounding = 0.0f;
        style.ChildBorderSize = 1.0f;
        style.PopupRounding = 0.0f;
        style.PopupBorderSize = 1.0f;
        style.FramePadding = new Vector2(4.0f, 3.0f);
        style.FrameRounding = 0.0f;
        style.FrameBorderSize = 0.0f;
        style.ItemSpacing = new Vector2(8.0f, 4.0f);
        style.ItemInnerSpacing = new Vector2(4.0f, 4.0f);
        style.CellPadding = new Vector2(4.0f, 2.0f);
        style.IndentSpacing = 21.0f;
        style.ColumnsMinSpacing = 6.0f;
        style.ScrollbarSize = 14.0f;
        style.ScrollbarRounding = 9.0f;
        style.GrabMinSize = 10.0f;
        style.GrabRounding = 0.0f;
        style.TabRounding = 4.0f;
        style.TabBorderSize = 0.0f;
        style.TabMinWidthForCloseButton = 0.0f;
        style.ColorButtonPosition = ImGuiDir.Right;
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0.0f, 0.0f);

        style.Colors[(int)ImGuiCol.Text] = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
        style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.6000000238418579f, 0.6000000238418579f, 0.6000000238418579f, 1.0f);
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.9372549057006836f, 0.9372549057006836f, 0.9372549057006836f, 1.0f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(1.0f, 1.0f, 1.0f, 0.9800000190734863f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.0f, 0.0f, 0.0f, 0.300000011920929f);
        style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.4f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.6700000166893005f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.95686274766922f, 0.95686274766922f, 0.95686274766922f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.8196078538894653f, 0.8196078538894653f, 0.8196078538894653f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(1.0f, 1.0f, 1.0f, 0.5099999904632568f);
        style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.8588235378265381f, 0.8588235378265381f, 0.8588235378265381f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.9764705896377563f, 0.9764705896377563f, 0.9764705896377563f, 0.5299999713897705f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.686274528503418f, 0.686274528503418f, 0.686274528503418f, 0.8f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.4862745106220245f, 0.4862745106220245f, 0.4862745106220245f, 0.8f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.4862745106220245f, 0.4862745106220245f, 0.4862745106220245f, 1.0f);
        style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 1.0f);
        style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.7799999713897705f);
        style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.4588235318660736f, 0.5372549295425415f, 0.8f, 0.6000000238418579f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.4f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.05882352963089943f, 0.529411792755127f, 0.9764705896377563f, 1.0f);
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.3100000023841858f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.8f);
        style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 1.0f);
        style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.3882353007793427f, 0.3882353007793427f, 0.3882353007793427f, 0.6200000047683716f);
        style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.1372549086809158f, 0.4392156898975372f, 0.8f, 0.7799999713897705f);
        style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.1372549086809158f, 0.4392156898975372f, 0.8f, 1.0f);
        style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.3490196168422699f, 0.3490196168422699f, 0.3490196168422699f, 0.1700000017881393f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.6700000166893005f);
        style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.949999988079071f);
        style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.7607843279838562f, 0.7960784435272217f, 0.8352941274642944f, 0.9309999942779541f);
        style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.8f);
        style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.5921568870544434f, 0.7254902124404907f, 0.8823529481887817f, 1.0f);
        style.Colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.9176470637321472f, 0.9254902005195618f, 0.9333333373069763f, 0.9861999750137329f);
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.7411764860153198f, 0.8196078538894653f, 0.9137254953384399f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.3882353007793427f, 0.3882353007793427f, 0.3882353007793427f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(1.0f, 0.4274509847164154f, 0.3490196168422699f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.8980392217636108f, 0.6980392336845398f, 0.0f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(1.0f, 0.4470588266849518f, 0.0f, 1.0f);
        style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.7764706015586853f, 0.8666666746139526f, 0.9764705896377563f, 1.0f);
        style.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.5686274766921997f, 0.5686274766921997f, 0.6392157077789307f, 1.0f);
        style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.6784313917160034f, 0.6784313917160034f, 0.7372549176216125f, 1.0f);
        style.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(0.2980392277240753f, 0.2980392277240753f, 0.2980392277240753f, 0.09000000357627869f);
        style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.3499999940395355f);
        style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.949999988079071f);
        style.Colors[(int)ImGuiCol.NavCursor] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.8f);
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(0.6980392336845398f, 0.6980392336845398f, 0.6980392336845398f, 0.7f);
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.2000000029802322f, 0.2000000029802322f, 0.2000000029802322f, 0.2000000029802322f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.2000000029802322f, 0.2000000029802322f, 0.2000000029802322f, 0.3499999940395355f);
    }

    public static void SetupRoundedVS()
    {
        // Rounded Visual Studio styleRedNicStone from ImThemes
        var style = ImGuiNET.ImGui.GetStyle();

        style.Alpha = 1.0f;
        style.DisabledAlpha = 0.6000000238418579f;
        style.WindowPadding = new Vector2(8.0f, 8.0f);
        style.WindowRounding = 4.0f;
        style.WindowBorderSize = 0.5f;
        style.WindowMinSize = new Vector2(32.0f, 32.0f);
        style.WindowTitleAlign = new Vector2(0.0f, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir.Left;
        style.ChildRounding = 0.0f;
        style.ChildBorderSize = 1.0f;
        style.PopupRounding = 4.0f;
        style.PopupBorderSize = 1.0f;
        style.FramePadding = new Vector2(4.0f, 3.0f);
        style.FrameRounding = 2.5f;
        style.FrameBorderSize = 0.0f;
        style.ItemSpacing = new Vector2(8.0f, 4.0f);
        style.ItemInnerSpacing = new Vector2(4.0f, 4.0f);
        style.CellPadding = new Vector2(4.0f, 2.0f);
        style.IndentSpacing = 21.0f;
        style.ColumnsMinSpacing = 6.0f;
        style.ScrollbarSize = 20.0f;
        style.ScrollbarRounding = 2.5f;
        style.GrabMinSize = 10.0f;
        style.GrabRounding = 2.0f;
        style.TabRounding = 3.5f;
        style.TabBorderSize = 0.0f;
        style.TabMinWidthForCloseButton = 0.0f;
        style.ColorButtonPosition = ImGuiDir.Right;
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0.0f, 0.0f);

        style.Colors[(int)ImGuiCol.Text] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.5921568870544434f, 0.5921568870544434f, 0.5921568870544434f, 1.0f);
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.3058823645114899f, 0.3058823645114899f, 0.7058823645114899f, 1.0f);
        style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.3058823645114899f, 0.3058823645114899f, 0.3058823645114899f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.2000000029802322f, 0.2000000029802322f, 0.2156862765550613f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 0.5f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 0.6f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.2000000029802322f, 0.2000000029802322f, 0.2156862765550613f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.2000000029802322f, 0.2000000029802322f, 0.2156862765550613f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.321568638086319f, 0.321568638086319f, 0.3333333432674408f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.3529411852359772f, 0.3529411852359772f, 0.3725490272045135f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.3529411852359772f, 0.3529411852359772f, 0.3725490272045135f, 1.0f);
        style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.8254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.0f, 0.4666666686534882f, 0.9843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.2000000029802322f, 0.2000000029802322f, 0.2156862765550613f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.2000000029802322f, 0.2000000029802322f, 0.2156862765550613f, 1.0f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.3058823645114899f, 0.3058823645114899f, 0.3058823645114899f, 1.0f);
        style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.3058823645114899f, 0.3058823645114899f, 0.3058823645114899f, 1.0f);
        style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.3058823645114899f, 0.3058823645114899f, 0.3058823645114899f, 1.0f);
        style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.2000000029802322f, 0.2000000029802322f, 0.2156862765550613f, 1.0f);
        style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.321568638086319f, 0.321568638086319f, 0.3333333432674408f, 1.0f);
        style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.1882352977991104f, 0.1882352977991104f, 0.2000000029802322f, 1.0f);
        style.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.3098039329051971f, 0.3098039329051971f, 0.3490196168422699f, 1.0f);
        style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.2274509817361832f, 0.2274509817361832f, 0.2470588237047195f, 1.0f);
        style.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1.0f, 1.0f, 1.0f, 0.05999999865889549f);
        style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.NavCursor] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.0f, 1.0f, 1.0f, 0.7f);
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.2000000029802322f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
    }

    public static void SetupUnreal()
    {
        // Unreal styledev0-1 from ImThemes
        var style = ImGuiNET.ImGui.GetStyle();

        style.Alpha = 1.0f;
        style.DisabledAlpha = 0.6000000238418579f;
        style.WindowPadding = new Vector2(8.0f, 8.0f);
        style.WindowRounding = 0.0f;
        style.WindowBorderSize = 1.0f;
        style.WindowMinSize = new Vector2(32.0f, 32.0f);
        style.WindowTitleAlign = new Vector2(0.0f, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir.Left;
        style.ChildRounding = 0.0f;
        style.ChildBorderSize = 1.0f;
        style.PopupRounding = 0.0f;
        style.PopupBorderSize = 1.0f;
        style.FramePadding = new Vector2(4.0f, 3.0f);
        style.FrameRounding = 0.0f;
        style.FrameBorderSize = 0.0f;
        style.ItemSpacing = new Vector2(8.0f, 4.0f);
        style.ItemInnerSpacing = new Vector2(4.0f, 4.0f);
        style.CellPadding = new Vector2(4.0f, 2.0f);
        style.IndentSpacing = 21.0f;
        style.ColumnsMinSpacing = 6.0f;
        style.ScrollbarSize = 14.0f;
        style.ScrollbarRounding = 9.0f;
        style.GrabMinSize = 10.0f;
        style.GrabRounding = 0.0f;
        style.TabRounding = 4.0f;
        style.TabBorderSize = 0.0f;
        style.TabMinWidthForCloseButton = 0.0f;
        style.ColorButtonPosition = ImGuiDir.Right;
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0.0f, 0.0f);

        style.Colors[(int)ImGuiCol.Text] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.4980392158031464f, 0.4980392158031464f, 0.4980392158031464f, 1.0f);
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.05882352963089943f, 0.05882352963089943f, 0.05882352963089943f, 0.9399999976158142f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(1.0f, 1.0f, 1.0f, 0.0f);
        style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.0784313753247261f, 0.0784313753247261f, 0.0784313753247261f, 0.9399999976158142f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.4274509847164154f, 0.4274509847164154f, 0.4980392158031464f, 0.5f);
        style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.2000000029802322f, 0.2078431397676468f, 0.2196078449487686f, 0.5400000214576721f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.4f, 0.4f, 0.4f, 0.4f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.1764705926179886f, 0.1764705926179886f, 0.1764705926179886f, 0.6700000166893005f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.03921568766236305f, 0.03921568766236305f, 0.03921568766236305f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.2862745225429535f, 0.2862745225429535f, 0.2862745225429535f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.0f, 0.0f, 0.0f, 0.5099999904632568f);
        style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.1372549086809158f, 0.1372549086809158f, 0.1372549086809158f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.01960784383118153f, 0.01960784383118153f, 0.01960784383118153f, 0.5299999713897705f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.3098039329051971f, 0.3098039329051971f, 0.3098039329051971f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.407843142747879f, 0.407843142747879f, 0.407843142747879f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.5098039507865906f, 0.5098039507865906f, 0.5098039507865906f, 1.0f);
        style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.9372549057006836f, 0.9372549057006836f, 0.9372549057006836f, 1.0f);
        style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.5098039507865906f, 0.5098039507865906f, 0.5098039507865906f, 1.0f);
        style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.8588235378265381f, 0.8588235378265381f, 0.8588235378265381f, 1.0f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.4392156898975372f, 0.4392156898975372f, 0.4392156898975372f, 0.4f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.4588235318660736f, 0.4666666686534882f, 0.47843137383461f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.4196078479290009f, 0.4196078479290009f, 0.4196078479290009f, 1.0f);
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.6980392336845398f, 0.6980392336845398f, 0.6980392336845398f, 0.3100000023841858f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.6980392336845398f, 0.6980392336845398f, 0.6980392336845398f, 0.8f);
        style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.47843137383461f, 0.4980392158031464f, 0.5176470875740051f, 1.0f);
        style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.4274509847164154f, 0.4274509847164154f, 0.4980392158031464f, 0.5f);
        style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.7176470756530762f, 0.7176470756530762f, 0.7176470756530762f, 0.7799999713897705f);
        style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.5098039507865906f, 0.5098039507865906f, 0.5098039507865906f, 1.0f);
        style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.9098039269447327f, 0.9098039269447327f, 0.9098039269447327f, 0.25f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.8078431487083435f, 0.8078431487083435f, 0.8078431487083435f, 0.6700000166893005f);
        style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.4588235318660736f, 0.4588235318660736f, 0.4588235318660736f, 0.949999988079071f);
        style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.1764705926179886f, 0.3490196168422699f, 0.5764706134796143f, 0.8619999885559082f);
        style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.8f);
        style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.196078434586525f, 0.407843142747879f, 0.6784313917160034f, 1.0f);
        style.Colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.06666667014360428f, 0.1019607856869698f, 0.1450980454683304f, 0.9724000096321106f);
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.1333333402872086f, 0.2588235437870026f, 0.4235294163227081f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.6078431606292725f, 0.6078431606292725f, 0.6078431606292725f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(1.0f, 0.4274509847164154f, 0.3490196168422699f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.729411780834198f, 0.6000000238418579f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(1.0f, 0.6000000238418579f, 0.0f, 1.0f);
        style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.1882352977991104f, 0.1882352977991104f, 0.2000000029802322f, 1.0f);
        style.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.3098039329051971f, 0.3098039329051971f, 0.3490196168422699f, 1.0f);
        style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.2274509817361832f, 0.2274509817361832f, 0.2470588237047195f, 1.0f);
        style.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1.0f, 1.0f, 1.0f, 0.05999999865889549f);
        style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.8666666746139526f, 0.8666666746139526f, 0.8666666746139526f, 0.3499999940395355f);
        style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(1.0f, 1.0f, 0.0f, 0.9f);
        style.Colors[(int)ImGuiCol.NavCursor] = new Vector4(0.6000000238418579f, 0.6000000238418579f, 0.6000000238418579f, 1.0f);
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.0f, 1.0f, 1.0f, 0.7f);
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.2000000029802322f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.3499999940395355f);
    }

    public static void SetupDark()
    {
        // Dark style from ImThemes
        var style = ImGuiNET.ImGui.GetStyle();

        style.Alpha = 1.0f;
        style.DisabledAlpha = 0.6000000238418579f;
        style.WindowPadding = new Vector2(8.0f, 8.0f);
        style.WindowRounding = 0.0f;
        style.WindowBorderSize = 1.0f;
        style.WindowMinSize = new Vector2(32.0f, 32.0f);
        style.WindowTitleAlign = new Vector2(0.0f, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir.Left;
        style.ChildRounding = 0.0f;
        style.ChildBorderSize = 1.0f;
        style.PopupRounding = 0.0f;
        style.PopupBorderSize = 1.0f;
        style.FramePadding = new Vector2(4.0f, 3.0f);
        style.FrameRounding = 0.0f;
        style.FrameBorderSize = 0.0f;
        style.ItemSpacing = new Vector2(8.0f, 4.0f);
        style.ItemInnerSpacing = new Vector2(4.0f, 4.0f);
        style.CellPadding = new Vector2(4.0f, 2.0f);
        style.IndentSpacing = 21.0f;
        style.ColumnsMinSpacing = 6.0f;
        style.ScrollbarSize = 14.0f;
        style.ScrollbarRounding = 9.0f;
        style.GrabMinSize = 10.0f;
        style.GrabRounding = 0.0f;
        style.TabRounding = 4.0f;
        style.TabBorderSize = 0.0f;
        style.TabMinWidthForCloseButton = 0.0f;
        style.ColorButtonPosition = ImGuiDir.Right;
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0.0f, 0.0f);

        style.Colors[(int)ImGuiCol.Text] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.4980392158031464f, 0.4980392158031464f, 0.4980392158031464f, 1.0f);
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.05882352963089943f, 0.05882352963089943f, 0.05882352963089943f, 0.9399999976158142f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.0784313753247261f, 0.0784313753247261f, 0.0784313753247261f, 0.9399999976158142f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.4274509847164154f, 0.4274509847164154f, 0.4980392158031464f, 0.5f);
        style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.1568627506494522f, 0.2862745225429535f, 0.47843137383461f, 0.5400000214576721f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.4f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.6700000166893005f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.03921568766236305f, 0.03921568766236305f, 0.03921568766236305f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.1568627506494522f, 0.2862745225429535f, 0.47843137383461f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.0f, 0.0f, 0.0f, 0.5099999904632568f);
        style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.1372549086809158f, 0.1372549086809158f, 0.1372549086809158f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.01960784383118153f, 0.01960784383118153f, 0.01960784383118153f, 0.5299999713897705f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.3098039329051971f, 0.3098039329051971f, 0.3098039329051971f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.407843142747879f, 0.407843142747879f, 0.407843142747879f, 1.0f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.5098039507865906f, 0.5098039507865906f, 0.5098039507865906f, 1.0f);
        style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.2599999904632568f, 0.5899999737739563f, 0.9800000190734863f, 1.0f);
        style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.239999994635582f, 0.5199999809265137f, 0.8799999952316284f, 1.0f);
        style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.2599999904632568f, 0.5899999737739563f, 0.9800000190734863f, 1.0f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.4f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.05882352963089943f, 0.529411792755127f, 0.9764705896377563f, 1.0f);
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.3100000023841858f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.8f);
        style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 1.0f);
        style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.4274509847164154f, 0.4274509847164154f, 0.4980392158031464f, 0.5f);
        style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.09803921729326248f, 0.4f, 0.7490196228027344f, 0.7799999713897705f);
        style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.09803921729326248f, 0.4f, 0.7490196228027344f, 1.0f);
        style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.2000000029802322f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.6700000166893005f);
        style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.949999988079071f);
        style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.1764705926179886f, 0.3490196168422699f, 0.5764706134796143f, 0.8619999885559082f);
        style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.8f);
        style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.196078434586525f, 0.407843142747879f, 0.6784313917160034f, 1.0f);
        style.Colors[(int)ImGuiCol.TabDimmed] = new Vector4(0.06666667014360428f, 0.1019607856869698f, 0.1450980454683304f, 0.9724000096321106f);
        style.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.1333333402872086f, 0.2588235437870026f, 0.4235294163227081f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.6078431606292725f, 0.6078431606292725f, 0.6078431606292725f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(1.0f, 0.4274509847164154f, 0.3490196168422699f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.8980392217636108f, 0.6980392336845398f, 0.0f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(1.0f, 0.6000000238418579f, 0.0f, 1.0f);
        style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.1882352977991104f, 0.1882352977991104f, 0.2000000029802322f, 1.0f);
        style.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.3098039329051971f, 0.3098039329051971f, 0.3490196168422699f, 1.0f);
        style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.2274509817361832f, 0.2274509817361832f, 0.2470588237047195f, 1.0f);
        style.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1.0f, 1.0f, 1.0f, 0.05999999865889549f);
        style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 0.3499999940395355f);
        style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(1.0f, 1.0f, 0.0f, 0.9f);
        style.Colors[(int)ImGuiCol.NavCursor] = new Vector4(0.2588235437870026f, 0.5882353186607361f, 0.9764705896377563f, 1.0f);
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.0f, 1.0f, 1.0f, 0.7f);
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.2000000029802322f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.3499999940395355f);
    }

    public static unsafe string GetCurrentStyleData()
    {
        var style = ImGui.GetStyle();
        var themeData = "";
        var colors = Enum.GetValues<ImGuiCol>();
        foreach (var col in colors) {
            if (col == ImGuiCol.COUNT) continue;
            var color = ImGui.GetColorU32(col);
            themeData += "color " + col + " = " + color.ToString("X", CultureInfo.InvariantCulture) + "\n";
        }

        foreach (var field in AppColors.ColorFields) {
            var col = (Vector4)field.GetValue(Colors.Current)!;
            themeData += "maincol " + field.Name + " = " + ImGui.ColorConvertFloat4ToU32(col).ToString("X", CultureInfo.InvariantCulture) + "\n";
        }

        foreach (var field in ThemeFields) {
            var value = field.GetValue(style);
            string valueStr;
            switch (value) {
                case float flt:
                    valueStr = flt.ToString(CultureInfo.InvariantCulture);
                    break;
                case bool boolean:
                    valueStr = boolean ? "true" : "false";
                    break;
                case Vector2 vec2:
                    valueStr = vec2.X.ToString(CultureInfo.InvariantCulture) + ", " + vec2.Y.ToString(CultureInfo.InvariantCulture);
                    break;
                case ImGuiHoveredFlags hovfl:
                    valueStr = ((int)hovfl).ToString();
                    break;
                case ImGuiDir dir:
                    valueStr = dir.ToString();
                    break;
                default:
                    continue;
            }
            themeData += "prop " + field.Name + " = " + valueStr + "\n";
        }
        return themeData;
    }

    public static unsafe void ApplyThemeData(IEnumerable<(string key, string value, string? group)> ini)
    {
        var style = ImGui.GetStyle();
        Colors.Current = new AppColors();
        foreach (var kv in ini) {
            if (kv.key.StartsWith("color ")) {
                var name = kv.key.Replace("color ", "");
                if (uint.TryParse(kv.value, System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var colU32) && Enum.TryParse<ImGuiCol>(name, out var col)) {
                    style.Colors[(int)col] = ImGui.ColorConvertU32ToFloat4(colU32);
                }
            } else if(kv.key.StartsWith("maincol ")) {
                var name = kv.key.Replace("maincol ", "");
                var field = AppColors.ColorFields.FirstOrDefault(f => f.Name == name);
                if (field != null && uint.TryParse(kv.value, System.Globalization.NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var colU32)) {
                    field.SetValue(Colors.Current, ImGui.ColorConvertU32ToFloat4(colU32));
                }
            } else if (kv.key.StartsWith("prop ")) {
                var name = kv.key.Replace("prop ", "");
                var prop = ThemeFields.FirstOrDefault(ff => ff.Name == name);
                if (prop == null) continue;

                var type = prop.PropertyType.GetElementType();

                if (type == typeof(float)) {
                    if (float.TryParse(kv.value, CultureInfo.InvariantCulture, out var flt)) {
                        ref var val = ref ((floatGetter)Delegate.CreateDelegate(typeof(floatGetter), style, prop.GetMethod!))();
                        val = flt;
                    }
                } else if (type == typeof(bool)) {
                    ref var val = ref ((boolGetter)Delegate.CreateDelegate(typeof(boolGetter), style, prop.GetMethod!))();
                    val = kv.value.Equals("yes", StringComparison.InvariantCultureIgnoreCase) || kv.value.Equals("true", StringComparison.InvariantCultureIgnoreCase);
                } else if (type == typeof(Vector2)) {
                    var vals = kv.value.Split(',');
                    if (float.TryParse(vals[0], CultureInfo.InvariantCulture, out var v1) && float.TryParse(vals[0], CultureInfo.InvariantCulture, out var v2)) {
                        ref var val = ref ((Vector2Getter)Delegate.CreateDelegate(typeof(Vector2Getter), style, prop.GetMethod!))();
                        val = new Vector2(v1, v2);
                    }
                } else if (type == typeof(ImGuiDir)) {
                    if (Enum.TryParse<ImGuiDir>(kv.value, out var dir)) {
                        ref var val = ref ((ImGuiDirGetter)Delegate.CreateDelegate(typeof(ImGuiDirGetter), style, prop.GetMethod!))();
                        val = dir;
                    }
                } else if (type == typeof(ImGuiHoveredFlags)) {
                    if (int.TryParse(kv.value, out var num)) {
                        ref var val = ref ((ImGuiHoveredFlagsGetter)Delegate.CreateDelegate(typeof(ImGuiHoveredFlagsGetter), style, prop.GetMethod!))();
                        val = (ImGuiHoveredFlags)num;
                    }
                }
            }
        }
    }

    private delegate ref Vector2 Vector2Getter();
    private delegate ref float floatGetter();
    private delegate ref bool boolGetter();
    private delegate ref ImGuiDir ImGuiDirGetter();
    private delegate ref ImGuiHoveredFlags ImGuiHoveredFlagsGetter();
}