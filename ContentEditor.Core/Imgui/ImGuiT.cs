using System.Numerics;
using Hexa.NET.ImGui;

namespace ContentEditor.Core;

/// <summary>Localized ImGui presentation text. Use ImGui directly for game and user data.</summary>
public static class ImGuiT
{
    public static void Text(string text) => ImGui.Text(UiText.T(text));
    public static void TextFormatted(FormattableString text) => ImGui.Text(UiText.F(text));
    public static void TextUnformatted(string text) => ImGui.TextUnformatted(UiText.T(text));
    public static void TextUnformattedFormatted(FormattableString text) => ImGui.TextUnformatted(UiText.F(text));
    public static void TextWrapped(string text) => ImGui.TextWrapped(UiText.T(text));
    public static void TextWrappedFormatted(FormattableString text) => ImGui.TextWrapped(UiText.F(text));
    public static void TextDisabled(string text) => ImGui.TextDisabled(UiText.T(text));
    public static void TextDisabledFormatted(FormattableString text) => ImGui.TextDisabled(UiText.F(text));
    public static void SeparatorText(string text) => ImGui.SeparatorText(UiText.T(text));
    public static void SeparatorTextFormatted(FormattableString text) => ImGui.SeparatorText(UiText.F(text));
    public static void SetTooltip(string text) => ImGui.SetTooltip(UiText.T(text));
    public static void SetTooltipFormatted(FormattableString text) => ImGui.SetTooltip(UiText.F(text));
    public static void SetItemTooltip(string text) => ImGui.SetItemTooltip(UiText.T(text));
    public static void SetItemTooltipFormatted(FormattableString text) => ImGui.SetItemTooltip(UiText.F(text));
    public static void TextColored(Vector4 color, string text) => ImGui.TextColored(color, UiText.T(text));
    public static void TextColoredFormatted(Vector4 color, FormattableString text) => ImGui.TextColored(color, UiText.F(text));
}
