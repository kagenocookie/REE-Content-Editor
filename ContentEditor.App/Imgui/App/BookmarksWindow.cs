using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using System.Numerics;

namespace ContentEditor.App;

public class BookmarksWindow : IWindowHandler
{
    public string HandlerName => "Bookmarks";
    bool IWindowHandler.HasUnsavedChanges => false;
    protected UIContext context = null!;
    private readonly BookmarksPanel panel;

    public BookmarksWindow(BookmarksPanel panel)
    {
        this.panel = panel;
    }
    public void Init(UIContext context)
    {
        this.context = context;
    }
    public void OnWindow() => this.ShowDefaultWindow(context, flags: ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

    public void OnIMGUI() => panel.ShowBookmarks();

    public bool RequestClose()
    {
        return false;
    }
}
public class BookmarksPanel
{
    private enum FilterMode
    {
        AnyMatch,
        AllMatch,
        ExactMatch
    }

    private readonly ContentWorkspace contentWorkspace;
    private readonly BookmarkHolder _bookmarks;
    private readonly Action<string> _navigateTo;

    private List<string> _activeTagFilter = new();
    private string bookmarkSearch = string.Empty;
    private bool isBookmarkSearchMatchCase = false;
    private string customBookmarkComment = "";
    private string? editingCustomBookmark = null;
    private FilterMode _filterMode = FilterMode.AnyMatch;

    public BookmarksPanel(ContentWorkspace contentWorkspace, BookmarkHolder bookmarks, Action<string> navigateTo)
    {
        this.contentWorkspace = contentWorkspace;
        this._bookmarks = bookmarks;
        this._navigateTo = navigateTo;
    }

    private Workspace Workspace => contentWorkspace.Env;

    public void ShowBookmarks()
    {        
        bool isHideDefaults = _bookmarks.Defaults.IsHideBookmarks;
        bool isHideCustoms = _bookmarks.User.IsHideBookmarks;

        ImguiHelpers.ToggleButtonMultiColor(AppIcons.SIC_BookmarkHide, ref isHideDefaults, [Colors.IconSecondary, Colors.IconPrimary, Colors.IconPrimary], Colors.IconActive);
        ImguiHelpers.Tooltip(UiText.Utf8("Hide Default Bookmarks"));
        _bookmarks.Defaults.IsHideBookmarks = isHideDefaults;
        using (var _ = ImguiHelpers.Disabled(_bookmarks.User.GetBookmarks(Workspace.Config.Game.name).Count == 0)) {
            ImGui.SameLine();
            ImguiHelpers.ToggleButtonMultiColor(AppIcons.SIC_BookmarkCustomHide, ref isHideCustoms, [Colors.IconSecondary, Colors.IconPrimary, Colors.IconPrimary], Colors.IconActive);
            ImguiHelpers.Tooltip(UiText.Utf8("Hide Custom Bookmarks"));
            _bookmarks.User.IsHideBookmarks = isHideCustoms;
            ImGui.SameLine();
            if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_BookmarkCustomClear, [Colors.IconPrimary, Colors.IconTertiary])) {
                ImGui.OpenPopup("Confirm Action"u8);
            }
            ImguiHelpers.Tooltip(UiText.T("Clear Custom Bookmarks"));
            AppImguiHelpers.ShowActionModal(Lang.General.ConfirmTitle, $"{AppIcons.SI_GenericDelete2}", Colors.IconTertiary,
                Lang.PakBrowser.ConfirmDeleteBookmarks.FormatRef(Lang.TranslateGame(Workspace.Config.Game.name)),
                () => {
                    _bookmarks.User.ClearBookmarks(Workspace.Config.Game.name);
                    Logger.Info($"Cleared custom bookmarks for {Workspace.Config.Game.name}");
                }
            );
        }
        ImGui.SameLine();

