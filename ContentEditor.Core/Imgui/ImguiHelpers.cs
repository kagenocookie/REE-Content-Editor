using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace ContentEditor.Core;

public static class ImguiHelpers
{
    private const int MarginX = 4;

    public const string DragDrop_File = "EXT_FILE";
    public const string DragDrop_Text = "EXT_TEXT";

    public static bool TreeNodeSuffix(string label, string suffix, Vector4 color = default)
    {
        ImGui.BeginGroup();
        var show = ImGui.TreeNode(label);
        ImGui.SameLine();
        if (color.W == 0) {
            ImGui.TextColored(Colors.Faded, suffix);
        } else {
            ImGui.TextColored(color, suffix);
        }
        ImGui.EndGroup();
        // hack: doing BeginGroup means the indent doesn't apply, but we need the group if we want to have a context menu trigger on both node and suffix
        // doing a manual indent fixes that
        if (show) ImGui.Indent(ImGui.GetStyle().IndentSpacing);
        return show;
    }

    public static void TextSuffix(string label, string suffix, Vector4 color = default)
    {
        ImGui.Text(label);
        ImGui.SameLine();
        if (color.W == 0) {
            ImGui.TextColored(Colors.Faded, suffix);
        } else {
            ImGui.TextColored(color, suffix);
        }
    }

    public static void ApplyDisplaySettingsPreLabel(FieldDisplaySettings settings)
    {
        if (settings.marginBefore != 0) {
            for (int i = 0; i < settings.marginBefore; ++i) ImGui.Spacing();
        }
    }

    public static void ApplyDisplaySettingsPostLabel(FieldDisplaySettings settings)
    {
        if (settings.tooltip != null && ImGui.IsItemHovered()) {
            ImGui.SetItemTooltip(settings.tooltip);
        }
    }

    public static void ApplyDisplaySettingsPostField(FieldDisplaySettings settings)
    {
        if (settings.marginAfter != 0) {
            for (int i = 0; i < settings.marginAfter; ++i) ImGui.Spacing();
        }
    }

