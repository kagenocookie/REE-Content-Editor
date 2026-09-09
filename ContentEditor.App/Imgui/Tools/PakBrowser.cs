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
using ReeLib.Common;

namespace ContentEditor.App;

public enum FileDisplayMode
{
    List,
    Grid,
}

public partial class PakBrowser(ContentWorkspace contentWorkspace, string[]? pakFilePaths) : IWindowHandler, IDisposable
{
    public string HandlerName => "PAK File Browser";

    public Workspace Workspace { get; } = contentWorkspace.Env;
    public string[]? PakFilePaths { get; } = pakFilePaths;
    private string? thumbnailDiscriminator = pakFilePaths == null ? null : MurMur3HashUtils.GetPakFilepathHash(string.Join("", pakFilePaths)).ToString("X16");

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
    private BookmarkHolder _bookmarks = new BookmarkHolder(contentWorkspace);
    private BookmarksPanel? _bookmarksPanel;
    private BookmarksPanel BookmarksPanel => _bookmarksPanel ??= new BookmarksPanel(contentWorkspace, _bookmarks, path => CurrentDir = path);
    private bool isShowBookmarks = false;
    private bool jumpToPageTop = false;
    private PakReader? unpacker;
    private int unpackExpectedFiles;
    private bool hasInvalidatedPaks;
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