        string filterModeName = UiText.T(_filterMode switch {
            FilterMode.AnyMatch => "Any",
            FilterMode.AllMatch => "All",
            FilterMode.ExactMatch => "Exact",
            _ => "?"
        });
        string filterLabelDisplayText = _activeTagFilter.Count == 0 ? $"{AppIcons.SI_Filter}" : $"{AppIcons.SI_Filter} : " + _activeTagFilter.Count.ToString();
        Vector2 filterLabelSize = ImGui.CalcTextSize(filterLabelDisplayText);
        float filterComboWidth = filterLabelSize.X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X + ImGui.GetFontSize();
        float searchBarWidth = 260f;
        ImguiHelpers.AlignElementRight(((ImGui.CalcTextSize($"{AppIcons.SI_GenericClose}").X + ImGui.GetStyle().FramePadding.X * 2) + ImGui.GetStyle().ItemSpacing.X) * 2 + filterComboWidth + searchBarWidth + ImGui.GetStyle().ItemSpacing.X);
        ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericMatchCase}", ref isBookmarkSearchMatchCase, Colors.IconActive);
        ImguiHelpers.Tooltip(UiText.T("Match Case"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(searchBarWidth);
        AppImguiHelpers.ClearableInputText("##BookmarkSearch"u8, UiText.F($"{AppIcons.SI_GenericMagnifyingGlass} Search Comments"), ref bookmarkSearch, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(filterComboWidth);
        if (ImGui.BeginCombo("##TagFilterCombo"u8, filterLabelDisplayText, ImGuiComboFlags.HeightLargest)) {
            ImGui.TextDisabled(UiText.T("Filter Mode:"));
            ImGui.SameLine();

            if (ImGui.SmallButton(filterModeName)) {
                _filterMode = _filterMode switch {
                    FilterMode.AnyMatch => FilterMode.AllMatch,
                    FilterMode.AllMatch => FilterMode.ExactMatch,
                    FilterMode.ExactMatch => FilterMode.AnyMatch,
                    _ => FilterMode.AnyMatch
                };
            }

            if (ImGui.BeginItemTooltip()) {
                ImGui.SeparatorText(UiText.T("Filter Modes"));
                ImGui.BulletText(UiText.T("Any: Keep entries with at least one matching tag"));
                ImGui.BulletText(UiText.T("All: Keep entries containing all active tags"));
                ImGui.BulletText(UiText.T("Exact: Keep entries with tags exactly matching the active filters"));
                ImGui.EndTooltip();
            }
            ImGui.Separator();

            foreach (var tag in BookmarkManager.TagInfoMap.Keys) {
                bool isSelected = _activeTagFilter.Contains(tag);
                if (ImGui.Checkbox(tag, ref isSelected)) {
                    if (isSelected) {
                        _activeTagFilter.Add(tag);
                    } else {
                        _activeTagFilter.Remove(tag);
                    }
                }
            }
            ImGui.EndCombo();
        }
        ImguiHelpers.Tooltip(UiText.T("Filters"));
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(_activeTagFilter.Count == 0)) {
            if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FilterClear, [Colors.IconTertiary, Colors.IconPrimary])) {
                _activeTagFilter.Clear();
            }
            ImguiHelpers.Tooltip(UiText.T("Clear Filters"));
        }
        bool showDefaultTable = _bookmarks.Defaults.GetBookmarks(Workspace.Config.Game.name).Count > 0 && !_bookmarks.Defaults.IsHideBookmarks;
        bool showCustomTable = _bookmarks.User.GetBookmarks(Workspace.Config.Game.name).Count > 0 && !_bookmarks.User.IsHideBookmarks;
        float windowSplitHeight = 50f;
        if (AppConfig.Instance.UseBookmarkWindow) {
            int visibleTables = (showDefaultTable ? 1 : 0) + (showCustomTable ? 1 : 0);
            if (visibleTables > 0) {
                float perTableHeader = ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2;
                windowSplitHeight = (ImGui.GetContentRegionAvail().Y - perTableHeader * visibleTables) / visibleTables;
                windowSplitHeight = Math.Max(windowSplitHeight, 0f);
            }
        }
        if (showDefaultTable) {
            ShowBookmarksTable("Default", 3, _bookmarks.Defaults, _activeTagFilter, bookmarkSearch.Trim(), windowSplitHeight);
        }
        if (showCustomTable) {
            ShowBookmarksTable("Custom", 4, _bookmarks.User, _activeTagFilter, bookmarkSearch.Trim(), windowSplitHeight);
        }
    }

    private void ShowBookmarksTable(string label, int columnNum, BookmarkManager manager, List<string> activeTagFilter, string searchText, float windowSplitHeight = 0f)
    {
        var bookmarks = manager.GetBookmarks(Workspace.Config.Game.name);
        if (bookmarks.Count == 0) return;

        var useCompactFilePaths = AppConfig.Instance.UsePakCompactFilePaths.Get();
        var filteredBookmarks = FilterBookmarks(bookmarks.ToList(), activeTagFilter, searchText, _filterMode);

        if (filteredBookmarks.Count == 0) {
            ImGui.SeparatorText(label + UiText.T(" [No Matches Found]"));
            return;
        }

        float rowHeight = ImGui.GetTextLineHeightWithSpacing();
        bool isWindowMode = AppConfig.Instance.UseBookmarkWindow;
        float currDisplayHeight = isWindowMode ? windowSplitHeight : (filteredBookmarks.Count <= 5 ? 0 : rowHeight * 10);

        var childFlags = isWindowMode ? ImGuiChildFlags.None : ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.AutoResizeY;
        var bookmarkTableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterV | ImGuiTableFlags.RowBg;
        if (filteredBookmarks.Count >= 5) {
            bookmarkTableFlags |= ImGuiTableFlags.ScrollY;
        }
        ImGui.SeparatorText(label);
        if (ImGui.BeginChild($"{label}_Scroll", new Vector2(0, currDisplayHeight), childFlags)) {
            if (ImGui.BeginTable($"{label}Table", columnNum, bookmarkTableFlags)) {
                ImGui.TableSetupColumn(UiText.LabelUtf8("Path"), ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn(UiText.LabelUtf8("Tags"), ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn(UiText.LabelUtf8("Comment"), ImGuiTableColumnFlags.WidthStretch);
                if (manager == _bookmarks.User) {
                    ImGui.TableSetupColumn(UiText.LabelUtf8("Order"), ImGuiTableColumnFlags.WidthFixed, ((ImGui.GetFrameHeight() * 2f) + ImGui.GetStyle().ItemSpacing.X * 3f));
                }
                ImGui.TableHeadersRow();

                foreach (var bm in filteredBookmarks) {
                    string displayPath = useCompactFilePaths ? LessCompactFilePath(bm.Path) : bm.Path;
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Selectable(displayPath, false)) {
                        _navigateTo(bm.Path);
                    }

                    if (manager == _bookmarks.User && ImGui.BeginPopupContextItem(bm.Path)) {
                        ShowBookmarksContextMenu(manager, bm);
                        ImGui.EndPopup();
                    }

                    ImGui.TableSetColumnIndex(1);
                    foreach (var tag in bm.Tags) {
                        ImGui.PushID($"{bm.Path}_{tag}");
                        if (BookmarkManager.TagInfoMap.TryGetValue(tag, out var info)) {
                            var cols = info.Colors();
                            ImGui.PushStyleColor(ImGuiCol.Button, cols[0]);
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, cols[1]);
                            ImGui.PushStyleColor(ImGuiCol.ButtonActive, cols[2]);
                            ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);
                            if (ImGui.Button(info.Icon)) {
                                if (!activeTagFilter.Remove(tag)) {
                                    activeTagFilter.Add(tag);
                                }
                            }
                            ImguiHelpers.Tooltip(tag);
                            ImGui.PopStyleColor(4);
                        }
                        ImGui.SameLine();
                        ImGui.PopID();
                    }

                    ImGui.TableSetColumnIndex(2);
                    if (!string.IsNullOrEmpty(bm.Comment)) {
                        (manager == _bookmarks.Defaults ? (Action<string>)ImGui.TextDisabled : ImGui.Text)(bm.Comment);
                    }

                    if (manager == _bookmarks.User) {
                        ImGui.TableSetColumnIndex(3);
                        int idx = bookmarks.IndexOf(bm);
                        int uniqueId = bm.GetHashCode();
                        int? moveFrom = null, moveTo = null;
                        using (var _ = ImguiHelpers.Disabled(!string.IsNullOrEmpty(searchText))) {
                            if (ImGui.ArrowButton($"##up_{uniqueId}", ImGuiDir.Up) && idx > 0) {
                                moveFrom = idx;
                                moveTo = idx - 1;
                            }
                            ImGui.SameLine();
                            if (ImGui.ArrowButton($"##down_{uniqueId}", ImGuiDir.Down) && idx < bookmarks.Count - 1) {
                                moveFrom = idx;
                                moveTo = idx + 1;
                            }
                        }

                        if (moveFrom.HasValue && moveTo.HasValue) {
                            (bookmarks[moveFrom.Value], bookmarks[moveTo.Value]) = (bookmarks[moveTo.Value], bookmarks[moveFrom.Value]);
                            manager.SaveBookmarks();
                        }
                    }
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
    }

    private List<BookmarkManager.BookmarkEntry> FilterBookmarks(List<BookmarkManager.BookmarkEntry> bookmarks, List<string> activeTagFilter, string searchText, FilterMode filterMode)
    {
        return bookmarks.Where(bm => {
            bool tagMatch = true;
            if (activeTagFilter.Count > 0) {
                switch (filterMode) {
                    case FilterMode.AnyMatch:
                        tagMatch = activeTagFilter.Any(tag => bm.Tags.Contains(tag));
                        break;
                    case FilterMode.AllMatch:
                        tagMatch = activeTagFilter.All(tag => bm.Tags.Contains(tag));
                        break;
                    case FilterMode.ExactMatch:
                        tagMatch = bm.Tags.Count == activeTagFilter.Count && activeTagFilter.All(tag => bm.Tags.Contains(tag));
                        break;
                }
            }

            bool commentMatch = string.IsNullOrEmpty(searchText) || (bm.Comment?.Contains(searchText, isBookmarkSearchMatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) ?? false);
            return tagMatch && commentMatch;
        }).ToList();
    }

    private void ShowBookmarksContextMenu(BookmarkManager manager, BookmarkManager.BookmarkEntry bm)
    {
        if (ImguiHelpers.ContextMenuItem("##CopyPath", AppIcons.SI_FileCopyPath, UiText.T("Copy Path"), Colors.IconPrimary)) {
            EditorWindow.CurrentWindow?.CopyToClipboard(bm.Path);
        }
        if (ImguiHelpers.ContextMenuItem("##JumptoLocation", AppIcons.SIC_FileJumpTo, UiText.T("Jump to file Location"), [Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary])) {
            _navigateTo(bm.Path);
        }
        if (ImguiHelpers.ContextMenuItem("##RemoveBookmarks", AppIcons.SIC_BookmarkRemove, UiText.T("Remove from Bookmarks"), [Colors.IconPrimary, Colors.IconTertiary])) {
            manager.RemoveBookmark(Workspace.Config.Game.name, bm.Path);
        }
        if (ImGui.BeginMenu(UiText.FormatLabel($"{AppIcons.SI_GenericTag} | Tags"))) {
            ImGui.PushItemFlag(ImGuiItemFlags.AutoClosePopups, false);
            foreach (var tag in BookmarkManager.TagInfoMap.Keys) {
                bool hasTag = bm.Tags.Contains(tag);
                if (ImGui.MenuItem(tag, "", hasTag)) {
                    if (hasTag) {
                        bm.Tags.Remove(tag);
                        manager.SaveBookmarks();
                    } else {
                        bm.Tags.Add(tag);
                        manager.SaveBookmarks();
                    }
                }
            }
            ImGui.Separator();
            if (ImGui.MenuItem(Lang.Buttons.Clear_Tags)) {
                bm.Tags.Clear();
                manager.SaveBookmarks();
            }
            ImGui.PopItemFlag();
            ImGui.EndMenu();
        }
        if (editingCustomBookmark != bm.Path) {
            editingCustomBookmark = bm.Path;
            customBookmarkComment = bm.Comment;
        }
        if (ImGui.InputTextWithHint(UiText.LabelUtf8("Edit Comment"), UiText.Utf8("Press Enter to save"), ref customBookmarkComment, 64, ImGuiInputTextFlags.EnterReturnsTrue)) {
            bm.Comment = customBookmarkComment;
            manager.SaveBookmarks();
            editingCustomBookmark = null;
        }
    }

    private static string LessCompactFilePath(string path)
    {
        var parts = path.NormalizeFilepath().Split('/');
        if (parts.Length <= 2) return path;
        var remainingParts = parts[2..];
        return string.Join("/", remainingParts);
    }
}