    public static bool CSharpEnumCombo<TEnum>(string label, ref TEnum selected, int height = -1) where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var labels = Enum.GetNames<TEnum>();
        var selectedIndex = Array.IndexOf(values, selected);
        if (ImGui.Combo(label, ref selectedIndex, labels, labels.Length, height)) {
            selected = values[selectedIndex];
            return true;
        }
        return false;
    }
    public static bool FilterableCSharpEnumCombo<TEnum>(string label, ref TEnum selected, ref string? filter) where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var labels = Enum.GetNames<TEnum>();
        if (FilterableCombo(label, labels, values, ref selected, ref filter)) {
            return true;
        }
        return false;
    }
    public static bool EnumCombo<TEnumSource>(string label, TEnumSource enumDescriptor, ref object? selected)
        where TEnumSource : IEnumDataSource
    {
        var labels = enumDescriptor.GetLabels();
        var values = enumDescriptor.GetValues();
        // could be optimized with a dictionary lookup instead of array index - but we can add that later
        var selectedIndex = Array.IndexOf(values, selected);
        if (ImGui.Combo(label, ref selectedIndex, labels, labels.Length)) {
            selected = values[selectedIndex];
            return true;
        }
        return false;
    }

    public static bool InlineRadioGroup<TValue>(string[] labels, TValue[] values, ref TValue selected) where TValue : unmanaged, IBinaryNumber<TValue>
    {
        var changed = false;
        for (int i = 0; i < labels.Length; ++i) {
            if (i != 0) ImGui.SameLine();
            if (ImGui.RadioButton(labels[i], values[i].Equals(selected))) {
                changed = true;
                selected = values[i];
            }
        }
        return changed;
    }

    public static bool ValueCombo<TValue>(string label, string[] labels, TValue[] values, ref TValue selected, int height = -1)
    {
        var selectedIndex = Array.IndexOf(values, selected);
        if (ImGui.Combo(label, ref selectedIndex, labels, labels.Length, height)) {
            selected = values[selectedIndex];
            return true;
        }
        return false;
    }

    public static bool FilterableEnumCombo<TEnumSource>(string label, TEnumSource enumDescriptor, ref object? selected, ref string? filter)
        where TEnumSource : IEnumDataSource
    {
        var labels = enumDescriptor.GetLabels();
        var values = enumDescriptor.GetValues();
        return FilterableCombo(label, labels, values, ref selected, ref filter);
    }

    public static bool FilterableEntityCombo<TEntityBaseType>(string label, IEnumerable<KeyValuePair<long, TEntityBaseType>> entities, ref long selected, ref string? filter)
        where TEntityBaseType : Entity
    {
        var labels = entities.Select(kv => kv.Value.Label).ToArray();
        var values = entities.Select(kv => kv.Key).ToArray();
        return FilterableCombo(label, labels, values, ref selected, ref filter);
    }

    private static int BoxedIndexOf<T>(this ReadOnlySpan<T> span, T value)
    {
        int i = 0;
        foreach (var item in span) {
            if (item == null && value == null || item?.Equals(value) == true) return i;
            i++;
        }
        return -1;
    }

    public static bool FilterableCombo<TValue>(string label, string[] labels, ReadOnlySpan<TValue> values, ref TValue? selected, ref string? filter)
    {
        ImGui.BeginGroup();
        var w = ImGui.CalcItemWidth();
        ImGui.SetNextItemWidth(w / 2 - MarginX);
        bool changed = false;
        if (string.IsNullOrEmpty(filter)) {
            // could be optimized with a dictionary lookup instead of array index - but we can add that later
            // note: we can't add a where TValue : IEquatable<TValue> constraint to use the built in Span.IndexOf() because enum values are boxed
            var selectedIndex = values!.BoxedIndexOf(selected);
            if (ImGui.Combo($"##{label}", ref selectedIndex, labels, labels.Length, 1000)) {
                changed = true;
                selected = values[selectedIndex];
            }
        } else {
            // could be optimized by storing a per-filter cache on the enum descriptor, but we probably don't care since it's just UI anyway
            var filteredLabels = new List<string>();
            var filteredValues = new List<TValue>();
            for (int i = 0; i < labels.Length; i++) {
                var item = labels[i];
                if (item.Contains(filter, StringComparison.InvariantCultureIgnoreCase)) {
                    filteredLabels.Add(item);
                    filteredValues.Add(values[i]);
                }
            }
            var selectedIndex = selected == null ? -1 : filteredValues.IndexOf(selected);
            if (ImGui.Combo($"##{label}", ref selectedIndex, filteredLabels.ToArray(), filteredLabels.Count, 1000)) {
                changed = true;
                selected = filteredValues[selectedIndex];
            }
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(w / 2 - MarginX);
        filter ??= "";
        ImGui.InputText(label, ref filter, 48);
        ImGui.EndGroup();
        return changed;
    }

    public static bool Tabs(string[] tabs, ref int selectedTabIndex, bool inline = false, string? header = null)
    {
        var changed = false;
        if (!inline) {
            ImGui.Spacing();
            ImGui.Indent(16);
        }
        var startX = ImGui.GetCursorPosX();
        var endX = ImGui.GetWindowSize().X;
        var totalPadding = startX * 2;
        var w_total = endX - totalPadding;
        BeginRect();
        if (header != null) {
            ImGui.Text(header);
            if (inline) ImGui.SameLine();
        }
        var tabMargin = ImGui.GetStyle().FramePadding.X * 4;
        var x = 0f;
        for (int i = 0; i < tabs.Length; ++i) {
            var tab = tabs[i];
            var tabWidth = ImGui.CalcTextSize(tab).X + tabMargin;
            if (i > 0) {
                if (x + tabWidth >= w_total) {
                    x = 0;
                } else {
                    ImGui.SameLine();
                }
            }
            x += tabWidth;

            if (i == selectedTabIndex) {
                ImGui.BeginDisabled(true);
                ImGui.Text(tab);
                ImGui.EndDisabled();
            } else {
                if (ImGui.Button(tab)) {
                    selectedTabIndex = i;
                    changed = true;
                }
            }
        }
        EndRect(4);
        if (!inline) {
            ImGui.Unindent(16);
            ImGui.Spacing();
        }
        return changed;
    }

    public static void BeginRect()
    {
        ImGui.BeginGroup();
    }
    public static void EndRect(int additionalSize = 0)
    {
        ImGui.EndGroup();
        var min = ImGui.GetItemRectMin() - new Vector2(additionalSize);
        var max = ImGui.GetItemRectMax() + new Vector2(additionalSize);

        ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border), ImGui.GetStyle().FrameRounding, ImDrawFlags.RoundCornersAll, 1.0f);
    }

    public static bool BeginWindow(WindowData data, string? name = null, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
    {
        ImGui.SetNextWindowSize(data.Size, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(data.Position, ImGuiCond.FirstUseEver);
        if (data.Context?.Changed == true) {
            flags |= ImGuiWindowFlags.UnsavedDocument;
        }
        var open = true;
        ImGui.Begin(name ?? data.Name ?? $"{data.Handler}##{data.ID}", ref open, flags);
        data.Size = ImGui.GetWindowSize();
        data.Position = ImGui.GetWindowPos();
        if (!open) {
            ImGui.End();
            return false;
        }
        return true;
    }

    public static unsafe GCHandle SetDragDropPayload<T>(string payloadType, T target) where T : class
    {
        var handle = GCHandle.Alloc(target);
        ImGui.SetDragDropPayload("BUNDLE", (IntPtr)handle, (uint)sizeof(IntPtr));
        return handle;
    }

    public static unsafe bool InputScalar<TVal>(string label, ImGuiDataType type, ref TVal value) where TVal : unmanaged, INumber<TVal>
    {
        var num = value;
        if (ImGui.DragScalar(label, type, (IntPtr)(&num))) {
            value = num;
            return true;
        }
        return false;
    }

    public static void ScrollItemIntoView(float extraPadding = 4)
    {
        if (!ImGui.IsItemVisible()) {
            var itemY = ImGui.GetItemRectSize().Y + ImGui.GetStyle().FramePadding.Y * 2 + extraPadding;
            var scrollY = ImGui.GetScrollY();
            var curY = ImGui.GetCursorPosY() - itemY;
            if (curY < scrollY) {
                ImGui.SetScrollHereY(0);
            } else {
                ImGui.SetScrollHereY(1);
            }
        }
    }

    public static void TextCentered(string text, float fullSize = -1)
    {
        if (fullSize <= 0) {
            fullSize = ImGui.GetWindowSize().X - ImGui.GetStyle().WindowPadding.X * 2;
        }
        var w = ImGui.CalcTextSize(text).X;
        ImGui.Indent(fullSize / 2 - w / 2);
        ImGui.Text(text);
        ImGui.Unindent(fullSize / 2 - w / 2);
    }

    public static bool TextMultilineAutoResize(string label, ref string text, float width, float maxHeight, int fontsize, uint maxLen = 2048, ImGuiInputTextFlags flags = ImGuiInputTextFlags.AllowTabInput)
    {
        var lineCount = text.Count(s => s == '\n') + 1;
        var requiredHeight = lineCount * (fontsize) + ImGui.GetStyle().FramePadding.Y * 2;
        if (ImGui.InputTextMultiline(label, ref text, maxLen, new System.Numerics.Vector2(width, Math.Min(requiredHeight, maxHeight)), flags)) {
            return true;
        }
        return false;
    }

    public static void TextColoredWrapped(Vector4 color, ReadOnlySpan<char> text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    public static DisposableColorOverride OverrideStyleCol(ImGuiCol style, uint color)
    {
        ImGui.PushStyleColor(style, color);
        return new DisposableColorOverride();
    }
    public static DisposableColorOverride OverrideStyleCol(ImGuiCol style, Vector4 color)
    {
        ImGui.PushStyleColor(style, color);
        return new DisposableColorOverride();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DisposableDisableOverride Disabled(bool disabled)
    {
        return new DisposableDisableOverride(disabled);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 GetColor(ImGuiCol style)
    {
        return ImGui.GetStyle().Colors[(int)style];
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetColorU32(ImGuiCol style)
    {
        return ImGui.ColorConvertFloat4ToU32(ImGui.GetStyle().Colors[(int)style]);
    }

    public struct DisposableStyleOverride() : IDisposable
    {
        public void Dispose() => ImGui.PopStyleVar();
    }
    public struct DisposableColorOverride() : IDisposable
    {
        public void Dispose() => ImGui.PopStyleColor();
    }
    public struct DisposableDisableOverride : IDisposable
    {
        private readonly bool disabled;

        public DisposableDisableOverride(bool disabled)
        {
            this.disabled = disabled;
            if (disabled) ImGui.BeginDisabled();
        }

        public void Dispose()
        {
            if (disabled) ImGui.EndDisabled();
        }
    }

    public static DisposableImguiID ScopedID(int id)
    {
        ImGui.PushID(id);
        return new DisposableImguiID();
    }

    public static DisposableImguiID ScopedID(string id)
    {
        ImGui.PushID(id);
        return new DisposableImguiID();
    }

    public struct DisposableImguiID : IDisposable
    {
        public void Dispose()
        {
            ImGui.PopID();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool SameLine()
    {
        ImGui.SameLine();
        return true;
    }

    public static bool SelectableSuffix(string text, string? suffix, bool selected)
    {
        if (suffix == null) return ImGui.Selectable(text, selected);

        ImGui.BeginGroup();
        var select = ImGui.Selectable(text, selected);
        ImGui.SameLine();
        ImGui.TextColored(Colors.Faded, suffix);
        ImGui.EndGroup();
        return select;
    }
    /// <summary>
    /// Draws an imgui button that behaves like a checkbox.
    /// </summary>
    public static bool ToggleButton(string label, ref bool value, Vector4? color = null, float frameSize = 0f)
    {
        var changed = false;
        if (value && color.HasValue) {
            ImGui.PushStyleColor(ImGuiCol.Text, color.Value);
            ImGui.PushStyleColor(ImGuiCol.Border, color.Value);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, frameSize);

            if (ImGui.Button(label)) {
                value = !value;
                changed = true;
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(2);
        } else {
            if (ImGui.Button(label)) {
                value = !value;
                changed = true;
            }
        }
        return changed;
    }
    /// <summary>
    /// Draws a tooltip when item is hovered.
    /// </summary>
    public static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip(text);
    }
    /// <summary>
    /// Draws a colored tooltip when item is hovered.
    /// </summary>
    public static void TooltipColored(string text, Vector4? color = null)
    {
        color ??= ImguiHelpers.GetColor(ImGuiCol.Text);
        ImGui.PushStyleColor(ImGuiCol.Text, color.Value);
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip(text);
        ImGui.PopStyleColor();
    }
    /// <summary>
    /// Draws a highlight over the last drawn menu bar item.
    /// </summary>
    public static void HighlightMenuItem(string text, Vector4? color = null)
    {
        color ??= ImguiHelpers.GetColor(ImGuiCol.TabSelected) with { W = 0.25f };
        var padding = ImGui.GetStyle().FramePadding;
        var spacing = ImGui.GetStyle().ItemSpacing;
        var size = ImGui.CalcTextSize(text) + new Vector2(spacing.X * 2, padding.Y * 2);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(
            pos + new Vector2(-padding.X - size.X + 1, 0),
            pos + new Vector2(-padding.X, size.Y),
            ImGui.ColorConvertFloat4ToU32(color.Value)
        );
    }
    /// <summary>
    /// Pushes element(s) to the right side of the window.
    /// </summary>
    public static void AlignElementRight(float width)
    {
        float avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - width);
    }
}
