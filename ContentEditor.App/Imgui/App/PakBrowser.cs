using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ImGuiNET;
using ReeLib;
using System.Diagnostics;
using System.Numerics;

namespace ContentEditor.App;

public partial class PakBrowser(Workspace workspace, string? pakFilePath) : IWindowHandler, IDisposable
{
    public string HandlerName => "PAK File Browser";

    public Workspace Workspace { get; } = workspace;
    public string? PakFilePath { get; } = pakFilePath;

    public string CurrentDir { get; set; } = workspace.BasePath[0..^1];
    private string? _editedDir;

    bool IWindowHandler.HasUnsavedChanges => false;
    // note: purposely not disposing the reader, in case we just reused the "main" pak reader from the workspace
    // it doesn't really need disposing with the current implementation either way
    private CachedMemoryPakReader? reader;
    private FilePreviewWindow? previewWindow;
    private WindowData data = null!;
    protected UIContext context = null!;
    private ListFileWrapper? matchedList;
    private BookmarkManager _bookmarkManagerDefaults = new BookmarkManager(Path.Combine(AppConfig.Instance.ConfigBasePath, "app/default_bookmarks_pak.json"));
    private BookmarkManager _bookmarkManager = new BookmarkManager(Path.Combine(AppConfig.Instance.ConfigBasePath, "user/bookmarks_pak.json"));
    private List<string> _activeTagFilter = new();

    private PakReader? unpacker;
    private int unpackExpectedFiles;

    private bool hasInvalidatedPaks;
    private bool isShowBookmarks = false;
    private int page;
    private int rowsPerPage = 1000;
    private int selectedRow = -1;

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