    private readonly Dictionary<(string, int, ImGuiSortDirection, int), string[]> cachedResults = new();
    private readonly HashSet<KnownFileFormats> _activeFileTypeFilters = new();
    private bool isHideUnsupportedFormats = true;
    private int fileTypeFilterIDX = 0;
    private string fileTypeFilterSearch = string.Empty;
    private static readonly (KnownFileFormats Format, string Ext)[] knownFileTypes = Enum.GetValues<KnownFileFormats>()
        .Select(f => (Format: f, Ext: FileFormatExtensions.FormatToFileExtension(f))).Where(t => t.Ext != "unknown").DistinctBy(t => t.Ext).OrderBy(t => t.Ext, StringComparer.OrdinalIgnoreCase).ToArray();

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
        if (selectedPath.StartsWith(PakReader.UnknownFilePathPrefix.AsSpan()[..^1])) {
            extractList = new ListFileWrapper(reader.UnknownFilePaths, contentWorkspace.Platform);
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
        if (list == null || workspace.CurrentBundle?.HasResources != true) return list;

        return new ListFileWrapper(list.Files.Concat(workspace.CurrentBundle.Files.Select(v => workspace.Env.PrependBasePath(v.Target))), workspace.Platform, true);
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
            if (PakFilePaths == null) {
                // all files - use default pak reader data, but make a clone just so we don't mess with the original stuff
                if (Workspace.PakReader.PakFilePriority.Count == 0) {
                    ImGui.TextColored(Colors.Warning, $"No PAK files found for game {Workspace.Config.Game}");
                    return;
                }
                Workspace.PakReader.IncludeUnknownFilePaths = true;
                if (!Workspace.PakReader.CachedPaths.Any()) {
                    // force a rescan in case it was previous loaded without the list file
                    Workspace.PakReader.Clear();
                }
                Workspace.PakReader.AddFiles(activeListFile.Files);
                Workspace.PakReader.CacheEntries(true);
                reader = Workspace.PakReader.Clone();
                currentList = activeListFile;
                hasInvalidatedPaks = reader.FileExists(0);
            } else {
                // single file
                reader = new CachedMemoryPakReader() { IncludeUnknownFilePaths = true };
                foreach (var path in PakFilePaths.Reverse()) {
                    if (!File.Exists(path)) {
                        ImGui.TextColored(Colors.Warning, $"File {path} not found.");
                        continue;
                    }

                    try {
                        if (!reader.TryReadManifestFileList(path)) {
                            reader.AddFiles(activeListFile.Files);
                        }
                        reader.PakFilePriority.Add(path);
                    } catch (Exception e) {
                        reader = null;
                        Logger.Error($"Pak file {path} could not be opened: {e.Message}");
                        return;
                    }
                }

                reader.CacheEntries(true);
                var platform = reader.DeterminePlatform();
                if (platform.id == Platform.Unknown) platform = contentWorkspace.Platform;
                currentList = new ListFileWrapper(reader.CachedPaths, platform);
                hasInvalidatedPaks = reader.FileExists(0);
            }
        }
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_InfoPAK, [Colors.IconPrimary, Colors.IconPrimary, Colors.Info])) {
            ImGui.OpenPopup("PAKInfoPopup"u8);
        }
        ImguiHelpers.Tooltip("PAK Info"u8);
        if (ImGui.BeginPopup("PAKInfoPopup"u8)) {
            ImGui.Text("Total File Count: " + reader.MatchedEntryCount);
            ImGui.SameLine();
            ImGui.Text($"| PAK Count: {reader.PakFilePriority.Count}");
            ImGui.Separator();
            foreach (var pak in reader.PakFilePriority) {
                ImGui.BulletText(pak);
            }
            ImGui.Separator();
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
        if (ImguiHelpers.ToggleButton($"{AppIcons.SI_LogCompact}", ref useCompactFilePaths, Colors.IconActive)) {
            AppConfig.Instance.UsePakCompactFilePaths.Set(useCompactFilePaths);
        }
        ImguiHelpers.Tooltip("Toggle Compact File Paths"u8);
        ImGui.SameLine();
        if (contentWorkspace.CurrentBundle?.HasResources == true) {
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
        bool isHideDefaults = _bookmarks.Defaults.IsHideBookmarks;
        bool isHideCustoms = _bookmarks.User.IsHideBookmarks;
        bool isBookmarked = _bookmarks.User.IsBookmarked(Workspace.Config.Game.name, CurrentDir);
        ImguiHelpers.AlignElementRight((ImGui.CalcTextSize($"{AppIcons.SI_ViewGridSmall}").X + ImGui.GetStyle().FramePadding.X * 2) * 2 + ImGui.GetStyle().ItemSpacing.X);
        bool isBookmarksActive = AppConfig.Instance.UseBookmarkWindow ? EditorWindow.CurrentWindow?.HasSubwindow<BookmarksWindow>(out _) == true : isShowBookmarks;
        if (ImguiHelpers.ToggleButton($"{AppIcons.SI_Bookmarks}", ref isBookmarksActive, Colors.IconActive)) {
            ToggleBookmarks();
        }
        ImguiHelpers.Tooltip("Bookmarks"u8);
        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_PakBrowser_OpenBookmarks.Get().IsPressed()) {
            ToggleBookmarks();
        }
        ImGui.SameLine();
        if (ImGui.Button(DisplayMode == FileDisplayMode.Grid ? $"{AppIcons.SI_ViewGridSmall}" : $"{AppIcons.List}")) {
            AppConfig.Instance.PakDisplayMode = DisplayMode = DisplayMode == FileDisplayMode.Grid ? FileDisplayMode.List : FileDisplayMode.Grid;
            previewGenerator?.CancelCurrentQueue();
        }
        ImguiHelpers.Tooltip(DisplayMode == FileDisplayMode.Grid ? "Grid View"u8 : "List View"u8);
        ImGui.Spacing();
        ImGui.Separator();
        if (isShowBookmarks && !AppConfig.Instance.UseBookmarkWindow) {
            BookmarksPanel.ShowBookmarks();
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
        bool hasFileTypeFilter = _activeFileTypeFilters.Count > 0;
        string fileTypeFilterLabel = !hasFileTypeFilter ? $"{AppIcons.SI_Filter}" : $"{AppIcons.SI_Filter} : {_activeFileTypeFilters.Count}";
        float fileTypeFilterW = ImGui.CalcTextSize(fileTypeFilterLabel).X + ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetFontSize() + ImGui.GetStyle().ItemSpacing.X;

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ((ImGui.CalcTextSize($"{AppIcons.SI_GenericClose}").X + ImGui.GetStyle().ItemSpacing.X * 2) * 3 + ImGui.GetStyle().FramePadding.X * 2) - ImGui.GetStyle().ItemSpacing.X - fileTypeFilterW);
        using (var _ = ImguiHelpers.Disabled(hasFileTypeFilter)) {
            if (ImGui.InputText("##Path"u8, ref _editedDir, 250)) {
                if (_editedDir.EndsWith('/')) _editedDir = _editedDir[0..^1];
                CurrentDir = _editedDir;
                pagination.page = 0;
            } else {
                _editedDir = null;
            }
            if (ImGui.BeginItemTooltip()) {
                if (hasFileTypeFilter) {
                    ImGui.Text("Regex search is disabled while a file filter is active");
                } else {
                    ImGui.Text("You can use patterns for more complex matching rules");
                    ImGui.BulletText("Regex patterns: natives/stm/character/**.mdf2.*");
                    ImGui.BulletText("Include rules (path MUST contain the text): +.tex    +cha01");
                    ImGui.BulletText("Exclude rules (path MUST NOT contain the text): !.tex    !/sm00");
                    ImGui.Text("Include and exclude rules must be separated with spaces");
                }
                ImGui.EndTooltip();
            }
        }
        ImGui.SameLine();
        if (isBookmarked || _bookmarks.Defaults.IsBookmarked(Workspace.Config.Game.name, CurrentDir)) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconActive);
        } else {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconPrimary);
        }
        if (ImGui.Button((isBookmarked ? AppIcons.Star : AppIcons.StarEmpty) + "##bookmark") || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && AppConfig.Instance.Key_PakBrowser_Bookmark.Get().IsPressed()) {
            if (isBookmarked) {
                _bookmarks.User.RemoveBookmark(Workspace.Config.Game.name, CurrentDir);
            } else {
                _bookmarks.User.AddBookmark(Workspace.Config.Game.name, CurrentDir);
            }
        }
        ImGui.PopStyleColor();
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(unpackedFiles != null)) {
            if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_PakExtractTo, [Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary])) {
                PlatformUtils.ShowFolderDialog(ExtractCurrentList, AppConfig.Instance.GetGameExtractPath(Workspace.Config.Game));
            }
            ImguiHelpers.Tooltip(Lang.PakBrowser.ExtractTo);
        }
        ImGui.SameLine();
        int fileTypeFilterColNum = 4;
        float colDesiredWidth = isHideUnsupportedFormats ? 180 * UI.UIScale : 195 * UI.UIScale;

        var mainViewport = ImGui.GetMainViewport();
        float viewportUsableW = mainViewport.WorkSize.X * 0.9f;
        int computedCols = (int)(viewportUsableW / colDesiredWidth);
        fileTypeFilterColNum = Math.Clamp(computedCols, 1, 4);
        float fileTypeFilterPopupW = colDesiredWidth * fileTypeFilterColNum;

        Vector2 buttonPos = ImGui.GetCursorScreenPos();
        float desiredButtonW = buttonPos.X;
        float maxViewportW = mainViewport.WorkPos.X + mainViewport.WorkSize.X - fileTypeFilterPopupW;
        float clampedViewportW = Math.Clamp(desiredButtonW, mainViewport.WorkPos.X, Math.Max(mainViewport.WorkPos.X, maxViewportW));

        ImGui.SetNextItemWidth(fileTypeFilterW);
        ImGui.SetNextWindowPos(new Vector2(clampedViewportW, buttonPos.Y + ImGui.GetFrameHeightWithSpacing()));
        ImGui.SetNextWindowSize(new Vector2(fileTypeFilterPopupW, 0));
        if (ImGui.BeginCombo("##FileTypeFilters"u8, fileTypeFilterLabel, ImGuiComboFlags.HeightLargest)) {
            AppImguiHelpers.ClearableInputText("##FileTypeFilterSearch"u8, $"{AppIcons.SI_GenericMagnifyingGlass} Filter file extensions", ref fileTypeFilterSearch, 32);
            ImGui.SameLine();
            using (var _ = ImguiHelpers.Disabled(_activeFileTypeFilters.Count == 0)) {
                if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FilterClear, [Colors.IconTertiary, Colors.IconPrimary], "00")) {
                    _activeFileTypeFilters.Clear();
                    pagination.page = 0;
                    fileTypeFilterIDX++;
                }
                ImguiHelpers.Tooltip(Lang.PakBrowser.Tooltip_ClearFilters);
            }
            ImguiHelpers.InlineVerticalSeparator();
            if (ImguiHelpers.ToggleButtonMultiColor(AppIcons.SIC_FileUnsupportedFormat, ref isHideUnsupportedFormats, [Colors.IconPrimary, Colors.IconPrimary, Colors.IconTertiary], Colors.IconActive)) {
                fileTypeFilterIDX++;
            }
            ImguiHelpers.Tooltip(Lang.PakBrowser.Tooltip_HideUnsupported);
            ImGui.Separator();

            var colW = fileTypeFilterPopupW / fileTypeFilterColNum;
            if (ImGui.BeginTable("##FileTypeFilterTable", fileTypeFilterColNum, ImGuiTableFlags.SizingFixedFit)) {
                for (int c = 0; c < fileTypeFilterColNum; c++) {
                    ImGui.TableSetupColumn($"##col{c}", ImGuiTableColumnFlags.WidthFixed, colW);
                }
                ImGui.PushItemFlag(ImGuiItemFlags.AutoClosePopups, false);
                int colIDX = 0;
                foreach (var (format, ext) in knownFileTypes) {
                    string fileEXT = "." + ext;
                    if (fileTypeFilterSearch.Length > 0 && ext.IndexOf(fileTypeFilterSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (isHideUnsupportedFormats && !contentWorkspace.ResourceManager.CanLoadFile(fileEXT)) continue;
                    if (colIDX % fileTypeFilterColNum == 0) {
                        ImGui.TableNextRow();
                    }
                    ImGui.TableSetColumnIndex(colIDX % fileTypeFilterColNum);
                    ImGui.AlignTextToFramePadding();

                    ImGui.PushID(ext);
                    bool isSelected = _activeFileTypeFilters.Contains(format);
                    float cellW = ImGui.GetContentRegionAvail().X;
                    bool wasRowClicked = ImGui.Selectable("##filterRow", false, ImGuiSelectableFlags.None, new Vector2(cellW, ImGui.GetFrameHeight()));

                    Vector2 selMin = ImGui.GetItemRectMin();
                    Vector2 selMax = ImGui.GetItemRectMax();
                    float selH = selMax.Y - selMin.Y;
                    float chkH = ImGui.GetFrameHeight();
                    float yOffset = (selH - chkH) * 0.5f;
                    float xOffset = 4f * UI.UIScale;

                    ImGui.SetNextItemAllowOverlap();
                    ImGui.SetCursorScreenPos(new Vector2(selMin.X + xOffset, selMin.Y + yOffset));
                    bool wasChkboxClicked = ImGui.Checkbox("##filterFakeChkBox", ref isSelected);
                    ImGui.SameLine();
                    var (icon, col) = AppIcons.GetIcon(format);
                    if (icon != '\0') {
                        ImGui.TextColored(col, $"{icon}");
                    } else {
                        ImGui.Text($"{AppIcons.SI_Blank}");
                    }
                    ImGui.SameLine();
                    ImGui.Text(fileEXT);

                    if (wasRowClicked || wasChkboxClicked) {
                        bool isUpdatedActiveFilters = wasChkboxClicked ? isSelected : !_activeFileTypeFilters.Contains(format);
                        if (isUpdatedActiveFilters) {
                            _activeFileTypeFilters.Add(format);
                        } else {
                            _activeFileTypeFilters.Remove(format);
                        }
                        pagination.page = 0;
                        fileTypeFilterIDX++;
                        previewGenerator?.CancelCurrentQueue();
                    }
                    ImGui.PopID();
                    colIDX++;
                }
                ImGui.PopItemFlag();
                ImGui.EndTable();
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(_activeFileTypeFilters.Count == 0)) {
            if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FilterClear, [Colors.IconTertiary, Colors.IconPrimary], "01")) {
                _activeFileTypeFilters.Clear();
                pagination.page = 0;
                fileTypeFilterIDX++;
            }
            ImguiHelpers.Tooltip(Lang.PakBrowser.Tooltip_ClearFilters);
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
        var cacheKey = (CurrentDir, ColumnIndex, SortDirection, fileTypeFilterIDX);
        if (!cachedResults.TryGetValue(cacheKey, out sortedEntries)) {
            string[] files;
            var isUnknownDir = CurrentDir.StartsWith(PakReader.UnknownFilePathPrefix.AsSpan()[..^1]);
            var hasFileTypeFilter = _activeFileTypeFilters.Count > 0;

            if (hasFileTypeFilter) {
                var sourceDir = isUnknownDir ? reader!.UnknownFilePaths : baseList.Files;
                files = sourceDir.Where(f => (CurrentDir.Length == 0 || f.StartsWith(CurrentDir, StringComparison.OrdinalIgnoreCase)) && _activeFileTypeFilters.Contains(PathUtils.ParseFileFormat(f).format)).ToArray();
            } else if (isUnknownDir) {
                files = ListFileWrapper.FilterFiles(reader!.UnknownFilePaths, CurrentDir);
            } else {
                files = baseList.GetFiles(CurrentDir);
            }
            if (!hasFileTypeFilter) {
                if (string.IsNullOrEmpty(CurrentDir) && reader!.ContainsUnknownFiles) {
                    Array.Resize(ref files, files.Length + 1);
                    files[^1] = PakReader.UnknownFilePathPrefix;
                } else if (files.Length == 0) {
                    if (reader!.FileExists(PakUtils.GetFilepathHash(CurrentDir))) {
                        files = [CurrentDir];
                        Log.Info($"Congratulations! You have found a new file path at {CurrentDir}!");
                    }
                }
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
        previewGenerator ??= new(contentWorkspace, EditorWindow.CurrentWindow?.GLContext!, PakFilePaths);

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

            bool isBookmarked = _bookmarks.User.IsBookmarked(Workspace.Config.Game.name, file);
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
            ImGui.PushFont(ImFontPtr.Null, UI.FontSizeLarge);
            if (Path.HasExtension(file)) {
                if (isFilePreviewEnabled) {
                    var previewStatus = previewGenerator.FetchPreview(file, out var previewTex, thumbnailDiscriminator);
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

            ShowFileContextMenu(file, _bookmarks.User.IsBookmarked(Workspace.Config.Game.name, file), true);
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
            previewGenerator ??= new(contentWorkspace, EditorWindow.CurrentWindow?.GLContext!, PakFilePaths);
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
                bool isBookmarked = _bookmarks.User.IsBookmarked(Workspace.Config.Game.name, file);
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
                        var previewStatus = previewGenerator!.FetchPreview(file, out var previewTex, thumbnailDiscriminator);
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
        if (file.Equals(PakUtils.ManifestFilepath, StringComparison.InvariantCultureIgnoreCase)) {
            var stream = reader!.GetFile(file);
            if (stream == null) {
                EditorWindow.CurrentWindow?.AddSubwindow(new ErrorModal("File not found", "File could not be found in the PAK file(s)."));
            } else {
                var pak = reader.GetPakFileOfEntry(PakUtils.GetFilepathHash(file));
                EditorWindow.CurrentWindow?.OpenFile(stream, file, pak + "://");
            }
            return true;
        }

        if (PathUtils.ParseFileFormat(file).format == KnownFileFormats.Mesh && file.Contains("streaming/", StringComparison.OrdinalIgnoreCase)) {
            Logger.Warn("Attempted to open streaming mesh. Opening the main non-streaming mesh instead.");
            file = Workspace.PrependBasePath(PathUtils.GetNonStreamingPath(file).ToString());
        }

        var bypassBundleFile = ImGui.IsKeyDown(ImGuiKey.ModAlt);
        if (!bypassBundleFile && IsFileOrFolderInBundle(file)) {
            if (contentWorkspace.CurrentBundle?.TryFindResource(contentWorkspace.Env.RemoveBasePath(file).ToString(), out var listing) == true) {
                if (contentWorkspace.ResourceManager.TryResolveGameFile(listing.Target, out var targetFile)) {
                    EditorWindow.CurrentWindow?.AddFileEditor(targetFile);
                    return true;
                }
            }
        }

        var fileExists = file.StartsWith(PakReader.UnknownFilePathPrefix) ? reader!.GetUnknownFile(file) != null : reader!.FileExists(file);
        if (!fileExists) {
            if (!baseList.FileExists(file)) {
                CurrentDir = file;
                pagination.page = 0;
                return true;
            }

            // if it's not a full list file match then it's a folder, navigate to it
            var hasLooseFile = File.Exists(Path.Combine(Workspace.Config.GamePath, file));
            if (hasLooseFile) {
                Logger.Error("File could not be found in the loaded PAK files. Matching loose file was found, the file entry may have been invalidated by Fluffy Mod Manager.");
            } else {
                Logger.Error("File could not be found in the loaded PAK files. Possible causes: Fluffy Mod Manager archive invalidation, missing some DLC content, not having the right PAK files open, or a wrong file list.");
            }
            return false;
        }

        try {
            if (file.StartsWith(PakReader.UnknownFilePathPrefix)) {
                var stream = reader!.GetUnknownFile(file);
                if (stream == null) {
                    Logger.Error("Failed to open unknown file " + file);
                } else {
                    var handle = contentWorkspace.ResourceManager.CreateCustomFileHandle(contentWorkspace.Env.AppendFileVersion(file), null, stream!);
                    EditorWindow.CurrentWindow?.AddFileEditor(handle);
                }
            } else if (PakFilePaths == null) {
                EditorWindow.CurrentWindow?.OpenFiles([file]);
            } else {
                var stream = reader.GetFile(file);
                if (stream == null) {
                    EditorWindow.CurrentWindow?.AddSubwindow(new ErrorModal("File not found", "File could not be found in the PAK file(s)."));
                } else {
                    var pak = reader.GetPakFileOfEntry(PakUtils.GetFilepathHash(file));
                    EditorWindow.CurrentWindow?.OpenFile(stream, file, (pak ?? Workspace.Config.GamePath) + "://");
                }
            }
        } catch (Exception e) {
            Logger.Error($"Failed to open file {file}: {e.Message}");
        }

        return true;
    }

    private void ShowFileContextMenu(string file, bool isBookmarked, bool showSort = false)
    {
        ImGui.SetNextWindowSize(new Vector2(300 * UI.UIScale, 0));
        if (ImGui.BeginPopupContextItem()) {
            if (ImguiHelpers.ContextMenuItem("##CopyPath", AppIcons.SI_FileCopyPath, "Copy Path", Colors.IconPrimary)) {
                EditorWindow.CurrentWindow?.CopyToClipboard(file);
            }
            var isFolder = !Path.HasExtension(file);
            if (!isFolder) {
                if (ImguiHelpers.ContextMenuItem("##ExtractFile", AppIcons.SIC_FileExtractTo, Lang.PakBrowser.ExtractFile.String, [Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary])) {
                    PlatformUtils.ShowSaveFileDialog((savePath) => {
                        var stream = file.StartsWith(PakReader.UnknownFilePathPrefix.AsSpan()[..^1]) ? reader!.GetUnknownFile(file) : reader!.GetFile(file);
                        if (stream == null) {
                            Logger.Error("Could not find file " + file + " in selected PAK files");
                            return;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
                        using var fs = File.Create(savePath);
                        stream.WriteTo(fs);
                    }, Path.GetFileName(file));
                    ImGui.CloseCurrentPopup();
                }
            }
            if (ImguiHelpers.ContextMenuItem("##Extract", AppIcons.SIC_FileExtractTo, isFolder ? Lang.PakBrowser.ExtractFolder.String : Lang.PakBrowser.ExtractFileKeepPaths.String, [Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary])) {
                // show folder unpack dialog instead
                PlatformUtils.ShowFolderDialog((output) => {
                    ExtractList(file, output);
                }, AppConfig.Instance.GetGameExtractPath(Workspace.Config.Game));
                ImGui.CloseCurrentPopup();
            }
            if (isBookmarked) {
                if (ImguiHelpers.ContextMenuItem("##RemoveBookmark", AppIcons.SIC_BookmarkRemove, "Remove from Bookmarks", [Colors.IconPrimary, Colors.IconTertiary])) {
                    _bookmarks.User.RemoveBookmark(Workspace.Config.Game.name, file);
                }
            } else {
                if (ImguiHelpers.ContextMenuItem("##AddBookmark", AppIcons.SIC_BookmarkAdd, "Add to Bookmarks", [Colors.IconPrimary, Colors.IconSecondary])) {
                    _bookmarks.User.AddBookmark(Workspace.Config.Game.name, file);
                }
            }
            if (Path.HasExtension(file) && contentWorkspace.CurrentBundle != null) {
                if (ImguiHelpers.ContextMenuItem("##SaveToBundle", AppIcons.SIC_BundleSaveTo, Lang.Buttons.SaveToBundle, [Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary, Colors.IconPrimary])) {
                    var handle = contentWorkspace.ResourceManager.GetFileHandle(file);
                    if (handle != null) {
                        ResourcePathPicker.SaveFileToBundle(contentWorkspace, handle, (savePath, localPath, nativePath) => handle.Save(contentWorkspace, savePath), closeFile: true);
                    }
                }

                if (ImguiHelpers.ContextMenuItem("##SaveToBundleNew", AppIcons.SIC_BundleSaveAsNew, Lang.Buttons.SaveToBundleNewFile, [Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary, Colors.IconPrimary])) {
                    var handle = contentWorkspace.ResourceManager.GetFileHandle(file);
                    if (handle != null) {
                        ResourcePathPicker.SaveFileToBundle(contentWorkspace, handle, (savePath, localPath, nativePath) => handle.Save(contentWorkspace, savePath), closeFile: true, useNewTargetPath: true);
                    }
                }
            }
            if (Path.HasExtension(file)) {
                if (ImguiHelpers.ContextMenuItem("##JumpToContainingFolder", AppIcons.SIC_FolderContain, "Jump to Containing Folder", [Colors.IconPrimary, Colors.IconSecondary])) {
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
    private bool IsFileOrFolderInBundle(string path)
    {
        if (contentWorkspace.CurrentBundle?.HasResources != true) return false;

        path = Workspace.RemoveBasePath(path).ToString();

        if (Path.HasExtension(path)) {
            return contentWorkspace.CurrentBundle.ContainsResource(path);
        }

        foreach (var p in contentWorkspace.CurrentBundle.Files) {
            if (p.Target.StartsWith(path, StringComparison.OrdinalIgnoreCase)) return true;
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
        var parts = path.NormalizeFilepath().Split('/');
        if (parts.Length <= 2) return path;

        return parts[^1];
    }
    private void ToggleBookmarks()
    {
        if (AppConfig.Instance.UseBookmarkWindow) {
            if (EditorWindow.CurrentWindow?.HasSubwindow<BookmarksWindow>(out var data) == true) {
                EditorWindow.CurrentWindow.CloseSubwindow(data!);
            } else {
                EditorWindow.CurrentWindow?.AddUniqueSubwindow(new BookmarksWindow(BookmarksPanel));
            }
        } else {
            isShowBookmarks = !isShowBookmarks;
        }
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
