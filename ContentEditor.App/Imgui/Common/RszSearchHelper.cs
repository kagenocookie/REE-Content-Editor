using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Il2cpp;
using ReeLib.via;
using ZstdSharp.Unsafe;

namespace ContentEditor.App.ImguiHandling;

public class RszSearchHelper : IObjectUIHandler, IFilterRoot
{
    public string? Query { get; private set; }

    private string componentMatch = "";
    private string nameMatch = "";

    public string? QueryError { get; private set; }

    public bool HasFilterActive => !string.IsNullOrEmpty(Query);

    public object? MatchedObject { get; set; }
    private bool shouldDeleteSearch;

    public void SetQuery(string? query)
    {
        if (string.IsNullOrEmpty(query)) {
            componentMatch = nameMatch = "";
            Query = null;
            return;
        }

        Query = query;
        var split = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var compQuery = split.FirstOrDefault(c => c.StartsWith("c:") && c.Length > 2);
        if (compQuery != null) {
            split = split.Except([compQuery]).ToArray();
            componentMatch = compQuery.Substring(2);
        } else {
            componentMatch = "";
        }

        if (split.Length > 1) {
            QueryError = "Multiple names in search are not supported";
        }

        nameMatch = split.Length >= 1 ? split[0] : "";
    }

    public bool IsMatch(object? obj)
    {
        if (obj == null) return false;
        if (string.IsNullOrEmpty(nameMatch) && string.IsNullOrEmpty(componentMatch)) {
            return true;
        }

        if (obj is Folder f) {
            if (!string.IsNullOrEmpty(componentMatch)) return false;

            return nameMatch == null ? true : f.Name.Contains(nameMatch, StringComparison.InvariantCultureIgnoreCase);
        }
        if (obj is GameObject o) {
            if (!string.IsNullOrEmpty(nameMatch) && !o.Name.Contains(nameMatch, StringComparison.InvariantCultureIgnoreCase)) {
                return false;
            }

            if (!string.IsNullOrEmpty(componentMatch) && !o.Components.Any(c => c.Classname.Contains(componentMatch, StringComparison.InvariantCultureIgnoreCase))) {
                return false;
            }

            return true;
        }
        if (obj is RszInstance r) {
            var clsMatcher = !string.IsNullOrEmpty(componentMatch) ? componentMatch : nameMatch!;
            if (r.RszClass.name.Contains(clsMatcher, StringComparison.InvariantCultureIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private bool showSearch;
    public bool ShowToggleButton()
    {
        ImguiHelpers.ToggleButton($"{AppIcons.Search}", ref showSearch, Colors.IconActive);
        return showSearch;
    }

    public bool ShowAdvancedSearchButton()
    {
        ImguiHelpers.ToggleButton($"{AppIcons.SI_Settings}##filter_adv", ref showSearch, Colors.IconActive);
        return showSearch;
    }

    public void ShowFileEditorInline(float indent = 0)
    {
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();

        var avail = ImGui.GetContentRegionAvail().X - ImGui.GetFrameHeightWithSpacing() - ImGui.GetStyle().WindowPadding.X * 2;
        ImGui.SetNextItemWidth(avail);
        ImGui.SameLine();
        ShowFilterInput();
        ImGui.SameLine();
        if (ShowAdvancedSearchButton()) {
            ImGui.Indent(indent);
            ShowAdvancedFilterSettings();
            ImGui.Unindent(indent);
        }
    }

    public void OnIMGUI(UIContext context)
    {
        if (shouldDeleteSearch) {
            MatchedObject = null;
            SetQuery(null);
        }

        if (ShowToggleButton()) {
            ShowFilterInput();
        }
    }

    public void ShowFilterInput()
    {
        var filter = Query ?? "";
        if (ImGui.InputTextWithHint("##Filter", $"{AppIcons.Search}", ref filter, 200)) {
            SetQuery(filter);
        }
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("""
            Search by object name.
            Can use "c:" prefix to search by a GameObject component, e.g. "c:render.mesh"
            """);
        // TODO support field/value filtering
        // Can use "f:" prefix to search by a field name, e.g. "f:itemID"
        // Can use "v:" prefix to search by a field value, e.g. "v:sm34.mesh"
        shouldDeleteSearch = MatchedObject != null;
    }

    public void ShowAdvancedFilterSettings()
    {
        var changed = false;
        if (ImGui.InputText("Name", ref nameMatch, 200)) {
            changed = true;
        }
        if (ImGui.InputText("Component", ref componentMatch, 200)) {
            changed = true;
        }

        if (changed) {
            var matchStr = nameMatch;
            if (!string.IsNullOrEmpty(componentMatch)) {
                matchStr += $" c:{componentMatch}";
            }
            SetQuery(matchStr);
        }
    }
}