        try {
            string[] files;
            if (CurrentDir.Contains('*')) {
                files = matchedList!.FilterAllFiles(CurrentDir);
            } else if (reader.FileExists(CurrentDir)) {
                files = [CurrentDir];
            } else {
                files = matchedList!.FilterAllFiles(CurrentDir + ".*");
            }

            unpackExpectedFiles = files.Length;
            unpacker = new PakReader();
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
                Workspace.PakReader.CacheEntries();
                reader = Workspace.PakReader.Clone();
                matchedList = list;
                hasInvalidatedPaks = reader.FileExists(0);
            } else {
                // single file
                reader = new CachedMemoryPakReader();
                if (!reader.TryReadManifestFileList(PakFilePath)) {
                    reader.AddFiles(list.Files);
                }
                reader.CacheEntries(true);
                matchedList = new ListFileWrapper(reader.CachedPaths);
                hasInvalidatedPaks = reader.FileExists(0);
            }
            // TODO handle unknowns properly
        }
        if (ImGui.Button("PAK Info")) {
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
            ImguiHelpers.Tooltip("Invalidated PAK entries have been detected (most likely from Fluffy Mod Manager).\nYou may be unable to open some files.");
        }
        ImGui.SameLine();
        var usePreviewWindow = AppConfig.Instance.UsePakFilePreviewWindow.Get();
        ImguiHelpers.ToggleButton($"{AppIcons.SI_FileOpenPreview}", ref usePreviewWindow, color: ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered), 2.0f);
        ImguiHelpers.Tooltip("Open files in Preview Window");
        AppConfig.Instance.UsePakFilePreviewWindow.Set(usePreviewWindow);
        ImGui.SameLine();
        var bookmarks = _bookmarkManager.GetBookmarks(Workspace.Config.Game.name);
        var defaults = _bookmarkManagerDefaults.GetBookmarks(Workspace.Config.Game.name);
        bool isHideDefaults = _bookmarkManagerDefaults.IsHideDefaults;
        bool isBookmarked = _bookmarkManager.IsBookmarked(Workspace.Config.Game.name, CurrentDir);
        bool isDefaultBookmark = !isHideDefaults && _bookmarkManagerDefaults.IsBookmarked(Workspace.Config.Game.name, CurrentDir);
        ImguiHelpers.ToggleButton($"{AppIcons.SI_Bookmarks} Bookmarks", ref isShowBookmarks, color: ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered), 2.0f);
        if (isShowBookmarks) {
            ImGui.Spacing();
            ImGui.Separator();
            ImguiHelpers.ToggleButton($"{AppIcons.SI_BookmarkHide}", ref isHideDefaults, color: ImguiHelpers.GetColor(ImGuiCol.PlotHistogramHovered), 2.0f);
            ImguiHelpers.Tooltip("Hide Default Bookmarks");
            _bookmarkManagerDefaults.IsHideDefaults = isHideDefaults;
            if (bookmarks.Count > 0) {
                ImGui.SameLine();
                if (ImGui.Button($"{AppIcons.SI_BookmarkClear}")) {
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

            if (defaults.Count > 0) {
                if (_activeTagFilter.Count > 0) {
                    ImGui.SameLine();
                    if (ImGui.Button($"{AppIcons.SI_FilterClear}")) {
                        _activeTagFilter.Clear();
                    }
                    ImguiHelpers.Tooltip("Clear Filters");
                    ImGui.SameLine();
                    ImGui.Text("Active Filters: ");

                    foreach (var tag in _activeTagFilter) {
                        ImGui.PushID("ActiveTag_" + tag);
                        if (BookmarkManager.TagColors.TryGetValue(tag, out var colors)) {
                            ImGui.PushStyleColor(ImGuiCol.Text, colors[1]);
                        }
                        ImGui.SameLine();
                        ImGui.Text(tag);
                        ImGui.PopStyleColor();
                        ImGui.SameLine();
                        ImGui.Text("|");
                        ImGui.PopID();
                    }
                }

                if (!_bookmarkManagerDefaults.IsHideDefaults) {
                    ImGui.SeparatorText("Default");
                    if (ImGui.BeginTable("BookmarksTable", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterV | ImGuiTableFlags.RowBg)) {
                        ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch, 0.5f);
                        ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthStretch, 0.3f);
                        ImGui.TableSetupColumn("Comment", ImGuiTableColumnFlags.WidthStretch, 0.2f);
                        ImGui.TableHeadersRow();

                        foreach (var bm in defaults) {
                            if (_activeTagFilter.Count > 0 && !_activeTagFilter.Any(t => bm.Tags.Contains(t))) {
                                continue;
                            }

                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(0);
                            if (ImGui.Selectable(bm.Path, false)) {
                                CurrentDir = bm.Path;
                            }

                            ImGui.TableSetColumnIndex(1);
                            foreach (var tag in bm.Tags) {
                                ImGui.PushID($"{bm.Path}_{tag}");

                                if (BookmarkManager.TagColors.TryGetValue(tag, out var colors)) {
                                    ImGui.PushStyleColor(ImGuiCol.Button, colors[0]);
                                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colors[1]);
                                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, colors[2]);
                                } else {
                                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.5f, 0.5f, 1f));
                                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.6f, 0.6f, 0.6f, 1f));
                                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1f));
                                }

                                ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);
                                if (ImGui.Button($"[ {tag} ]")) {
                                    if (_activeTagFilter.Contains(tag)) {
                                        _activeTagFilter.Remove(tag);
                                    } else {
                                        _activeTagFilter.Add(tag);
                                    }
                                }

                                ImGui.PopStyleColor(4);
                                ImGui.PopID();
                                ImGui.SameLine();
                            }

                            ImGui.TableSetColumnIndex(2);
                            if (!string.IsNullOrEmpty(bm.Comment)) {
                                ImGui.TextDisabled(bm.Comment);
                            }
                        }
                        ImGui.EndTable();
                    }
                }
            }

            if (bookmarks.Count > 0) {
                ImGui.SeparatorText("Custom");
                if (ImGui.BeginTable("UserBookmarksTable", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersOuterV | ImGuiTableFlags.RowBg)) {
                    ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch, 0.5f);
                    ImGui.TableSetupColumn("Tags", ImGuiTableColumnFlags.WidthStretch, 0.3f);
                    ImGui.TableSetupColumn("Comment", ImGuiTableColumnFlags.WidthStretch, 0.2f);
                    ImGui.TableHeadersRow();

                    foreach (var bm in bookmarks.ToList()) {
                        if (_activeTagFilter.Count > 0 && !_activeTagFilter.Any(t => bm.Tags.Contains(t))) {
                            continue;
                        }

                        ImGui.TableNextRow();
                        ImGui.TableSetColumnIndex(0);
                        if (ImGui.Selectable(bm.Path, false))
                        {
                            CurrentDir = bm.Path;
                        }

                        if (ImGui.BeginPopupContextItem(bm.Path)) {
                            if (ImGui.Selectable($"{AppIcons.SI_FileJumpTo} | Jump to...")) {
                                CurrentDir = bm.Path;
                            }
                            if (ImGui.Selectable($"{AppIcons.SI_FileCopyPath} | Copy Path")) {
                                EditorWindow.CurrentWindow?.CopyToClipboard(bm.Path);
                            }
                            ImGui.Spacing();
                            if (ImGui.Selectable($"{AppIcons.SI_BookmarkRemove} | Remove from Bookmarks")) {
                                _bookmarkManager.RemoveBookmark(Workspace.Config.Game.name, bm.Path);
                            }
                            ImGui.Spacing();
                            if (ImGui.BeginMenu($"{AppIcons.SI_GenericTag} | Tags")) {
                                foreach (var tag in BookmarkManager.TagColors.Keys) {
                                    bool hasTag = bm.Tags.Contains(tag);
                                    if (ImGui.MenuItem(tag, "", hasTag)) {
                                        if (hasTag) {
                                            bm.Tags.Remove(tag);
                                            _bookmarkManager.SaveBookmarks();
                                        } else {
                                            bm.Tags.Add(tag);
                                            _bookmarkManager.SaveBookmarks();
                                        }
                                    }
                                }
                                ImGui.EndMenu();
                            }
                            string comment = bm.Comment;
                            if (ImGui.InputText("Edit Comment", ref comment, 64, ImGuiInputTextFlags.EnterReturnsTrue)) {
                                bm.Comment = comment;
                                _bookmarkManager.SaveBookmarks();
                            }
                            ImGui.EndPopup();
                        }
                        ImGui.TableSetColumnIndex(1);
                        foreach (var tag in bm.Tags) {
                            ImGui.PushID($"{bm.Path}_{tag}");

                            if (BookmarkManager.TagColors.TryGetValue(tag, out var colors)) {
                                ImGui.PushStyleColor(ImGuiCol.Button, colors[0]);
                                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, colors[1]);
                                ImGui.PushStyleColor(ImGuiCol.ButtonActive, colors[2]);
                            } else {
                                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.5f, 0.5f, 1f));
                                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.6f, 0.6f, 0.6f, 1f));
                                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.4f, 0.4f, 0.4f, 1f));
                            }

                            ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);
                            if (ImGui.Button($"[ {tag} ]")) {
                                if (_activeTagFilter.Contains(tag)) {
                                    _activeTagFilter.Remove(tag);
                                } else {
                                    _activeTagFilter.Add(tag);
                                }
                            }
                            ImGui.PopStyleColor(4);
                            ImGui.PopID();
                            ImGui.SameLine();
                        }
                        ImGui.TableSetColumnIndex(2);
                        if (!string.IsNullOrEmpty(bm.Comment)) {
                            ImGui.Text($"{bm.Comment}");
                        }
                    }
                    ImGui.EndTable();
                }
            } else {
                ImGui.TextDisabled("No Custom Bookmarks yet...");
            }
            ImGui.Separator();
            ImGui.Spacing();
        }

        if (ImGui.ArrowButton("##left", ImGuiDir.Left)) {
            CurrentDir = Path.GetDirectoryName(CurrentDir)?.Replace('\\', '/') ?? string.Empty;
        }
        ImguiHelpers.Tooltip("Back");
        ImGui.SameLine();

        // SILVER: Only enable the 'Return to Top' button when we are at least 3 layers deep
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
            page = 0;
        } else {
            _editedDir = null;
        }
        ImguiHelpers.Tooltip("You can use regex to match file patterns (e.g. natives/stm/character/**.mdf2.*)");
        ImGui.SameLine();
        if (isBookmarked || isDefaultBookmark) {
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
        DrawContents(matchedList!);
    }

    private void DrawContents(ListFileWrapper baseList)
    {
        if (reader == null) return;
        ImGui.BeginChild("Content");
        int p = 0, i = 0;
        var isCtrl = ImGui.IsKeyDown(ImGuiKey.ModCtrl);
        var isShift = ImGui.IsKeyDown(ImGuiKey.ModShift);
        int maxPage = 0;
        string[]? sortedEntries = null;
        var remainingHeight = ImGui.GetWindowSize().Y - ImGui.GetCursorPosY() - ImGui.GetStyle().WindowPadding.Y - UI.FontSize - ImGui.GetStyle().FramePadding.Y;
        if (ImGui.BeginTable("List", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuterV | ImGuiTableFlags.Sortable, new Vector2(0, remainingHeight))) {
            ImGui.TableSetupColumn(" Path ", ImGuiTableColumnFlags.WidthStretch, 0.9f);
            ImGui.TableSetupColumn(" Size ", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.PreferSortDescending, 100);
            ImGui.TableSetupScrollFreeze(0, 1);
            var sort = ImGui.TableGetSortSpecs();
            var cacheKey = (CurrentDir, sort.Specs.ColumnIndex, sort.Specs.SortDirection);
            if (!cachedResults.TryGetValue(cacheKey, out sortedEntries)) {
                var files = baseList.GetFiles(CurrentDir);
                var sorted = sort.Specs.ColumnIndex switch {
                    0 => sort.Specs.SortDirection == ImGuiSortDirection.Ascending ? files : files.Reverse(),
                    1 => sort.Specs.SortDirection == ImGuiSortDirection.Ascending ? files.OrderBy(e => reader.GetSize(e)) : files.OrderByDescending(e => reader.GetSize(e)),
                    _ => sort.Specs.SortDirection == ImGuiSortDirection.Ascending ? files : files.Reverse(),
                };
                cachedResults[cacheKey] = sortedEntries = sorted.ToArray();
            }
            maxPage = (int)Math.Floor((float)sortedEntries.Length / rowsPerPage);
            ImGui.TableHeadersRow();
            ImGui.TableNextColumn();
            foreach (var file in sortedEntries.Skip(rowsPerPage * page).Take(rowsPerPage)) {
                ImGui.PushID(i);
                i++;
                if (isCtrl) {
                    if (ImGui.Selectable(file, i == selectedRow, ImGuiSelectableFlags.SpanAllColumns)) {
                        selectedRow = i;
                    }
                } else if (isShift) {
                    if (ImGui.Selectable(file, i == selectedRow, ImGuiSelectableFlags.SpanAllColumns)) {
                        selectedRow = i;
                    }
                } else {
                    var usePreviewWindow = AppConfig.Instance.UsePakFilePreviewWindow.Get();
                    var bookmarks = _bookmarkManager.GetBookmarks(Workspace.Config.Game.name);
                    bool isBookmarked = _bookmarkManager.IsBookmarked(Workspace.Config.Game.name, file);
                    if (isBookmarked) {
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.PlotHistogramHovered]);
                    } else {
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
                    }
                    if (ImGui.Selectable(file, false, ImGuiSelectableFlags.SpanAllColumns)) {
                        if (!baseList.FileExists(file)) {
                            // if it's not a full list file match then it's a folder, navigate to it
                            ImGui.PopStyleColor();
                            ImGui.PopID();
                            ImGui.TableNextColumn();
                            ImGui.EndTable();
                            ImGui.EndChild();
                            CurrentDir = file;
                            return;
                        }

                        if (!reader.FileExists(file)) {
                            var hasLooseFile = File.Exists(Path.Combine(Workspace.Config.GamePath, file));
                            if (hasLooseFile) {
                                Logger.Error("File could not be found in the loaded PAK files. Matching loose file was found, the file entry may have been invalidated by Fluffy Mod Manager.");
                            } else {
                                Logger.Error("File could not be found in the loaded PAK files. Possible causes: Fluffy Mod Manager archive invalidation, missing some DLC content, not having the right PAK files open, or a wrong file list.");
                            }
                        } else {
                            if (usePreviewWindow) {
                                if (previewWindow == null || !EditorWindow.CurrentWindow!.HasSubwindow<FilePreviewWindow>(out _, w => w.Handler == previewWindow)) {
                                    EditorWindow.CurrentWindow!.AddSubwindow(previewWindow = new FilePreviewWindow());
                                }
                                if (PakFilePath != null && reader.GetFile(file) is Stream stream) {
                                    previewWindow.SetFile(stream, file, PakFilePath);
                                } else {
                                    previewWindow.SetFile(file);
                                }
                            } else if (PakFilePath == null) {
                                EditorWindow.CurrentWindow?.OpenFiles([file]);
                            } else {
                                var stream = reader.GetFile(file);
                                if (stream == null) {
                                    EditorWindow.CurrentWindow?.AddSubwindow(new ErrorModal("File not found", "File could not be found in the PAK file(s)."));
                                } else {
                                    EditorWindow.CurrentWindow?.OpenFile(stream, file, PakFilePath + "://");
                                }
                            }
                        }
                    }
                    ImGui.PopStyleColor();
                    if (ImGui.BeginPopupContextItem()) {
                        if (ImGui.Selectable($"{AppIcons.SI_FileCopyPath} | Copy Path")) {
                            EditorWindow.CurrentWindow?.CopyToClipboard(file);
                            ImGui.CloseCurrentPopup();
                        }
                        ImGui.Spacing();
                        if (ImGui.Selectable($"{AppIcons.SI_FileExtractTo} | Extract File to ...")) {
                            var nativePath = file;
                            PlatformUtils.ShowSaveFileDialog((savePath) => {
                                var stream = reader.GetFile(nativePath);
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
                        ImGui.EndPopup();
                    }
                }
                ImGui.TableNextColumn();
                var size = reader.GetSize(file);
                if (size > 0) {
                    string prettySize;
                    if (size >= 1024 * 1024) {
                        prettySize = ((float)size / (1024 * 1024)).ToString("0.00") + " MB";
                    } else {
                        prettySize = ((float)size / 1024).ToString("0.00") + " KB";
                    }
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
        using (var _ = ImguiHelpers.Disabled(page <= 0)) {
            if (ImGui.ArrowButton("##prev", ImGuiDir.Left)) {
                page--;
            }
        }
        ImGui.SameLine();
        ImGui.Text(i == 0 ? "Page 0 / 0" : $"Page {page + 1} / {maxPage + 1}");
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(page >= maxPage)) {
            if (ImGui.ArrowButton("##next", ImGuiDir.Right)) {
                page++;
            }
        }
        ImGui.SameLine();
        ImGui.Text($"Total matches: {sortedEntries?.Length} | Displaying: {page * rowsPerPage + Math.Sign(i)}-{page * rowsPerPage + i}");
        ImGui.EndChild();
    }

    public bool RequestClose()
    {
        return false;
    }

    public void Dispose()
    {
        if (previewWindow != null && !previewWindow.RequestClose()) {
            EditorWindow.CurrentWindow?.CloseSubwindow(previewWindow);
        }
    }
}
