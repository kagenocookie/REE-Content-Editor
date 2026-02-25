using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.App.ImguiHandling;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;

namespace ContentEditor.App;

public enum FileDisplayMode
{
    List,
    Grid,
}

public partial class PakBrowser(ContentWorkspace contentWorkspace, string? pakFilePath) : IWindowHandler, IDisposable
{
    public string HandlerName => "PAK File Browser";

    public Workspace Workspace { get; } = contentWorkspace.Env;
    public string? PakFilePath { get; } = pakFilePath;

    private string _currentDir = contentWorkspace.Env.BasePath[0..^1];
    public string CurrentDir {
        get => _currentDir;
        set {
            _currentDir = value;
            previewGenerator?.CancelCurrentQueue();
        }
    }

    private string? _editedDir;

    public FileDisplayMode DisplayMode { get; set; } = AppConfig.Instance.PakDisplayMode;

    bool IWindowHandler.HasUnsavedChanges => false;
    // note: purposely not disposing the reader, in case we just reused the "main" pak reader from the workspace
    // it doesn't really need disposing with the current implementation either way
    private CachedMemoryPakReader? reader;
    private WindowData data = null!;
    protected UIContext context = null!;
    private ListFileWrapper? currentList;
    private ListFileWrapper? activeListFile;
    private BookmarkManager _bookmarkManagerDefaults = new BookmarkManager(Path.Combine(AppConfig.Instance.ConfigBasePath, "global/default_bookmarks_pak.json"));
    private BookmarkManager _bookmarkManager = new BookmarkManager(
        Path.Combine(AppConfig.Instance.BookmarksFilepath.Get()!, "bookmarks_pak.json"),
        Path.Combine(AppConfig.Instance.ConfigBasePath, "user/bookmarks_pak.json")
    );
    private List<string> _activeTagFilter = new();
    private string bookmarkSearch = string.Empty;
    private bool isBookmarkSearchMatchCase = false;
    private string customBookmarkComment = "";
    private string? editingCustomBookmark = null;
    private bool jumpToPageTop = false;
    private enum FilterMode
    {
        AnyMatch,
        AllMatch,
        ExactMatch
    }
    private FilterMode _filterMode = FilterMode.AnyMatch;

    private PakReader? unpacker;
    private int unpackExpectedFiles;

    private bool hasInvalidatedPaks;
    private bool isShowBookmarks = false;
    private int itemsPerPage = 1000;

    private ImGuiSortDirection gridSortDir = ImGuiSortDirection.Ascending;
    private int gridSortColumn;

    // TODO should probably store somewhere else?
    private FilePreviewGenerator? previewGenerator;
    private bool isFilePreviewEnabled = AppConfig.Instance.UsePakFilePreviewWindow.Get();
    private PageState pagination;

    private bool includeBasegameFiles = true;
    private bool includeBundleFiles = true;

    private struct PageState
    {
        public int maxPage;
        public int page;
        public int displayedCount;
        public int totalCount;
    }

    private readonly Dictionary<(string, int, ImGuiSortDirection), string[]> cachedResults = new();

