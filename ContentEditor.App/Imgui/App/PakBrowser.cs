using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

namespace ContentEditor.App;

public partial class PakBrowser(Workspace workspace, string? pakFilePath) : IWindowHandler
{
    public string HandlerName => " PAK File Browser ";

    public Workspace Workspace { get; } = workspace;
    public string? PakFilePath { get; } = pakFilePath;

    public string CurrentDir { get; set; } = workspace.BasePath[0..^1];
    private string? _editedDir;

    bool IWindowHandler.HasUnsavedChanges => false;
    // note: purposely not disposing the reader, in case we just reused the "main" pak reader from the workspace
    // it doesn't really need disposing with the current implementation either way
    private CachedMemoryPakReader? reader;

    private WindowData data = null!;
    protected UIContext context = null!;
    private ListFileWrapper? matchedList;

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

        ImGui.Text("Total File Count: " + reader.MatchedEntryCount);
        ImGui.SameLine();

        if (PakFilePath == null) {
            if (ImGui.TreeNode("PAK Count: " + reader.PakFilePriority.Count)) {
                ImGui.Separator();
                foreach (var pak in reader.PakFilePriority) {
                    ImGui.Text(pak);
                }
                ImGui.Separator();
                ImGui.TreePop();
            }
        } else {
            ImGui.Text("PAK file: " + PakFilePath);
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
                    if (ImGui.Selectable(file, false, ImGuiSelectableFlags.SpanAllColumns)) {
                        if (isFile) {
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
                        } else {
                            ImGui.PopID();
                            ImGui.TableNextColumn();
                            ImGui.EndTable();
                            ImGui.EndChild();
                            CurrentDir = file;
                            return;
                        }
                    }
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
}
