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

    private string? componentMatch;
    private string? nameMatch;

    public string? QueryError { get; private set; }

    public bool HasFilterActive => !string.IsNullOrEmpty(Query);

    public object? MatchedObject { get; set; }
    private bool shouldDeleteSearch;

    public void SetQuery(string? query)
    {
        if (string.IsNullOrEmpty(query)) {
            componentMatch = nameMatch = null;
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
            componentMatch = null;
        }

        if (split.Length > 1) {
            QueryError = "Multiple names in search are not supported";
        }

        nameMatch = split.Length >= 1 ? split[0] : null;
    }

    public bool IsMatch(object? obj)
    {
        if (obj == null) return false;
        if (string.IsNullOrEmpty(nameMatch) && string.IsNullOrEmpty(componentMatch)) {
            return true;
        }

        if (obj is Folder f) {
            if (componentMatch != null) return false;

            return nameMatch == null ? true : f.Name.Contains(nameMatch, StringComparison.InvariantCultureIgnoreCase);
        }
        if (obj is GameObject o) {
            if (nameMatch != null && !o.Name.Contains(nameMatch, StringComparison.InvariantCultureIgnoreCase)) {
                return false;
            }

            if (componentMatch != null && !o.Components.Any(c => c.Classname.Contains(componentMatch, StringComparison.InvariantCultureIgnoreCase))) {
                return false;
            }

            return true;
        }
        if (obj is RszInstance r) {
            var clsMatcher = componentMatch ?? nameMatch!;
            if (r.RszClass.name.Contains(clsMatcher, StringComparison.InvariantCultureIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    public void OnIMGUI(UIContext context)
    {
        if (shouldDeleteSearch) {
            MatchedObject = null;
            SetQuery(null);
        }

        var filter = Query ?? "";
        if (ImGui.InputText("Filter", ref filter, 200)) {
            SetQuery(filter);
        }
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Search by object name.\nCan use \"c:\" prefix to search by a GameObject component, e.g. \"c:render.mesh\"");
        shouldDeleteSearch = MatchedObject != null;
    }
}