    public unsafe void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    private void ExtractCurrentList(string outputDir) => ExtractList(CurrentDir, outputDir);
    private void ExtractList(string selectedPath, string outputDir)
    {
        if (unpacker != null) return;

        if (currentList == null || reader == null) {
            Logger.Error("File list missing");
            return;
        }

        var extractList = currentList;
        if (selectedPath.StartsWith(PakReader.UnknownFilePathPrefix)) {
            extractList = new ListFileWrapper(reader.UnknownFilePaths);
        }

        try {
            string[] files;
            if (ListFileWrapper.QueryHasPatterns(selectedPath)) {
                files = extractList.FilterAllFiles(selectedPath);
            } else if (reader.FileExists(selectedPath)) {
                files = [selectedPath];
            } else {
                files = extractList.FilterAllFiles(selectedPath.Replace('\\', '/') + ".*");
            }

            unpackExpectedFiles = files.Length;
            unpacker = new PakReader() { IncludeUnknownFilePaths = true };
            unpacker.PakFilePriority = reader.PakFilePriority;
            unpacker.MaxThreads = AppConfig.Instance.UnpackMaxThreads.Get();
            unpacker.EnableConsoleLogging = false;
            unpacker.AddFiles(files);

            var t = Stopwatch.StartNew();
            Logger.Info($"Starting unpack of {unpackExpectedFiles} files ...");
            var success = unpacker.UnpackFilesAsyncTo(outputDir).ContinueWith((task) => {
                if (task.IsCompletedSuccessfully) {
                    var success = task.Result;
                    Logger.Info($"Successfully extracted {success}/{unpackExpectedFiles} files in {t.Elapsed}");
                } else {
                    Logger.Error($"Extraction failed. " + task.Exception);
                }
                unpacker = null;
            });
        } catch (Exception e) {
            unpacker = null;
            Logger.Error(e, "Extraction failed.");
        }
    }
    private static ListFileWrapper? LocalizeListFile(ContentWorkspace workspace)
    {
        var list = workspace.Env.ListFile;
        if (list == null || workspace.CurrentBundle?.ResourceListing == null) return list;

        return new ListFileWrapper(list.Files.Concat(workspace.CurrentBundle.ResourceListing.Values.Select(v => v.Target)));
    }
    public void OnIMGUI()
    {
        activeListFile ??= LocalizeListFile(contentWorkspace);
        if (activeListFile == null || activeListFile.Files.Length == 0) {
            ImGui.TextColored(Colors.Warning, $"List file not found for game {Workspace.Config.Game}");
            // TODO add a "scan file list" option
            return;
        }

        if (reader == null) {
            if (PakFilePath == null) {
                // all files - use default pak reader data, but make a clone just so we don't mess with the original stuff
                Workspace.PakReader.IncludeUnknownFilePaths = true;
                Workspace.PakReader.AddFiles(activeListFile.Files);
                Workspace.PakReader.CacheEntries(true);
                reader = Workspace.PakReader.Clone();
                currentList = activeListFile;
                hasInvalidatedPaks = reader.FileExists(0);
            } else {
                // single file
                if (!File.Exists(PakFilePath)) {
                    ImGui.TextColored(Colors.Warning, $"File {PakFilePath} not found.");
                    return;
                }
                try {
                    reader = new CachedMemoryPakReader() { IncludeUnknownFilePaths = true };
                    if (!reader.TryReadManifestFileList(PakFilePath)) {
                        reader.AddFiles(activeListFile.Files);
                    }
                    reader.CacheEntries(true);
                    currentList = new ListFileWrapper(reader.CachedPaths);
                    hasInvalidatedPaks = reader.FileExists(0);
                } catch (Exception e) {
                    reader = null;
                    Logger.Error($"Pak file {PakFilePath} could not be opened: {e.Message}");
                    return;
                }
            }
        }
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_InfoPAK, new[] {Colors.IconPrimary, Colors.IconPrimary, Colors.Info} )) {
            ImGui.OpenPopup("PAKInfoPopup"u8);
        }
        ImguiHelpers.Tooltip("PAK Info"u8);
        if (ImGui.BeginPopup("PAKInfoPopup"u8)) {
            ImGui.Text("Total File Count: " + reader.MatchedEntryCount);
            ImGui.SameLine();
            if (PakFilePath == null) {
                ImGui.Text($"| PAK Count: {reader.PakFilePriority.Count}");
                ImGui.Separator();
                foreach (var pak in reader.PakFilePriority) {
                    ImGui.BulletText(pak);
                }
                ImGui.Separator();
            } else {
                ImGui.Text("PAK file: " + PakFilePath);
            }
            ImGui.EndPopup();
        }
        if (hasInvalidatedPaks) {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Warning);
            ImGui.Button($"{AppIcons.SI_GenericWarning}");
            ImGui.PopStyleColor();
            ImguiHelpers.TooltipColored("Invalidated PAK entries have been detected (most likely from Fluffy Mod Manager).\nYou may be unable to open some files.", Colors.Warning);
        }
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();
        ImguiHelpers.ToggleButton($"{AppIcons.SI_FileOpenPreview}", ref isFilePreviewEnabled, Colors.IconActive);
        ImguiHelpers.Tooltip("Toggle File Preview"u8);
        if (isFilePreviewEnabled != AppConfig.Instance.UsePakFilePreviewWindow.Get()) {
            AppConfig.Instance.UsePakFilePreviewWindow.Set(isFilePreviewEnabled);
        }
        ImGui.SameLine();
        var useCompactFilePaths = AppConfig.Instance.UsePakCompactFilePaths.Get();
        if (ImguiHelpers.ToggleButton($"{AppIcons.SI_PathShort}", ref useCompactFilePaths, Colors.IconActive)) {
            AppConfig.Instance.UsePakCompactFilePaths.Set(useCompactFilePaths);
        }
        ImguiHelpers.Tooltip("Toggle Compact File Paths"u8);
        ImGui.SameLine();
        if (contentWorkspace.CurrentBundle?.ResourceListing != null) {
            var resetCache = ImguiHelpers.ToggleButton($"{AppIcons.SI_FileType_PAK}", ref includeBasegameFiles, Colors.IconActive);
            ImguiHelpers.Tooltip("Show base game files"u8);
            ImGui.SameLine();
            resetCache = ImguiHelpers.ToggleButton($"{AppIcons.SI_Bundle}", ref includeBundleFiles, Colors.IconActive) || resetCache;
            ImguiHelpers.Tooltip("Show files from active bundle"u8);
            if (resetCache) {
                cachedResults.Clear();
            }
        }
        ImGui.SameLine();
        bool isHideDefaults = _bookmarkManagerDefaults.IsHideBookmarks;
        bool isHideCustoms = _bookmarkManager.IsHideBookmarks;
        bool isBookmarked = _bookmarkManager.IsBookmarked(Workspace.Config.Game.name, CurrentDir);
        ImguiHelpers.AlignElementRight((ImGui.CalcTextSize($"{AppIcons.SI_ViewGridSmall}").X + ImGui.GetStyle().FramePadding.X * 2) * 2 + ImGui.GetStyle().ItemSpacing.X);
        ImguiHelpers.ToggleButton($"{AppIcons.SI_Bookmarks}", ref isShowBookmarks, Colors.IconActive);
        ImguiHelpers.Tooltip("Bookmarks"u8);
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_PakBrowser_OpenBookmarks.Get().IsPressed()) {
            isShowBookmarks = !isShowBookmarks;
        }
        ImGui.SameLine();
        if (ImGui.Button(DisplayMode == FileDisplayMode.Grid ? $"{AppIcons.SI_ViewGridSmall}" : $"{AppIcons.List}")) {
            AppConfig.Instance.PakDisplayMode = DisplayMode = DisplayMode == FileDisplayMode.Grid ? FileDisplayMode.List : FileDisplayMode.Grid;
            previewGenerator?.CancelCurrentQueue();
        }
        ImguiHelpers.Tooltip(DisplayMode == FileDisplayMode.Grid ? "Grid View"u8 : "List View"u8);
        ImGui.Spacing();
        ImGui.Separator();
        if (isShowBookmarks) {
            ImguiHelpers.ToggleButtonMultiColor(AppIcons.SIC_BookmarkHide, ref isHideDefaults, new[] { Colors.IconSecondary, Colors.IconPrimary, Colors.IconPrimary }, Colors.IconActive);
            ImguiHelpers.Tooltip("Hide Default Bookmarks"u8);
            _bookmarkManagerDefaults.IsHideBookmarks = isHideDefaults;
            using (var _ = ImguiHelpers.Disabled(_bookmarkManager.GetBookmarks(Workspace.Config.Game.name).Count == 0)) {
                ImGui.SameLine();
                ImguiHelpers.ToggleButtonMultiColor(AppIcons.SIC_BookmarkCustomHide, ref isHideCustoms, new[] { Colors.IconSecondary, Colors.IconPrimary, Colors.IconPrimary }, Colors.IconActive);
                ImguiHelpers.Tooltip("Hide Custom Bookmarks"u8);
                _bookmarkManager.IsHideBookmarks = isHideCustoms;
                ImGui.SameLine();
                if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_BookmarkCustomClear, new[] { Colors.IconPrimary, Colors.IconTertiary })) {
                    ImGui.OpenPopup("Confirm Action"u8);
                }
                ImguiHelpers.Tooltip("Clear Custom Bookmarks");

                if (ImGui.BeginPopupModal("Confirm Action"u8, ImGuiWindowFlags.AlwaysAutoResize)) {
                    string confirmText = $"Are you sure you want to delete all custom bookmarks for {Languages.TranslateGame(Workspace.Config.Game.name)}?";
                    var textSize = ImGui.CalcTextSize(confirmText);
                    ImGui.Text(confirmText);
                    ImGui.Separator();

                    if (ImGui.Button("Yes"u8, new Vector2(textSize.X / 2, 0))) {
                        _bookmarkManager.ClearBookmarks(Workspace.Config.Game.name);
                        Logger.Info($"Cleared custom bookmarks for {Workspace.Config.Game.name}");
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("No"u8, new Vector2(textSize.X / 2, 0))) {
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
            }
            ImGui.SameLine();
            string filterModeName = _filterMode switch {
                FilterMode.AnyMatch => "Any",
                FilterMode.AllMatch => "All",
                FilterMode.ExactMatch => "Exact",
                _ => "?"
            };
            string filterLabelDisplayText = _activeTagFilter.Count == 0 ? $"{AppIcons.SI_Filter}" : $"{AppIcons.SI_Filter} : " + _activeTagFilter.Count.ToString();
            Vector2 filterLabelSize = ImGui.CalcTextSize(filterLabelDisplayText);
            float filterComboWidth = filterLabelSize.X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X + ImGui.GetFontSize();
            float searchBarWidth = 260f;
            ImguiHelpers.AlignElementRight(((ImGui.CalcTextSize($"{AppIcons.SI_GenericClose}").X + ImGui.GetStyle().FramePadding.X * 2) + ImGui.GetStyle().ItemSpacing.X) * 2 + filterComboWidth + searchBarWidth + ImGui.GetStyle().ItemSpacing.X);
            ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericMatchCase}", ref isBookmarkSearchMatchCase, Colors.IconActive);
            ImguiHelpers.Tooltip("Match Case");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(searchBarWidth);
            ImGui.SetNextItemAllowOverlap();
            ImGui.InputTextWithHint("##BookmarkSearch"u8, $"{AppIcons.SI_GenericMagnifyingGlass} Search Comments", ref bookmarkSearch, 64);
            var bookmarkSearchQuery = isBookmarkSearchMatchCase ? bookmarkSearch.Trim() : bookmarkSearch.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(bookmarkSearch)) {
                ImGui.SameLine();
                ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMax().X - ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.X, ImGui.GetItemRectMin().Y));
                ImGui.SetNextItemAllowOverlap();
                if (ImGui.Button($"{AppIcons.SI_GenericClose}")) {
                    bookmarkSearch = string.Empty;
                }
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(filterComboWidth);
            if (ImGui.BeginCombo("##TagFilterCombo"u8, filterLabelDisplayText, ImGuiComboFlags.HeightLargest)) {
                ImGui.TextDisabled("Filter Mode:");
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
                    ImGui.SeparatorText("Filter Modes");
                    ImGui.BulletText("Any: Keep entries with at least one matching tag");
                    ImGui.BulletText("All: Keep entries containing all active tags");
                    ImGui.BulletText("Exact: Keep entries with tags exactly matching the active filters");
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
            ImguiHelpers.Tooltip("Filters");
            ImGui.SameLine();
            using (var _ = ImguiHelpers.Disabled(_activeTagFilter.Count == 0)) {
                if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FilterClear, new[] { Colors.IconTertiary, Colors.IconPrimary })) {
                    _activeTagFilter.Clear();
                }
                ImguiHelpers.Tooltip("Clear Filters");
            }
            if (_bookmarkManagerDefaults.GetBookmarks(Workspace.Config.Game.name).Count > 0 && !_bookmarkManagerDefaults.IsHideBookmarks) {
                ShowBookmarksTable("Default", 3, _bookmarkManagerDefaults, _activeTagFilter, bookmarkSearchQuery);
            }
            if (_bookmarkManager.GetBookmarks(Workspace.Config.Game.name).Count > 0 && !_bookmarkManager.IsHideBookmarks) {
                ShowBookmarksTable("Custom", 4, _bookmarkManager, _activeTagFilter, bookmarkSearchQuery);
            } else {
                if (!_bookmarkManager.IsHideBookmarks) {
                    ImGui.TextDisabled("No Custom Bookmarks yet...");
                }
            }
            ImGui.Separator();
            ImGui.Spacing();
        }
        var unpackedFiles = unpacker?.unpackedFileCount;
        if (unpackedFiles != null) {
            var w = ImGui.GetWindowSize().X - (ImGui.GetStyle().ItemSpacing.X * 2);
            var total = unpackExpectedFiles;
            ImGui.ProgressBar((float)unpackedFiles.Value / total, new Vector2(w, UI.FontSize + ImGui.GetStyle().FramePadding.Y * 2), $"{unpackedFiles.Value}/{total}");
        }
        if (ImGui.ArrowButton("##left"u8, ImGuiDir.Left) || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_Back.Get().IsPressed()) {
            CurrentDir = Path.GetDirectoryName(CurrentDir)?.Replace('\\', '/') ?? string.Empty;
            pagination.page = 0;
        }
        ImguiHelpers.Tooltip("Back");
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(CurrentDir.Count(c => c == '/') < 3)) {
            if (ImGui.ArrowButton("##up"u8, ImGuiDir.Up)) {
                CurrentDir = Workspace.BasePath[0..^1];
            }
            ImguiHelpers.Tooltip("Return to Top");
        }
        ImGui.SameLine();

        _editedDir ??= CurrentDir;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - (ImGui.CalcTextSize("Path").X + (ImGui.CalcTextSize($"{AppIcons.SI_GenericClose}").X + ImGui.GetStyle().ItemSpacing.X * 2) * 2 + ImGui.GetStyle().FramePadding.X * 2));
        if (ImGui.InputText("Path"u8, ref _editedDir, 250)) {
            if (_editedDir.EndsWith('/')) _editedDir = _editedDir[0..^1];
            CurrentDir = _editedDir;
            pagination.page = 0;
        } else {
            _editedDir = null;
        }
        if (ImGui.BeginItemTooltip()) {
            ImGui.Text("You can use patterns for more complex matching rules");
            ImGui.BulletText("Regex patterns: natives/stm/character/**.mdf2.*");
            ImGui.BulletText("Include rules (path MUST contain the text): +.tex    +cha01");
            ImGui.BulletText("Exclude rules (path MUST NOT contain the text): !.tex    !/sm00");
            ImGui.Text("Include and exclude rules must be separated with spaces");
            ImGui.EndTooltip();
        }
        ImGui.SameLine();
        if (isBookmarked || _bookmarkManagerDefaults.IsBookmarked(Workspace.Config.Game.name, CurrentDir)) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconActive);
        } else {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconPrimary);
        }
        if (ImGui.Button((isBookmarked ? AppIcons.Star : AppIcons.StarEmpty) + "##bookmark") || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_PakBrowser_Bookmark.Get().IsPressed()) {
            if (isBookmarked) {
                _bookmarkManager.RemoveBookmark(Workspace.Config.Game.name, CurrentDir);
            } else {
                _bookmarkManager.AddBookmark(Workspace.Config.Game.name, CurrentDir);
            }
        }
        ImGui.PopStyleColor();
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(unpackedFiles != null)) {
            if (ImGui.Button($"{AppIcons.SI_ArchiveExtractTo}")) {
                PlatformUtils.ShowFolderDialog(ExtractCurrentList, AppConfig.Instance.GetGameExtractPath(Workspace.Config.Game));
            }
            ImguiHelpers.Tooltip("Extract To...");
        }
        DrawContents();
    }

    private void DrawContents()
    {
        if (reader == null) return;
        ImGui.BeginChild("Content"u8);
        ShowFiles(out var sortedEntries);

        using (var _ = ImguiHelpers.Disabled(pagination.page <= 0)) {
            if (ImGui.ArrowButton("##prev"u8, ImGuiDir.Left)) {
                pagination.page--;
                jumpToPageTop = true;
                previewGenerator?.CancelCurrentQueue();
            }
        }
        ImGui.SameLine();
        var fileCount = sortedEntries?.Length ?? 0;
        ImGui.Text(fileCount == 0 ? "Page 0 / 0" : $"Page {pagination.page + 1} / {pagination.maxPage + 1}");
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(pagination.page >= pagination.maxPage)) {
            if (ImGui.ArrowButton("##next"u8, ImGuiDir.Right)) {
                pagination.page++;
                jumpToPageTop = true;
                previewGenerator?.CancelCurrentQueue();
            }
        }
        ImGui.SameLine();
        ImGui.Text($"Total matches: {fileCount}");
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();
        ImGui.Text($"Displaying: {pagination.page * itemsPerPage + Math.Sign(fileCount)}-{pagination.page * itemsPerPage + pagination.displayedCount}");
        ImGui.SameLine();
        ImguiHelpers.AlignElementRight((ImGui.GetFrameHeight()));
        if (ImGui.ArrowButton("##JumpToPageTop", ImGuiDir.Up) || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_PakBrowser_JumpToPageTop.Get().IsPressed()) {
            jumpToPageTop = true;
        }
        ImguiHelpers.Tooltip("Jump to page top");
        ImGui.EndChild();
    }

    private void ShowFiles(out string[] sortedEntries)
    {
        sortedEntries = null!;
        var remainingHeight = ImGui.GetWindowSize().Y - ImGui.GetCursorPosY() - ImGui.GetStyle().WindowPadding.Y - UI.FontSize - ImGui.GetStyle().FramePadding.Y;
        if (DisplayMode == FileDisplayMode.Grid) {
            ShowFileGrid(ref sortedEntries, remainingHeight);
        } else {
            ShowFileList(ref sortedEntries, remainingHeight);
        }
    }

    private void GetPageFiles(ListFileWrapper baseList, short ColumnIndex, ImGuiSortDirection SortDirection, [NotNull] ref string[]? sortedEntries)
    {
        var cacheKey = (CurrentDir, ColumnIndex, SortDirection);
        if (!cachedResults.TryGetValue(cacheKey, out sortedEntries)) {
            string[] files;
            if (CurrentDir.StartsWith(PakReader.UnknownFilePathPrefix)) {
                files = ListFileWrapper.FilterFiles(reader?.UnknownFilePaths ?? [], CurrentDir);
            } else {
                files = baseList.GetFiles(CurrentDir);
            }
            if (string.IsNullOrEmpty(CurrentDir) && reader!.ContainsUnknownFiles) {
                Array.Resize(ref files, files.Length + 1);
                files[^1] = PakReader.UnknownFilePathPrefix;
            }
            var sorted = cacheKey.ColumnIndex switch {
                0 => cacheKey.SortDirection == ImGuiSortDirection.Ascending ? files : files.Reverse(),
                1 => cacheKey.SortDirection == ImGuiSortDirection.Ascending ? files.OrderBy(e => reader!.GetSize(e)) : files.OrderByDescending(e => reader!.GetSize(e)),
                _ => cacheKey.SortDirection == ImGuiSortDirection.Ascending ? files : files.Reverse(),
            };
            if (!includeBasegameFiles || !includeBundleFiles) {
                if (!includeBasegameFiles && !includeBundleFiles) sorted = [];
                else if (!includeBasegameFiles) sorted = sorted.Where(IsFileOrFolderInBundle);
                else if (!includeBundleFiles) sorted = sorted.Where(IsFileOrFolderInBaseGame);
            }
            cachedResults[cacheKey] = sortedEntries = sorted.OrderByDescending(e => !Path.HasExtension(e)).ToArray();
        }
        pagination.maxPage = (int)Math.Floor((float)sortedEntries.Length / itemsPerPage);
        pagination.totalCount = sortedEntries.Length;
    }

    private void ShowFileGrid([NotNull] ref string[]? sortedEntries, float remainingHeight)
    {
        var baseList = currentList!;
        var useCompactFilePaths = AppConfig.Instance.UsePakCompactFilePaths.Get();
        GetPageFiles(baseList, (short)gridSortColumn, gridSortDir, ref sortedEntries);
        if (sortedEntries.Length == 0) return;
        previewGenerator ??= new(contentWorkspace, EditorWindow.CurrentWindow?.GLContext!);

        var style = ImGui.GetStyle();
        var btnSize = new Vector2(120 * UI.UIScale, 100 * UI.UIScale);
        var iconPadding = new Vector2(32, 14) * UI.UIScale;
        var availableSize = ImGui.GetWindowWidth() - style.WindowPadding.X;

        ImGui.BeginChild("FileGrid"u8, new Vector2(availableSize, remainingHeight));
        if (jumpToPageTop) {
            ImGui.SetScrollY(0);
            jumpToPageTop = false;
        }
        int i = 0;
        var curX = 0f;
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 1f));
        foreach (var file in sortedEntries.Skip(itemsPerPage * pagination.page).Take(itemsPerPage)) {
            ImGui.PushID(i);
            if (curX > 0) {
                if (curX > availableSize - btnSize.X) {
                    curX = 0;
                } else {
                    ImGui.SameLine();
                }
            }
            curX += btnSize.X + style.ItemSpacing.X;

            bool isBookmarked = _bookmarkManager.IsBookmarked(Workspace.Config.Game.name, file);
            if (isBookmarked) {
                ImGui.PushStyleColor(ImGuiCol.Text, Colors.TextActive);
            } else {
                ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.Text));
            }
            var filename = Path.GetFileName(PathUtils.GetFilepathWithoutSuffixes(file.AsSpan()));
            var displayName = filename;
            if (displayName.Length >= 16) {
                var ext = Path.GetExtension(displayName);
                var nameEnd = 13 - ext.Length;
                displayName = Path.GetFileName(filename)[0..nameEnd].ToString() + ".." + ext.ToString();
            }
            var pos = ImGui.GetCursorScreenPos();
            var click = ImGui.Button(displayName.ToString(), btnSize);
            ImGui.PopStyleColor();
            ImGui.PushFont(null, UI.FontSizeLarge);
            if (Path.HasExtension(file)) {
                if (isFilePreviewEnabled) {
                    var previewStatus = previewGenerator.FetchPreview(file, out var previewTex);
                    if (previewStatus == PreviewImageStatus.Ready) {
                        var texSize = new Vector2(btnSize.X, btnSize.Y - UI.FontSize) - style.FramePadding;
                        ImGui.GetWindowDrawList().AddImage(previewTex!.AsTextureRef(), pos + style.FramePadding, pos + texSize);
                    } else {
                        var (icon, col) = previewStatus == PreviewImageStatus.PredefinedIcon ||
                            previewStatus == PreviewImageStatus.Ready ? AppIcons.GetIcon(PathUtils.ParseFileFormat(file).format) : (AppIcons.SI_File, Vector4.One);
                        ImGui.GetWindowDrawList().AddText(pos + iconPadding, ImGui.ColorConvertFloat4ToU32(col), $"{icon}");
                    }
                } else {
                    var (icon, col) = AppIcons.GetIcon(PathUtils.ParseFileFormat(file).format);
                    (icon, col) = icon != '\0' ? (icon, col) : (AppIcons.SI_File, Vector4.One);
                    ImGui.GetWindowDrawList().AddText(pos + iconPadding, ImGui.ColorConvertFloat4ToU32(col), $"{icon}");
                }
            } else {
                ImGui.GetWindowDrawList().AddText(pos + iconPadding, 0xffffffff, $"{AppIcons.SI_FolderEmpty}");
            }
            if (includeBundleFiles && IsFileOrFolderInBundle(file)) {
                ImguiHelpers.DrawOverlayIcon($"{AppIcons.SI_Bundle}", 0.4f, -1f, -1f, ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), Colors.IconOverlay, Colors.IconOverlayBackground);
            }
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) {
                var tt = file;
                var fmt = PathUtils.ParseFileFormat(file);
                tt += "\nResource type: " + fmt.format;
                var prettySize = GetFileSizeString(file);
                if (prettySize != null) tt += "\nSize: " + Encoding.UTF8.GetString(prettySize);
                ImGui.SetItemTooltip(tt);
            }
            if (click) {
                HandleFileClick(baseList, file);
            }

            ShowFileContextMenu(file, _bookmarkManager.IsBookmarked(Workspace.Config.Game.name, file), true);
            ImGui.PopID();
            i++;
        }
        ImGui.PopStyleVar();
        ImGui.EndChild();
        pagination.displayedCount = i;
    }

    private void ShowFileList([NotNull] ref string[]? sortedEntries, float remainingHeight)
    {
        sortedEntries ??= [];
        var baseList = currentList!;
        int i = 0;
        if (isFilePreviewEnabled) {
            previewGenerator ??= new(contentWorkspace, EditorWindow.CurrentWindow?.GLContext!);
        }

        var useCompactFilePaths = AppConfig.Instance.UsePakCompactFilePaths.Get();
        if (ImGui.BeginTable("List"u8, 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuterV | ImGuiTableFlags.Sortable, new Vector2(0, remainingHeight))) {
            ImGui.TableSetupColumn("Path"u8, ImGuiTableColumnFlags.WidthStretch, 0.9f);
            ImGui.TableSetupColumn("Size"u8, ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.PreferSortDescending, 100);
            ImGui.TableSetupScrollFreeze(0, 1);
            var sort = ImGui.TableGetSortSpecs();
            GetPageFiles(baseList, sort.Specs.ColumnIndex, sort.Specs.SortDirection, ref sortedEntries);
            ImGui.TableHeadersRow();
            ImGui.TableNextColumn();
            if (jumpToPageTop) {
                ImGui.SetScrollY(0);
                jumpToPageTop = false;
            }
            foreach (var file in sortedEntries.Skip(itemsPerPage * pagination.page).Take(itemsPerPage)) {
                ImGui.PushID(i);
                i++;
                var displayName = useCompactFilePaths ? CompactFilePath(file) : file;
                bool isBookmarked = _bookmarkManager.IsBookmarked(Workspace.Config.Game.name, file);
                if (isBookmarked) {
                    ImGui.PushStyleColor(ImGuiCol.Text, Colors.TextActive);
                } else {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
                }

                if (Path.HasExtension(file)) {
                    var (icon, col) = AppIcons.GetIcon(PathUtils.ParseFileFormat(file).format);
                    if (icon == '\0') {
                        ImGui.Text($"{AppIcons.SI_File}");
                    } else if (icon == AppIcons.SI_FolderEmpty) {
                        ImGui.TextColored(col, $"{AppIcons.SI_FolderLink}");
                    } else {
                        ImGui.TextColored(col, $"{icon}");
                    }
                } else {
                    ImGui.Text($"{AppIcons.SI_FolderEmpty}");
                }
                if (includeBundleFiles && IsFileOrFolderInBundle(file)) {
                    ImGui.SameLine();
                    ImguiHelpers.DrawOverlayIcon($"{AppIcons.SI_Bundle}", 0.6f, 2f, 1.5f, ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), Colors.IconOverlay, Colors.IconOverlayBackground);
                }

                ImGui.SameLine();
                if (ImGui.Selectable(displayName, false, ImGuiSelectableFlags.SpanAllColumns)) {
                    bool wasFile = HandleFileClick(baseList, file);
                    if (!wasFile) {
                        ImGui.PopStyleColor();
                        ImGui.PopID();
                        ImGui.TableNextColumn();
                        break;
                    }
                }
                ImGui.PopStyleColor();

                if (isFilePreviewEnabled && ImGui.IsItemHovered()) {
                    if (Path.HasExtension(file)) {
                        var previewStatus = previewGenerator!.FetchPreview(file, out var previewTex);
                        if (previewStatus == PreviewImageStatus.Ready) {
                            ImGui.BeginTooltip();
                            ImGui.Image(previewTex!.AsTextureRef(), new Vector2(256, 256));
                            ImGui.EndTooltip();
                        }
                    }
                }
                ShowFileContextMenu(file, isBookmarked);
                ImGui.TableNextColumn();
                var prettySize = GetFileSizeString(file);
                if (prettySize != null) {
                    var posX = ImGui.GetCursorPosX() + ImGui.GetColumnWidth() - ImGui.CalcTextSize(prettySize).X - ImGui.GetScrollX() - ImGui.GetStyle().ItemSpacing.X;
                    if (posX > ImGui.GetCursorPosX()) {
                        ImGui.SetCursorPosX(posX);
                    }
                    ImGui.Text(prettySize);
                }
                ImGui.PopID();
                ImGui.TableNextColumn();
            }
            ImGui.EndTable();
        }
        pagination.displayedCount = i;
    }

    private readonly Dictionary<int, byte[]> _fileSizeStringCache = new();
    private byte[]? GetFileSizeString(string file)
    {
        var size = reader!.GetSize(file);
        if (size > 0) {
            if (_fileSizeStringCache.TryGetValue(size, out var prettySize)) {
                return prettySize;
            }
            if (size >= 1024 * 1024) {
                prettySize = Encoding.UTF8.GetBytes(((float)size / (1024 * 1024)).ToString("0.00") + " MB\0");
            } else {
                prettySize = Encoding.UTF8.GetBytes(((float)size / 1024).ToString("0.00") + " KB\0");
            }
            _fileSizeStringCache[size] = prettySize;
            return prettySize;
        }
        return null;
    }

    private bool HandleFileClick(ListFileWrapper baseList, string file)
    {
        if (!baseList.FileExists(file)) {
            if (file.Equals(PakUtils.ManifestFilepath, StringComparison.InvariantCultureIgnoreCase)) {
                var stream = reader!.GetFile(file);
                if (stream == null) {
                    EditorWindow.CurrentWindow?.AddSubwindow(new ErrorModal("File not found", "File could not be found in the PAK file(s)."));
                } else {
                    EditorWindow.CurrentWindow?.OpenFile(stream, file, PakFilePath + "://");
                }
                return true;
            }

            // if it's not a full list file match then it's a folder, navigate to it
            CurrentDir = file;
            pagination.page = 0;
            return false;
        }

        if (IsFileOrFolderInBundle(file)) {
            if (contentWorkspace.ResourceManager.TryResolveGameFile(file, out var targetFile)) {
                EditorWindow.CurrentWindow?.AddFileEditor(targetFile);
                return true;
            }
        }

        if (!reader!.FileExists(file)) {
            var hasLooseFile = File.Exists(Path.Combine(Workspace.Config.GamePath, file));
            if (hasLooseFile) {
                Logger.Error("File could not be found in the loaded PAK files. Matching loose file was found, the file entry may have been invalidated by Fluffy Mod Manager.");
            } else {
                Logger.Error("File could not be found in the loaded PAK files. Possible causes: Fluffy Mod Manager archive invalidation, missing some DLC content, not having the right PAK files open, or a wrong file list.");
            }
            return true;
        }

        try {
            if (PathUtils.ParseFileFormat(file).format == KnownFileFormats.Mesh && file.Contains("/streaming")) {
                Logger.Warn("Attempted to open streaming mesh. Opening the main non-streaming mesh instead.");
                file = file.Replace("/streaming/", "/");
            }

            if (PakFilePath == null) {
                EditorWindow.CurrentWindow?.OpenFiles([file]);
            } else {
                var stream = reader.GetFile(file);
                if (stream == null) {
                    EditorWindow.CurrentWindow?.AddSubwindow(new ErrorModal("File not found", "File could not be found in the PAK file(s)."));
                } else {
                    EditorWindow.CurrentWindow?.OpenFile(stream, file, PakFilePath + "://");
                }
            }
        } catch (Exception e) {
            Logger.Error($"Failed to open file {file}: {e.Message}");
        }

        return true;
    }

    private void ShowFileContextMenu(string file, bool isBookmarked, bool showSort = false)
    {

        ImGui.SetNextWindowSize(new Vector2(250, 0));
        if (ImGui.BeginPopupContextItem()) {
            if (ImguiHelpers.ContextMenuItem("##CopyPath", AppIcons.SI_FileCopyPath, "Copy Path", Colors.IconPrimary)) {
                EditorWindow.CurrentWindow?.CopyToClipboard(file);
            }
            var isFolder = !Path.HasExtension(file);
            if (ImguiHelpers.ContextMenuItem("##Extract", AppIcons.SIC_FileExtractTo, isFolder ? "Extract Folder to..." : "Extract File to...", new[] {Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary})) {
                if (isFolder) {
                    // show folder unpack dialog instead
                    PlatformUtils.ShowFolderDialog((output) => {
                        ExtractList(file, output);
                    }, AppConfig.Instance.GetGameExtractPath(Workspace.Config.Game));
                } else {
                    PlatformUtils.ShowSaveFileDialog((savePath) => {
                        var stream = file.StartsWith(PakReader.UnknownFilePathPrefix) ? reader!.GetUnknownFile(file) : reader!.GetFile(file);
                        if (stream == null) {
                            Logger.Error("Could not find file " + file + " in selected PAK files");
                            return;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                        using var fs = File.Create(savePath);
                        stream.WriteTo(fs);
                    }, Path.GetFileName(file));
                }
                ImGui.CloseCurrentPopup();
            }
            if (isBookmarked) {
                if (ImguiHelpers.ContextMenuItem("##RemoveBookmark", AppIcons.SIC_BookmarkRemove, "Remove from Bookmarks", new[] { Colors.IconPrimary, Colors.IconTertiary })) {
                    _bookmarkManager.RemoveBookmark(Workspace.Config.Game.name, file);
                }
            } else {
                if (ImguiHelpers.ContextMenuItem("##AddBookmark", AppIcons.SIC_BookmarkAdd, "Add to Bookmarks", new[] { Colors.IconPrimary, Colors.IconSecondary })) {
                    _bookmarkManager.AddBookmark(Workspace.Config.Game.name, file);
                }
            }
            if (Path.HasExtension(file) && contentWorkspace.CurrentBundle != null) {
                var handle = contentWorkspace.ResourceManager.GetFileHandle(file);
                if (ImguiHelpers.ContextMenuItem("##SaveToBundle", AppIcons.SIC_BundleSaveTo, "Save to Bundle", new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary, Colors.IconPrimary })) {
                    if (handle != null) {
                        ResourcePathPicker.SaveFileToBundle(contentWorkspace, handle, (savePath, localPath, nativePath) => handle.Save(contentWorkspace, savePath));
                    }
                }
            }
            if (Path.HasExtension(file)) {
                if (ImguiHelpers.ContextMenuItem("##JumpToContainingFolder", AppIcons.SIC_FolderContain, "Jump to Containing Folder", new[] { Colors.IconPrimary, Colors.IconSecondary })) {
                    string currFolder = Path.GetDirectoryName(file)!;
                    _currentDir = PathUtils.NormalizeFilepath(currFolder);
                }
            }
            if (showSort) {
                ImGui.Spacing();
                ImGui.Separator();
                if (ImGui.Selectable("Sort By: " + (gridSortColumn == 1 ? "Size" : "Name"))) {
                    gridSortColumn = 1 - gridSortColumn;
                    previewGenerator?.CancelCurrentQueue();
                }
                if (ImGui.Selectable("Order: " + gridSortDir)) {
                    gridSortDir = gridSortDir == ImGuiSortDirection.Ascending ? ImGuiSortDirection.Descending : ImGuiSortDirection.Ascending;
                    previewGenerator?.CancelCurrentQueue();
                }
            }
            ImGui.EndPopup();
        }
    }

    private void ShowBookmarksTable(string label, int columnNum, BookmarkManager manager, List<string> activeTagFilter, string searchText)
    {
        var bookmarks = manager.GetBookmarks(Workspace.Config.Game.name);
        if (bookmarks.Count == 0) {
            return;
        }

        var useCompactFilePaths = AppConfig.Instance.UsePakCompactFilePaths.Get();
        var filteredBookmarks = FilterBookmarks(bookmarks.ToList(), activeTagFilter, searchText, _filterMode);

        if (filteredBookmarks.Count == 0) {
            ImGui.SeparatorText(label + " [No Matches Found]");
            return;
        }

        float rowHeight = ImGui.GetTextLineHeightWithSpacing();
        float currDisplayHeight = filteredBookmarks.Count <= 5 ? 0 : rowHeight * 10;
        var bookmarkTableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterV | ImGuiTableFlags.RowBg;
        if (filteredBookmarks.Count >= 5) {
            bookmarkTableFlags |= ImGuiTableFlags.ScrollY;
        }
        ImGui.SeparatorText(label);
        if (ImGui.BeginChild($"{label}_Scroll", new Vector2(0, currDisplayHeight), ImGuiChildFlags.AlwaysAutoResize | ImGuiChildFlags.AutoResizeY)) {
            if (ImGui.BeginTable($"{label}Table", columnNum, bookmarkTableFlags)) {
                ImGui.TableSetupColumn("Path"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Tags"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Comment"u8, ImGuiTableColumnFlags.WidthStretch);
                if (manager == _bookmarkManager) {
                    ImGui.TableSetupColumn("Order"u8, ImGuiTableColumnFlags.WidthFixed, ((ImGui.GetFrameHeight() * 2f) + ImGui.GetStyle().ItemSpacing.X * 3f));
                }
                ImGui.TableHeadersRow();

                foreach (var bm in filteredBookmarks) {
                    string displayPath = useCompactFilePaths ? LessCompactFilePath(bm.Path) : bm.Path;
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (ImGui.Selectable(displayPath, false)) {
                        CurrentDir = bm.Path;
                    }

                    if (manager == _bookmarkManager && ImGui.BeginPopupContextItem(bm.Path)) {
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
                        (manager == _bookmarkManagerDefaults ? (Action<string>)ImGui.TextDisabled : ImGui.Text)(bm.Comment);
                    }

                    if (manager == _bookmarkManager) {
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

        if (ImguiHelpers.ContextMenuItem("##CopyPath", AppIcons.SI_FileCopyPath, "Copy Path", Colors.IconPrimary)) {
            EditorWindow.CurrentWindow?.CopyToClipboard(bm.Path);
        }
        if (ImguiHelpers.ContextMenuItem("##JumptoLocation", AppIcons.SIC_FileJumpTo, "Jump to file Location", new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary })) {
            CurrentDir = bm.Path;
        }
        if (ImguiHelpers.ContextMenuItem("##RemoveBookmarks", AppIcons.SIC_BookmarkRemove, "Remove from Bookmarks", new[] { Colors.IconPrimary, Colors.IconTertiary })) {
            manager.RemoveBookmark(Workspace.Config.Game.name, bm.Path);
        }
        if (ImGui.BeginMenu($"{AppIcons.SI_GenericTag} | Tags")) {
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
            if (ImGui.MenuItem("Clear All Tags"u8)) {
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
        if (ImGui.InputTextWithHint("Edit Comment"u8, "Press Enter to save"u8, ref customBookmarkComment, 64, ImGuiInputTextFlags.EnterReturnsTrue)) {
            bm.Comment = customBookmarkComment;
            manager.SaveBookmarks();
            editingCustomBookmark = null;
        }
    }

    private bool IsFileOrFolderInBundle(string path)
    {
        if (contentWorkspace.CurrentBundle?.ResourceListing == null) return false;

        if (Path.HasExtension(path)) {
            return contentWorkspace.CurrentBundle.TryFindResourceByNativePath(path, out _);
        }

        foreach (var p in contentWorkspace.CurrentBundle.ResourceListing) {
            if (p.Value.Target.StartsWith(path)) return true;
        }
        return false;
    }

    private bool IsFileOrFolderInBaseGame(string path)
    {
        if (Path.HasExtension(path)) {
            return reader?.FileExists(path) == true;
        }
        return Workspace.ListFile?.GetFilesInFolder(path)?.Length > 0;
    }

    private static string CompactFilePath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        if (parts.Length <= 2) return path;

        return parts[^1];
    }

    private static string LessCompactFilePath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        if (parts.Length <= 2) return path;

        var remainingParts = parts[2..];
        return string.Join("/", remainingParts);
    }
    public bool RequestClose()
    {
        return false;
    }

    public void Dispose()
    {
        previewGenerator?.Dispose();
    }
}
