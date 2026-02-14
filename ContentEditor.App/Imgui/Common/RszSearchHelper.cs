using ContentEditor.Core;
using ReeLib;
using ReeLib.via;

namespace ContentEditor.App.ImguiHandling;

public class RszSearchHelper : IObjectUIHandler, IFilterRoot
{
    public string? Query { get; private set; }

    private string classnameMatch = "";
    private bool classnameComponentOnly;
    private string nameMatch = "";
    private string valueMatch = "";
    private string fieldMatch = "";

    public string? QueryError { get; private set; }

    public bool HasFilterActive => !string.IsNullOrEmpty(Query);

    public object? MatchedObject { get; set; }
    private bool shouldDeleteSearch;

    public void SetQuery(string? query)
    {
        if (string.IsNullOrEmpty(query)) {
            classnameMatch = nameMatch = "";
            Query = null;
            return;
        }

        Query = query;
        classnameMatch = "";
        nameMatch = "";
        valueMatch = "";
        fieldMatch = "";

        var split = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in split) {
            if (part.StartsWith("c:")) {
                classnameMatch = part.Substring(2);
                classnameComponentOnly = false;
            } else if (part.StartsWith("cc:")) {
                classnameMatch = part.Substring(3);
                classnameComponentOnly = true;
            } else if (part.StartsWith("v:")) {
                valueMatch = part.Substring(2);
            } else if (part.StartsWith("f:")) {
                fieldMatch = part.Substring(2);
            } else if (string.IsNullOrEmpty(nameMatch)) {
                nameMatch = part;
            } else {
                nameMatch += " " + part;
            }
        }
    }

    public bool IsMatch(object? obj)
    {
        if (obj == null) return false;
        if (string.IsNullOrEmpty(nameMatch) && string.IsNullOrEmpty(classnameMatch) && string.IsNullOrEmpty(valueMatch) && string.IsNullOrEmpty(fieldMatch)) {
            return true;
        }

        if (obj is Folder f) {
            if (!string.IsNullOrEmpty(classnameMatch)) return false;

            if (!string.IsNullOrEmpty(nameMatch) && !f.Name.Contains(nameMatch, StringComparison.InvariantCultureIgnoreCase)) {
                return false;
            }

            if (!string.IsNullOrEmpty(valueMatch) && (string.IsNullOrEmpty(classnameMatch) || CompareSubstring("via.Folder", classnameMatch))) {
                return TryMatchRszInstanceValue(f.Instance, valueMatch, fieldMatch);
            } else if (!string.IsNullOrEmpty(fieldMatch)) {
                // TODO field name only check
            }

            return true;
        }
        if (obj is GameObject o) {
            if (!string.IsNullOrEmpty(nameMatch)) {
                if (!o.Name.Contains(nameMatch, StringComparison.InvariantCultureIgnoreCase) && (!Guid.TryParse(nameMatch, out var guid) || o.guid != guid)) {
                    return false;
                }
            }

            var defaultShow = true;
            if (!string.IsNullOrEmpty(classnameMatch)) {
                var hasComponentMatch = o.Components.Any(c => c.Classname.Contains(classnameMatch, StringComparison.InvariantCultureIgnoreCase));
                if (classnameComponentOnly && !hasComponentMatch) {
                    return false;
                }

                defaultShow = hasComponentMatch;
            }

            if (!string.IsNullOrEmpty(valueMatch)) {
                return TryMatchGameObjectRszValue(o, valueMatch, fieldMatch);
            }

            return defaultShow;
        }
        if (obj is RszInstance r) {
            var clsMatcher = !string.IsNullOrEmpty(classnameMatch) ? classnameMatch : nameMatch!;
            if (!string.IsNullOrEmpty(clsMatcher)) {
                if (!r.RszClass.name.Contains(clsMatcher, StringComparison.InvariantCultureIgnoreCase)) {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(valueMatch) && (string.IsNullOrEmpty(classnameMatch))) {
                return TryMatchRszInstanceValue(r, valueMatch, fieldMatch);
            }

            return true;
        }

        return false;
    }

    private bool TryMatchGameObjectRszValue(GameObject gameObject, string value, string? fieldFilter)
    {
        if (gameObject.Instance != null
            && (string.IsNullOrEmpty(classnameMatch) || CompareSubstring("via.GameObject", classnameMatch))
            && TryMatchRszInstanceValue(gameObject.Instance, valueMatch, fieldFilter)) {
            return true;
        }

        foreach (var comp in gameObject.Components) {
            if (classnameComponentOnly && !string.IsNullOrEmpty(classnameMatch) && !CompareSubstring(comp.Classname, classnameMatch)) {
                continue;
            }

            if (TryMatchRszInstanceValue(comp.Data, value, fieldFilter)) {
                return true;
            }
        }

        return false;
    }

    private bool TryMatchRszInstanceValue(RszInstance instance, string value, string? fieldFilter)
    {
        if (instance.Values.Length == 0) return false;

        var fields = instance.RszClass.fields;
        bool isInsideAllowedClassname = true;
        if (!classnameComponentOnly && !string.IsNullOrEmpty(classnameMatch)) {
            isInsideAllowedClassname = CompareSubstring(instance.RszClass.name, classnameMatch);
        }

        for (int i = 0; i < fields.Length; ++i) {
            var type = fields[i].type;

            if (!string.IsNullOrEmpty(fieldFilter) && !(type is RszFieldType.Object or RszFieldType.Struct)) {
                if (!fields[i].name.Contains(fieldFilter, StringComparison.InvariantCultureIgnoreCase)) continue;
            }

            if (fields[i].array) {
                var list = (List<object>)instance.Values[i];
                foreach (var v in list) {
                    if (TryMatchRszSingleValue(v, type, value, fieldFilter, isInsideAllowedClassname)) {
                        return true;
                    }
                }
            } else {
                if (TryMatchRszSingleValue(instance.Values[i], type, value, fieldFilter, isInsideAllowedClassname)) {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryMatchRszSingleValue(object value, RszFieldType type, string comparison, string? fieldFilter, bool isInsideAllowedClassname)
    {
        if (type is RszFieldType.Object or RszFieldType.Struct) {
            return TryMatchRszInstanceValue((RszInstance)value, comparison, fieldFilter);
        }

        if (!isInsideAllowedClassname) {
            return false;
        }

        switch (type) {
            case RszFieldType.Guid: {
                    return Guid.TryParse(comparison, out var g) && (Guid)value == g;
                }
            case RszFieldType.GameObjectRef:
            case RszFieldType.Uri: {
                    return Guid.TryParse(comparison, out var g) && ((GameObjectRef)value).guid == g;
                }
            case RszFieldType.U8:
            case RszFieldType.S8:
            case RszFieldType.U16:
            case RszFieldType.S16:
            case RszFieldType.U32:
            case RszFieldType.S32:
            case RszFieldType.S64: {
                    return long.TryParse(comparison, out var s64) && Convert.ToInt64(value) == s64;
                }
            case RszFieldType.U64: {
                    return ulong.TryParse(comparison, out var u64) && (ulong)value == u64;
                }
            case RszFieldType.String:
            case RszFieldType.Resource:
            case RszFieldType.RuntimeType:
                return CompareSubstring((string)value, comparison);
            case RszFieldType.F32: {
                    return float.TryParse(comparison, out var f) && Math.Abs((float)value - f) < 0.0001f;
                }
            case RszFieldType.F64: {
                    return double.TryParse(comparison, out var f) && Math.Abs((double)value - f) < 0.0001;
                }
        }

        return false;
    }

    private bool CompareSubstring(string text, string substr)
    {
        // TODO case sensitivity, full match, regex setting
        return text.Contains(substr, StringComparison.InvariantCultureIgnoreCase);
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
        if (ImGui.InputTextWithHint("##Filter"u8, $"{AppIcons.Search}", ref filter, 200)) {
            SetQuery(filter);
        }
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("""
            Search by object name.
            Can use "c:" prefix to search by a GameObject component, e.g. "c:render.mesh"
            Can use "v:" prefix to search by a field value, e.g. "v:sm34.mesh"
            Can use "f:" prefix to filter values by field name, e.g. "f:itemID"
            """u8);

        shouldDeleteSearch = MatchedObject != null;
    }

    public void ShowAdvancedFilterSettings()
    {
        var changed = false;
        changed |= ImGui.InputText("Name"u8, ref nameMatch, 200);
        changed |= ImGui.InputText("##Classname"u8, ref classnameMatch, 200);

        ImGui.SameLine();
        changed |= ImguiHelpers.ToggleButton($"{AppIcons.SI_FileType_CFIL}", ref classnameComponentOnly, Colors.IconActive);
        ImguiHelpers.Tooltip("""
            Match GameObject components only.
            Otherwise, will consider it a match if any nested object has a matching classname.
            """u8);
        ImGui.SameLine();
        ImGui.Text("Classname");

        changed |= ImGui.InputText("Field Name"u8, ref fieldMatch, 200);
        changed |= ImGui.InputText("Value"u8, ref valueMatch, 200);
        if (changed) {
            var matchStr = nameMatch;
            if (!string.IsNullOrEmpty(classnameMatch)) {
                matchStr += classnameComponentOnly ? $" cc:{classnameMatch}" : $" c:{classnameMatch}";
            }
            if (!string.IsNullOrEmpty(valueMatch)) {
                matchStr += $" v:{valueMatch}";
            }
            if (!string.IsNullOrEmpty(fieldMatch)) {
                matchStr += $" f:{fieldMatch}";
            }
            SetQuery(matchStr);
        }
    }
}