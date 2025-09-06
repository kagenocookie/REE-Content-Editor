using ContentEditor.App.Windowing;
using ImGuiNET;
using ReeLib;
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
    private BookmarkManager _bookmarkManagerDefaults = new BookmarkManager("configs//app//default_bookmarks_pak.json"); // TODO SILVER: Add more default bookmarks
    private BookmarkManager _bookmarkManager = new BookmarkManager("configs//user//bookmarks_pak.json");
    private List<string> _activeTagFilter = new();

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    private void ExtractCurrentList(string outputDir)
    {
        if (matchedList == null || reader == null) {
            Logger.Error("File list missing");
            return;
        }

        int success = 0, fail = 0;
        int count = 0;
        var files = CurrentDir.Contains('*')
            ? matchedList!.GetFiles(CurrentDir, rowsPerPage)
            : matchedList.GetRecursiveFileList(CurrentDir, rowsPerPage);
        foreach (var file in files) {
            var path = Path.Combine(outputDir, file);
            count++;
            var stream = reader.GetFile(file);
            if (stream == null) {
                Logger.Error("Could not get file " + file);
                fail++;
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var outfs = File.Create(path);
            stream?.WriteTo(outfs);
            success++;
            if (count % 100 == 0) {
                Logger.Info($"Extracted {count} files");
            }
        }
        Logger.Info($"Successfully extracted {success}/{count} files");
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
            } else {
                // single file
                reader = new CachedMemoryPakReader();
                if (!reader.TryReadManifestFileList(PakFilePath)) {
                    reader.AddFiles(list.Files);
                }
                reader.CacheEntries(true);
                matchedList = new ListFileWrapper(reader.CachedPaths);
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
        ImGui.SameLine();
        var usePreviewWindow = AppConfig.Instance.UsePakFilePreviewWindow.Get();
        if (ImGui.Checkbox("Open files in preview window", ref usePreviewWindow)) {
            AppConfig.Instance.UsePakFilePreviewWindow.Set(usePreviewWindow);
        }
        ImGui.SameLine();
        if (ImGui.TreeNode("Bookmarks")) {
            var bookmarks = _bookmarkManager.GetBookmarks(Workspace.Config.Game.name);
            var defaults = _bookmarkManagerDefaults.GetBookmarks(Workspace.Config.Game.name);
            bool hideDefaults = _bookmarkManagerDefaults.IsHideDefaults;
            ImGui.Spacing();
            ImGui.Separator();
            if (ImGui.Checkbox("Hide Default Bookmarks", ref hideDefaults)) {
                _bookmarkManagerDefaults.IsHideDefaults = hideDefaults;
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear Custom Bookmarks")) {
                _bookmarkManager.ClearBookmarks(Workspace.Config.Game.name);
                Logger.Info($"Cleared custom bookmarks for {Workspace.Config.Game.name}");
            }
           
            if (defaults.Count > 0) {
                if (_activeTagFilter.Count > 0) {
                    ImGui.SameLine();
                    ImGui.Text("Active Tag Filters: ");
                    ImGui.SameLine();

                    foreach (var tag in _activeTagFilter) {
                        ImGui.PushID("ActiveTag_" + tag);
                        if (BookmarkManager.TagColors.TryGetValue(tag, out var colors)) {
                            ImGui.PushStyleColor(ImGuiCol.Text, colors[1]);
                        }
                        ImGui.Text(tag);
                        ImGui.PopStyleColor();
                        ImGui.SameLine();
                        ImGui.Text("|");
                        ImGui.PopID();
                        ImGui.SameLine();
                    }

                    if (ImGui.Button("Clear Filters")) {
                        _activeTagFilter.Clear();
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
                            if (ImGui.Selectable("Jump to...")) {
                                CurrentDir = bm.Path;
                            }
                            if (ImGui.Selectable("Copy Path")) {
                                EditorWindow.CurrentWindow?.CopyToClipboard(bm.Path);
                            }
                            if (ImGui.Selectable("Remove from Bookmarks")) {
                                _bookmarkManager.RemoveBookmark(Workspace.Config.Game.name, bm.Path);
                            }
                            if (ImGui.BeginMenu("Tags")) {
                                foreach (var tag in BookmarkManager.TagColors.Keys) {
                                    bool hasTag = bm.Tags.Contains(tag);
                                    if (ImGui.MenuItem(tag, "", hasTag)) {
                                        if (hasTag) {
                                            bm.Tags.Remove(tag);
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
            ImGui.TreePop();
        }

        if (ImGui.ArrowButton("##left", ImGuiDir.Left)) {
            CurrentDir = Path.GetDirectoryName(CurrentDir)?.Replace('\\', '/') ?? string.Empty;
        }
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Back");
        ImGui.SameLine();

        // SILVER: Only show the 'Return to Top' button when we are at least 3 layers deep
        if (CurrentDir.Count(c => c == '/') >= 3) {
            if (ImGui.ArrowButton("##up", ImGuiDir.Up)) {
                CurrentDir = Workspace.BasePath[0..^1];
            }
            if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Return to Top");
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
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("You can use regex to match file patterns (e.g. natives/stm/character/**.mdf2.*)");
        ImGui.SameLine();

        if (ImGui.Button("Extract to...")) {
            PlatformUtils.ShowFolderDialog(ExtractCurrentList, AppConfig.Instance.GetGameExtractPath(Workspace.Config.Game));
        }
        DrawContents(matchedList!);
    }

    private int page;
    private int rowsPerPage = 1000;
    private int selectedRow = -1;

    private void DrawContents(ListFileWrapper baseList)
    {
        if (reader == null) return;
        ImGui.BeginChild("Content");
        int p = 0, i = 0;
        var isCtrl = ImGui.IsKeyDown(ImGuiKey.ModCtrl);
        var isShift = ImGui.IsKeyDown(ImGuiKey.ModShift);
        if (ImGui.BeginTable("List", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuterV)) {
            ImGui.TableSetupColumn(" Path ", ImGuiTableColumnFlags.WidthStretch, 0.9f);
            ImGui.TableSetupColumn(" Actions ", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 100);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            ImGui.TableNextColumn();
            foreach (var file in baseList.GetFiles(CurrentDir, rowsPerPage)) {
                if (p < page) {
                    if (++i >= rowsPerPage) {
                        i = 0;
                        p++;
                    }
                    continue;
                }
                ImGui.PushID(i);
                i++;
                var isFile = reader.FileExists(file);
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
                    bool isBookmarked = bookmarks.Any(b => b.Path == file);
                    if (isBookmarked) {
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.PlotHistogramHovered]);
                    } else {
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
                    }
                    if (ImGui.Selectable(file, false, ImGuiSelectableFlags.SpanAllColumns)) {
                        if (isFile) {
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
                        } else {
                            ImGui.PopStyleColor();
                            ImGui.PopID();
                            ImGui.TableNextColumn();
                            ImGui.EndTable();
                            ImGui.EndChild();
                            CurrentDir = file;
                            return;
                        }
                    }
                    ImGui.PopStyleColor();
                    if (ImGui.BeginPopupContextItem()) {
                        if (ImGui.Selectable("Copy Path")) {
                            EditorWindow.CurrentWindow?.CopyToClipboard(file);
                            ImGui.CloseCurrentPopup();
                        }
                        if (ImGui.Selectable("Extract File to ...")) {
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
                        
                        if (isBookmarked) {
                            if (ImGui.Selectable("Remove from Bookmarks")) {
                                _bookmarkManager.RemoveBookmark(Workspace.Config.Game.name, file);
                            }
                        } else {
                            if (ImGui.Selectable("Add to Bookmarks")) {
                                _bookmarkManager.AddBookmark(Workspace.Config.Game.name, file);
                            }
                        }
                        ImGui.EndPopup();
                    }
                }
                ImGui.TableNextColumn();
                ImGui.PopID();
                ImGui.TableNextColumn();
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }

    public bool RequestClose()
    {
        return false;
    }

    public void Dispose()
    {
        if (previewWindow != null) {
            EditorWindow.CurrentWindow?.CloseSubwindow(previewWindow);
        }
    }
}
