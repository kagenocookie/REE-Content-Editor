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
    public string HandlerName => "PAK file browser";

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

    private const string ManifestFilepath = "__MANIFEST/MANIFEST.TXT";

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
                reader.PakFilePriority = [PakFilePath];
                reader.AddFiles([ManifestFilepath]);
                var embeddedList = reader.FindFiles().SingleOrDefault();
                reader.AddFiles(list.Files);
                if (embeddedList.stream != null) {
                    reader.AddFilesFromListFile(embeddedList.stream);
                }
                reader.CacheEntries(true);
                matchedList = new ListFileWrapper(reader.CachedPaths);
            }
            // TODO handle unknowns properly
        }

        ImGui.Text("Total file count: " + reader.MatchedEntryCount);
        ImGui.SameLine();
        if (PakFilePath == null) {
            if (ImGui.TreeNode("PAK count: " + reader.PakFilePriority.Count)) {
                foreach (var pak in reader.PakFilePriority) {
                    ImGui.Text(pak);
                }
                ImGui.TreePop();
            }
        } else {
            ImGui.Text("PAK file: " + PakFilePath);
        }

        if (ImGui.Button("Up")) {
            CurrentDir = Path.GetDirectoryName(CurrentDir)?.Replace('\\', '/') ?? string.Empty;
        }
        ImGui.SameLine();

        _editedDir ??= CurrentDir;
        if (ImGui.InputText("Path", ref _editedDir, 250, ImGuiInputTextFlags.EnterReturnsTrue)) {
            if (_editedDir.EndsWith('/')) _editedDir = _editedDir[0..^1];
            CurrentDir = _editedDir;
            page = 0;
        } else {
            _editedDir = null;
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
        if (ImGui.BeginTable("List", 2, ImGuiTableFlags.Sortable | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp)) {
            ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch, 0.9f);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 100);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            var sort = ImGui.TableGetSortSpecs();
            ImGui.TableNextColumn();
            foreach (var file in baseList.GetFilesInFolder(CurrentDir)) {
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
                                EditorWindow.CurrentWindow?.OpenFile(stream, file, PakFilePath + "://");
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