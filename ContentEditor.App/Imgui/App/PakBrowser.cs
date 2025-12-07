using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

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
    private ListFileWrapper? matchedList;
    private BookmarkManager _bookmarkManagerDefaults = new BookmarkManager(Path.Combine(AppConfig.Instance.ConfigBasePath, "app/default_bookmarks_pak.json"));
    private BookmarkManager _bookmarkManager = new BookmarkManager(Path.Combine(AppConfig.Instance.ConfigBasePath, "user/bookmarks_pak.json"));
    private List<string> _activeTagFilter = new();
    private string bookmarkSearch = string.Empty;
    private bool isBookmarkSearchMatchCase = false;
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

    private ImGuiSortDirection gridSortDir = ImGuiSortDirection.Descending;
    private int gridSortColumn;

    // TODO should probably store somewhere else?
    private FilePreviewGenerator? previewGenerator;
    private bool isFilePreviewEnabled = AppConfig.Instance.UsePakFilePreviewWindow.Get();
    private PageState pagination;

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

    private void ExtractCurrentList(string outputDir)
    {
        if (unpacker != null) return;

        if (matchedList == null || reader == null) {
            Logger.Error("File list missing");
            return;
        }

        var extractList = matchedList;
        if (CurrentDir.StartsWith(PakReader.UnknownFilePathPrefix)) {
            extractList = new ListFileWrapper(reader.UnknownFilePaths);
        }

        try {
            string[] files;
            if (CurrentDir.Contains('*') || CurrentDir.Contains('+')) {
                files = extractList.FilterAllFiles(CurrentDir);
            } else if (reader.FileExists(CurrentDir)) {
                files = [CurrentDir];
            } else {
                files = extractList.FilterAllFiles(CurrentDir.Replace('\\', '/') + ".*");
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
    public void OnIMGUI()
    {
        var list = Workspace.ListFile;
        if (list == null) {
            ImGui.TextColored(Colors.Warning, $"List file not found for game {Workspace.Config.Game}");
            return;
        }

        if (reader == null) {
            if (PakFilePath == null) {
                // all files - use default pak reader data, but make a clone just so we don't mess with the original stuff
                Workspace.PakReader.IncludeUnknownFilePaths = true;
                Workspace.PakReader.CacheEntries();
                reader = Workspace.PakReader.Clone();
                matchedList = list;
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
                        reader.AddFiles(list.Files);
                    }
                    reader.CacheEntries(true);
                    matchedList = new ListFileWrapper(reader.CachedPaths);
                    hasInvalidatedPaks = reader.FileExists(0);
                } catch (Exception e) {
                    reader = null;
                    Logger.Error($"Pak file {PakFilePath} could not be opened: {e.Message}");
                    return;
                }
            }
            // TODO handle unknowns properly
        }
        if (ImGui.Button($"{AppIcons.SI_GenericInfo} PAK Info")) {
            ImGui.OpenPopup("PAKInfoPopup");
        }
        if (ImGui.BeginPopup("PAKInfoPopup")) {
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
        ImguiHelpers.ToggleButton($"{AppIcons.SI_FileOpenPreview}", ref isFilePreviewEnabled, ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered), 2.0f);
        ImguiHelpers.Tooltip("Toggle File Preview");
        if (isFilePreviewEnabled != AppConfig.Instance.UsePakFilePreviewWindow.Get()) {
            AppConfig.Instance.UsePakFilePreviewWindow.Set(isFilePreviewEnabled);
        }
        ImGui.SameLine();
        var useCompactFilePaths = AppConfig.Instance.UsePakCompactFilePaths.Get();
        if (ImguiHelpers.ToggleButton($"{AppIcons.SI_PathShort}", ref useCompactFilePaths, ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered), 2.0f)) {
            AppConfig.Instance.UsePakCompactFilePaths.Set(useCompactFilePaths);
        }
        ImguiHelpers.Tooltip("Toggle Compact File Paths");
        ImGui.SameLine();
        bool isHideDefaults = _bookmarkManagerDefaults.IsHideBookmarks;
        bool isHideCustoms = _bookmarkManager.IsHideBookmarks;
        bool isBookmarked = _bookmarkManager.IsBookmarked(Workspace.Config.Game.name, CurrentDir);
        ImguiHelpers.AlignElementRight((ImGui.CalcTextSize($"{AppIcons.SI_ViewGridSmall}").X + ImGui.GetStyle().FramePadding.X * 2) * 2 + ImGui.GetStyle().ItemSpacing.X);
        ImguiHelpers.ToggleButton($"{AppIcons.SI_Bookmarks}", ref isShowBookmarks, ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered), 2.0f);
        ImguiHelpers.Tooltip("Bookmarks");
        ImGui.SameLine();
        if (ImGui.Button(DisplayMode == FileDisplayMode.Grid ? $"{AppIcons.SI_ViewGridSmall}" : $"{AppIcons.SI_ViewList}")) {
            AppConfig.Instance.PakDisplayMode = DisplayMode = DisplayMode == FileDisplayMode.Grid ? FileDisplayMode.List : FileDisplayMode.Grid;
            previewGenerator?.CancelCurrentQueue();
        }
        ImguiHelpers.Tooltip(DisplayMode == FileDisplayMode.Grid ? "Grid View" : "List View");
        ImGui.Spacing();
        ImGui.Separator();
        if (isShowBookmarks) {
            ImguiHelpers.ToggleButton($"{AppIcons.SI_BookmarkHide}", ref isHideDefaults, ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered), 2.0f);
            ImguiHelpers.Tooltip("Hide Default Bookmarks");
            _bookmarkManagerDefaults.IsHideBookmarks = isHideDefaults;
            using (var _ = ImguiHelpers.Disabled(_bookmarkManager.GetBookmarks(Workspace.Config.Game.name).Count == 0)) {
                ImGui.SameLine();
                ImguiHelpers.ToggleButton($"{AppIcons.SI_BookmarkCustomHide}", ref isHideCustoms, ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered), 2.0f);
                ImguiHelpers.Tooltip("Hide Custom Bookmarks");
                _bookmarkManager.IsHideBookmarks = isHideCustoms;
                ImGui.SameLine();
                if (ImGui.Button($"{AppIcons.SI_BookmarkCustomClear}")) {
                    ImGui.OpenPopup("Confirm Action");
                }
                ImguiHelpers.Tooltip("Clear Custom Bookmarks");

                if (ImGui.BeginPopupModal("Confirm Action", ImGuiWindowFlags.AlwaysAutoResize)) {
                    ImGui.Text($"Are you sure you want to delete all custom bookmarks for {Workspace.Config.Game.name}?");
                    ImGui.Separator();

                    if (ImGui.Button("Yes", new Vector2(200, 0))) {
                        _bookmarkManager.ClearBookmarks(Workspace.Config.Game.name);
                        Logger.Info($"Cleared custom bookmarks for {Workspace.Config.Game.name}");
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("No", new Vector2(200, 0))) {
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_Filter}")) {
                ImGui.OpenPopup("TagFilterDropdown");
            }
            ImguiHelpers.Tooltip("Filters");

            if (ImGui.BeginPopup("TagFilterDropdown")) {
                foreach (var tag in BookmarkManager.TagInfoMap.Keys) {
                    bool hasTag = _activeTagFilter.Contains(tag);
                    if (ImGui.Selectable(tag)) {
                        if (hasTag) {
                            _activeTagFilter.Remove(tag);
                        } else {
                            _activeTagFilter.Add(tag);
                        }
                    }
                }
                ImGui.EndPopup();
            }
            ImGui.SameLine();
            ImguiHelpers.AlignElementRight(300f);
            ImguiHelpers.ToggleButton($"{AppIcons.SI_GenericMatchCase}", ref isBookmarkSearchMatchCase, ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered), 2.0f);
            ImguiHelpers.Tooltip("Match Case");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(260f);
            ImGui.SetNextItemAllowOverlap();
            ImGui.InputTextWithHint("##BookmarkSearch", $"{AppIcons.SI_GenericMagnifyingGlass} Search Comments", ref bookmarkSearch, 64);
            var bookmarkSearchQuery = isBookmarkSearchMatchCase ? bookmarkSearch.Trim() : bookmarkSearch.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(bookmarkSearch)) {
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() - (ImGui.CalcTextSize($"{AppIcons.SI_GenericError}").X * 2));
                ImGui.SetNextItemAllowOverlap();
                if (ImGui.Button($"{AppIcons.SI_GenericError}")) { // SILVER: temp icon
                    bookmarkSearch = string.Empty;
                }
            } // SILVER: Maybe move this whole search bar to ImguiHelpers so it can be reused elsewhere
            if (_activeTagFilter.Count > 0) {
                if (ImGui.Button($"{AppIcons.SI_FilterClear}")) {
                    _activeTagFilter.Clear();
                }
                ImguiHelpers.Tooltip("Clear Filters");
                ImGui.SameLine();
                string filterModeName = _filterMode switch {
                    FilterMode.AnyMatch => "Any",
                    FilterMode.AllMatch => "All",
                    FilterMode.ExactMatch => "Exact",
                    _ => "?"
                };

                if (ImGui.Button($"Filter Mode: {filterModeName}")) {
                    _filterMode = _filterMode switch {
                        FilterMode.AnyMatch => FilterMode.AllMatch,
                        FilterMode.AllMatch => FilterMode.ExactMatch,
                        FilterMode.ExactMatch => FilterMode.AnyMatch,
                        _ => FilterMode.AnyMatch
                    };
                }
                ImGui.SameLine();
                ImguiHelpers.Tooltip("Filter Modes:\nAny = Keep entries with at least one matching tag\nAll = Keep entries containing all active tags\nExact = Keep entries with tags exactly matching the active filters");
                ImGui.SameLine();
                ImGui.Text("Active Filters: ");

                foreach (var tag in _activeTagFilter) {
                    ImGui.PushID("ActiveTag_" + tag);
                    if (BookmarkManager.TagInfoMap.TryGetValue(tag, out var info)) {
                        ImGui.PushStyleColor(ImGuiCol.Text, info.Colors[1]);
                    }
                    ImGui.SameLine();
                    ImGui.Text(tag);
                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                    ImGui.Text("|");
                    ImGui.PopID();
                }
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

        if (ImGui.ArrowButton("##left", ImGuiDir.Left)) {
            CurrentDir = Path.GetDirectoryName(CurrentDir)?.Replace('\\', '/') ?? string.Empty;
        }
        ImguiHelpers.Tooltip("Back");
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(CurrentDir.Count(c => c == '/') < 3)) {
            if (ImGui.ArrowButton("##up", ImGuiDir.Up)) {
                CurrentDir = Workspace.BasePath[0..^1];
            }
            ImguiHelpers.Tooltip("Return to Top");
        }
        ImGui.SameLine();

        _editedDir ??= CurrentDir;
        if (ImGui.InputText("Path", ref _editedDir, 250)) {
            if (_editedDir.EndsWith('/')) _editedDir = _editedDir[0..^1];
            CurrentDir = _editedDir;
            pagination.page = 0;
        } else {
            _editedDir = null;
        }
        ImguiHelpers.Tooltip("You can use regex to match file patterns (e.g. natives/stm/character/**.mdf2.*)");
        ImGui.SameLine();
        if (isBookmarked || _bookmarkManagerDefaults.IsBookmarked(Workspace.Config.Game.name, CurrentDir)) {
            ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered));
        } else {
            ImGui.PushStyleColor(ImGuiCol.Text, ImguiHelpers.GetColor(ImGuiCol.Text));
        }
        if (ImGui.Button((isBookmarked ? AppIcons.Star : AppIcons.StarEmpty) + "##bookmark")) {
            if (isBookmarked) {
                _bookmarkManager.RemoveBookmark(Workspace.Config.Game.name, CurrentDir);
            } else {
                _bookmarkManager.AddBookmark(Workspace.Config.Game.name, CurrentDir);
            }
        }
        ImGui.PopStyleColor();
        ImGui.SameLine();

        var unpackedFiles = unpacker?.unpackedFileCount;
        if (unpackedFiles != null) {
            var style = ImGui.GetStyle();
            var w = ImGui.GetWindowSize().X - ImGui.GetCursorPos().X - style.WindowPadding.X;
            var total = unpackExpectedFiles;
            ImGui.ProgressBar((float)unpackedFiles.Value / total, new Vector2(w, UI.FontSize + style.FramePadding.Y * 2), $"{unpackedFiles.Value}/{total}");
        } else if (ImGui.Button($"{AppIcons.SI_ArchiveExtractTo}")) {
            PlatformUtils.ShowFolderDialog(ExtractCurrentList, AppConfig.Instance.GetGameExtractPath(Workspace.Config.Game));
        }
        ImguiHelpers.Tooltip("Extract To...");
        DrawContents();
    }

    private void DrawContents()
    {
        if (reader == null) return;
        ImGui.BeginChild("Content");
        ShowFiles(out var sortedEntries);

        using (var _ = ImguiHelpers.Disabled(pagination.page <= 0)) {
            if (ImGui.ArrowButton("##prev", ImGuiDir.Left)) {
                pagination.page--;
                previewGenerator?.CancelCurrentQueue();
            }
        }
        ImGui.SameLine();
        var fileCount = sortedEntries?.Length ?? 0;
        ImGui.Text(fileCount == 0 ? "Page 0 / 0" : $"Page {pagination.page + 1} / {pagination.maxPage + 1}");
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(pagination.page >= pagination.maxPage)) {
            if (ImGui.ArrowButton("##next", ImGuiDir.Right)) {
                pagination.page++;
                previewGenerator?.CancelCurrentQueue();
            }
        }
        ImGui.SameLine();
        ImGui.Text($"Total matches: {fileCount} | Displaying: {pagination.page * itemsPerPage + Math.Sign(fileCount)}-{pagination.page * itemsPerPage + pagination.displayedCount}");
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
            var files = baseList.GetFiles(CurrentDir);
            if (string.IsNullOrEmpty(CurrentDir) && reader!.ContainsUnknownFiles) {
                Array.Resize(ref files, files.Length + 1);
                files[^1] = PakReader.UnknownFilePathPrefix;
            } else if (CurrentDir.StartsWith(PakReader.UnknownFilePathPrefix)) {
                files = reader!.UnknownFilePaths;
            }
            var sorted = cacheKey.ColumnIndex switch {
                0 => cacheKey.SortDirection == ImGuiSortDirection.Ascending ? files : files.Reverse(),
                1 => cacheKey.SortDirection == ImGuiSortDirection.Ascending ? files.OrderBy(e => reader!.GetSize(e)) : files.OrderByDescending(e => reader!.GetSize(e)),
                _ => cacheKey.SortDirection == ImGuiSortDirection.Ascending ? files : files.Reverse(),
            };
            cachedResults[cacheKey] = sortedEntries = sorted.ToArray();
        }
        pagination.maxPage = (int)Math.Floor((float)sortedEntries.Length / itemsPerPage);
        pagination.totalCount = sortedEntries.Length;
    }

    private void ShowFileGrid([NotNull] ref string[]? sortedEntries, float remainingHeight)
    {
        var baseList = matchedList!;
        var useCompactFilePaths = AppConfig.Instance.UsePakCompactFilePaths.Get();
        GetPageFiles(baseList, (short)gridSortColumn, gridSortDir, ref sortedEntries);
        if (sortedEntries.Length == 0) return;

        previewGenerator ??= new(contentWorkspace, EditorWindow.CurrentWindow?.GLContext!);

        var style = ImGui.GetStyle();
        var btnSize = new Vector2(120 * UI.UIScale, 100 * UI.UIScale);
        var iconPadding = new Vector2(32, 14) * UI.UIScale;
        var availableSize = ImGui.GetWindowWidth() - style.WindowPadding.X;
        ImGui.BeginChild("FileGrid", new Vector2(availableSize, remainingHeight));

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
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.PlotHistogramHovered]);
            } else {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
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
                ImGui.GetWindowDrawList().AddText(pos + iconPadding, 0xffffffff, $"{AppIcons.Folder}");
            }
            ImGui.PopFont();
            if (ImGui.IsItemHovered()) {
                var tt = file;
                var fmt = PathUtils.ParseFileFormat(file);
                tt += "\nResource type: " + fmt.format;
                var prettySize = GetFileSizeString(file);
                if (prettySize != null) tt += "\nSize: " + prettySize;
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
        var baseList = matchedList!;
        int i = 0;
        if (isFilePreviewEnabled) {
            previewGenerator ??= new(contentWorkspace, EditorWindow.CurrentWindow?.GLContext!);
        }
        var useCompactFilePaths = AppConfig.Instance.UsePakCompactFilePaths.Get();
        if (ImGui.BeginTable("List", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuterV | ImGuiTableFlags.Sortable, new Vector2(0, remainingHeight))) {
            ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch, 0.9f);
            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.PreferSortDescending, 100);
            ImGui.TableSetupScrollFreeze(0, 1);
            var sort = ImGui.TableGetSortSpecs();
            GetPageFiles(baseList, sort.Specs.ColumnIndex, sort.Specs.SortDirection, ref sortedEntries);
            ImGui.TableHeadersRow();
            ImGui.TableNextColumn();
            foreach (var file in sortedEntries.Skip(itemsPerPage * pagination.page).Take(itemsPerPage)) {
                ImGui.PushID(i);
                i++;
                var displayName = useCompactFilePaths ? CompactFilePath(file) : file;
                bool isBookmarked = _bookmarkManager.IsBookmarked(Workspace.Config.Game.name, file);
                if (isBookmarked) {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.PlotHistogramHovered]);
                } else {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
                }

                {
                    if (Path.HasExtension(file)) {
                        var (icon, col) = AppIcons.GetIcon(PathUtils.ParseFileFormat(file).format);
                        if (icon == '\0') {
                            ImGui.Text($"{AppIcons.SI_File}");
                        } else if (icon == AppIcons.Folder) {
                            ImGui.TextColored(col, $"{AppIcons.FolderLink}");
                        } else {
                            ImGui.TextColored(col, $"{icon}");
                        }
                    } else {
                        ImGui.Text($"{AppIcons.Folder}");
                    }
                    ImGui.SameLine();
                }
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

    private string? GetFileSizeString(string file)
    {
        var size = reader!.GetSize(file);
        if (size > 0) {
            string prettySize;
            if (size >= 1024 * 1024) {
                prettySize = ((float)size / (1024 * 1024)).ToString("0.00") + " MB";
            } else {
                prettySize = ((float)size / 1024).ToString("0.00") + " KB";
            }
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
        if (ImGui.BeginPopupContextItem()) {
            if (ImGui.Selectable($"{AppIcons.SI_FileCopyPath} | Copy Path")) {
                EditorWindow.CurrentWindow?.CopyToClipboard(file);
                ImGui.CloseCurrentPopup();
            }
            ImGui.Spacing();
            if (ImGui.Selectable($"{AppIcons.SI_FileExtractTo} | Extract File to ...")) {
                var nativePath = file;
                PlatformUtils.ShowSaveFileDialog((savePath) => {
                    var stream = reader!.GetFile(nativePath);
                    if (stream == null) {
                        Logger.Error("Could not find file " + nativePath + " in selected PAK files");
                        return;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                    using var fs = File.Create(savePath);
                    stream.WriteTo(fs);
                }, Path.GetFileName(nativePath));
                ImGui.CloseCurrentPopup();
            }
            ImGui.Spacing();
            if (isBookmarked) {
                if (ImGui.Selectable($"{AppIcons.SI_BookmarkRemove} | Remove from Bookmarks")) {
                    _bookmarkManager.RemoveBookmark(Workspace.Config.Game.name, file);
                }
            } else {
                if (ImGui.Selectable($"{AppIcons.SI_BookmarkAdd} | Add to Bookmarks")) {
                    _bookmarkManager.AddBookmark(Workspace.Config.Game.name, file);
                }
            }
            ImGui.Spacing();
            if (Path.HasExtension(file)) {
                if (ImGui.Selectable("Jump to Containing Folder")) {
                    string currFolder = Path.GetDirectoryName(file)!;
                    _currentDir = PathUtils.NormalizeFilepath(currFolder);
                }
            }
            if (showSort) {
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
                ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Comment", ImGuiTableColumnFlags.WidthStretch);
                if (manager == _bookmarkManager) {
                    ImGui.TableSetupColumn("Order", ImGuiTableColumnFlags.WidthFixed, ((ImGui.GetFrameHeight() * 2f) + ImGui.GetStyle().ItemSpacing.X * 3f));
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
                            ImGui.PushStyleColor(ImGuiCol.Button, info.Colors[0]);
                            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, info.Colors[1]);
                            ImGui.PushStyleColor(ImGuiCol.ButtonActive, info.Colors[2]);
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
                        // SILVER: Maybe drag & drop would be neater for this...
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
    private List<BookmarkManager.BookmarkEntry> FilterBookmarks(List<BookmarkManager.BookmarkEntry> bookmarks, List<string> activeTagFilter, string searchText, FilterMode filterMode) {
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

    private void ShowBookmarksContextMenu(BookmarkManager manager, BookmarkManager.BookmarkEntry bm) {
        if (ImGui.Selectable($"{AppIcons.SI_FileJumpTo} | Jump to Location")) {
            CurrentDir = bm.Path;
        }
        ImGui.Spacing();
        if (ImGui.Selectable($"{AppIcons.SI_FileCopyPath} | Copy Path")) {
            EditorWindow.CurrentWindow?.CopyToClipboard(bm.Path);
        }
        ImGui.Spacing();
        if (ImGui.Selectable($"{AppIcons.SI_BookmarkRemove} | Remove from Bookmarks")) {
            manager.RemoveBookmark(Workspace.Config.Game.name, bm.Path);
        }
        ImGui.Spacing();
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
            if (ImGui.MenuItem("Clear All Tags")) {
                bm.Tags.Clear();
                manager.SaveBookmarks();
            }
            ImGui.PopItemFlag();
            ImGui.EndMenu();
        }
        string comment = bm.Comment;
        if (ImGui.InputText("Edit Comment", ref comment, 64, ImGuiInputTextFlags.EnterReturnsTrue)) {
            bm.Comment = comment;
            manager.SaveBookmarks();
        }
    }

    private static string CompactFilePath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        if (parts.Length <= 2) return path;

        return ".../" + parts[^1];
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
