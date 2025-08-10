using System.Collections.Concurrent;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Efx;
using ReeLib.Msg;
using ReeLib.UVar;

namespace ContentEditor.App;

public class RszDataFinder : IWindowHandler
{
    public string HandlerName => "Data Finder";

    public bool HasUnsavedChanges => false;
    private string? classname = "";
    private string valueString = "";
    private int selectedFieldIndex;
    private bool searchUserFiles = true;
    private bool searchPfb = false;
    private bool searchScn = false;
    private bool searchClassOnly = false;

    private string msgSearch = "";

    private EfxAttributeType efxAttrType;
    private string efxSearch = "";
    private string? efxAttrFilter = "";
    private bool efxFileMatchOnly = true;

    private Variable.TypeKind uvarKind;
    private string uvarSearch = "";

    private CancellationTokenSource? cancellationTokenSource;
    private int searchedFiles;
    private bool SearchInProgress => cancellationTokenSource != null;

    private ConcurrentBag<(string label, string file)> matches = new();

    private WindowData data = null!;
    protected UIContext context = null!;

    private int findType;

    private string[]? classNames;
    private static readonly string[] FindTypes = ["RSZ Data", "Messages", "EFX", "Uvar"];

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    public void OnIMGUI()
    {
        var workspace = (data.ParentWindow as IWorkspaceContainer)?.Workspace;
        if (workspace == null) {
            ImGui.TextColored(Colors.Error, "Workspace not configured");
            return;
        }

        var searching = SearchInProgress;
        if (searching) ImGui.BeginDisabled();

        ImguiHelpers.Tabs(FindTypes, ref findType);
        switch (findType) {
            case 0:
                ShowRszFind(workspace);
                break;
            case 1:
                ShowMessageFind(workspace);
                break;
            case 2:
                ShowEfxFind(workspace);
                break;
            case 3:
                ShowUvarFind(workspace);
                break;
        }
        if (searching) {
            ImGui.EndDisabled();
            ShowSearchStatus(true);
        } else {
            ShowSearchStatus(false);
        }
    }

    private void ShowSearchStatus(bool searchInProgress)
    {
        if (cancellationTokenSource != null && searchInProgress) {
            ImGui.Separator();
            ImGui.Text("Query: " + valueString);
            ImGui.Text("Search in progress...");
            ImGui.Text("Searched file count: " + searchedFiles);
            if (ImguiHelpers.SameLine() && ImGui.Button("Stop")) {
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
            }
        } else if (!matches.IsEmpty) {
            ImGui.Separator();
            ImGui.Text($"Last search results: ({matches.Count} matches)");
            if (ImguiHelpers.SameLine() && ImGui.Button("Clear")) matches.Clear();
        } else {
            return;
        }

        foreach (var (label, file) in matches) {
            if (label == file) {
                ImGui.Text(label);
            } else {
                ImGui.Text(label + " :  " + file);
            }
            ImGui.PushID(label + file);
            if (ImguiHelpers.SameLine() && ImGui.Button("Copy")) {
                EditorWindow.CurrentWindow!.CopyToClipboard(file);
            }
            if (ImguiHelpers.SameLine() && ImGui.Button("Open")) {
                EditorWindow.CurrentWindow!.OpenFiles([file]);
            }
            ImGui.PopID();
        }
    }

    private void ShowRszFind(ContentWorkspace workspace)
    {
        ImGui.InputText("Classname", ref classname, 1024);

        var cls = workspace.Env.RszParser.GetRSZClass(classname);
        if (cls == null) {
            ImGui.TextColored(Colors.Warning, "Classname not found");
            classNames ??= workspace.Env.RszParser.ClassDict.Values.Select(cs => cs.name).ToArray();
            var suggestions = classNames.OrderBy(s => s.Length).Where(cs => cs.Contains(classname, StringComparison.OrdinalIgnoreCase)).Take(100);
            ImGui.BeginListBox("Suggestions", new System.Numerics.Vector2(ImGui.CalcItemWidth(), 400));
            foreach (var sugg in suggestions) {
                if (ImGui.Button(sugg)) {
                    classname = sugg;
                }
            }
            ImGui.EndListBox();
            return;
        }

        RszField? field = null;
        ImGui.Checkbox("Search class instance only", ref searchClassOnly);
        if (!searchClassOnly) {
            var fields = cls.fields.Select(f => f.name).ToArray();
            ImGui.Combo("Field", ref selectedFieldIndex, fields, fields.Length);

            field = selectedFieldIndex >= 0 && selectedFieldIndex < cls.fields.Length ? cls.fields[selectedFieldIndex] : null;
            if (field == null) {
                ImGui.TextColored(Colors.Warning, "Select a field");
                return;
            }

            if (field.type is ReeLib.RszFieldType.Object or ReeLib.RszFieldType.Struct or ReeLib.RszFieldType.UserData) {
                ImGui.TextColored(Colors.Warning, "Not a filterable field");
                return;
            }
        }

        object? value = null;
        if (cancellationTokenSource == null) {
            Type csType;
            if (!searchClassOnly) {
                if (field!.type is RszFieldType.String or RszFieldType.RuntimeType) {
                    csType = typeof(string);
                } else {
                    try {
                        csType = RszInstance.RszFieldTypeToCSharpType(field.type);
                    } catch (Exception) {
                        ImGui.TextColored(Colors.Warning, "Not a filterable field type: " + field.type);
                        return;
                    }

                    if (!csType.Namespace!.StartsWith("System")) {
                        ImGui.TextColored(Colors.Warning, "Not a filterable field type: " + csType);
                        return;
                    }
                }

                var tmpvalue = valueString;
                switch (field.type) {
                    case RszFieldType.String:
                    case RszFieldType.RuntimeType:
                        ImGui.InputText("Value", ref valueString, 100);
                        break;
                    case RszFieldType.U8 or RszFieldType.U16 or RszFieldType.U32 or RszFieldType.U64:
                    case RszFieldType.S8 or RszFieldType.S16 or RszFieldType.S32 or RszFieldType.S64:
                        if (ImGui.InputText("Value", ref tmpvalue, 100)) {
                        }
                        if (long.TryParse(tmpvalue, out var vvv)) {
                            valueString = tmpvalue;
                            value = Convert.ChangeType(tmpvalue, csType);
                        }
                        break;
                    case RszFieldType.Guid:
                        ImGui.InputText("Value", ref valueString, 100);
                        value = Guid.TryParse(valueString, out var gg) ? gg : null;
                        break;
                    case RszFieldType.Bool:
                        {
                            var b = valueString == "true";
                            ImGui.Checkbox("Value", ref b);
                            valueString = b ? "true" : "false";
                            value = b;
                        }
                        break;
                    default:
                        ImGui.TextColored(Colors.Warning, "Not a filterable field type: " + field.type);
                        return;
                }
                if (value == null) {
                    ImGui.TextColored(Colors.Danger, "Could not parse value");
                    return;
                }
            }

            ImGui.Separator();
            ImGui.Text("Parsed value: " + value);

            ImGui.Checkbox("Search user files", ref searchUserFiles);
            ImGui.Checkbox("Search SCN files", ref searchScn);
            ImGui.Checkbox("Search PFB files", ref searchPfb);
        } else {
            return;
        }

        if (ImGui.Button("Run search")) {
            matches.Clear();
            cancellationTokenSource = new();
            var (user, pfb, scn) = (searchUserFiles, searchPfb, searchScn);
            var token = cancellationTokenSource.Token;
            searchedFiles = 0;
            Task.Run(() => {
                if (user) InvokeSearchUser(workspace, (EditorWindow)data.ParentWindow, cls, selectedFieldIndex, field?.array ?? false, value, token);
                if (token.IsCancellationRequested) return;
                if (pfb) InvokeSearchPfb(workspace, (EditorWindow)data.ParentWindow, cls, selectedFieldIndex, field?.array ?? false, value, token);
                if (token.IsCancellationRequested) return;
                if (scn) InvokeSearchScn(workspace, (EditorWindow)data.ParentWindow, cls, selectedFieldIndex, field?.array ?? false, value, token);
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
            });
        }
    }

    private void ShowMessageFind(ContentWorkspace workspace)
    {
        ImGui.InputText("Query", ref msgSearch, 100);

        if (!string.IsNullOrEmpty(msgSearch) && ImGui.Button("Search")) {
            matches.Clear();
            cancellationTokenSource = new();
            var (user, pfb, scn) = (searchUserFiles, searchPfb, searchScn);
            var token = cancellationTokenSource.Token;
            Task.Run(() => {
                InvokeSearchMsg(workspace, (EditorWindow)data.ParentWindow, msgSearch, token);
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
            });
        }
    }

    private void ShowEfxFind(ContentWorkspace workspace)
    {
        ImguiHelpers.FilterableCSharpEnumCombo("Type", ref efxAttrType, ref efxAttrFilter);
        ImGui.InputText("Query", ref efxSearch, 100);
        ImGui.Checkbox("Match per file", ref efxFileMatchOnly);

        if (efxAttrType == EfxAttributeType.Unknown && string.IsNullOrEmpty(efxSearch)) return;

        if (ImGui.Button("Search")) {
            matches.Clear();
            cancellationTokenSource = new();
            var (user, pfb, scn) = (searchUserFiles, searchPfb, searchScn);
            var token = cancellationTokenSource.Token;
            Task.Run(() => {
                InvokeSearchEfx(workspace, (EditorWindow)data.ParentWindow, efxAttrType, efxSearch, token);
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
            });
        }
    }

    private void ShowUvarFind(ContentWorkspace workspace)
    {
        ImguiHelpers.CSharpEnumCombo("Type", ref uvarKind);
        ImGui.InputText("Query", ref uvarSearch, 100);

        if (uvarKind == Variable.TypeKind.Unknown && string.IsNullOrEmpty(uvarSearch)) return;

        if (ImGui.Button("Search")) {
            matches.Clear();
            cancellationTokenSource = new();
            var (user, pfb, scn) = (searchUserFiles, searchPfb, searchScn);
            var token = cancellationTokenSource.Token;
            Task.Run(() => {
                InvokeSearchUvar(workspace, (EditorWindow)data.ParentWindow, uvarKind, uvarSearch, token);
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
            });
        }
    }

    private static string LimitLength(string str, int maxlen) => str.Length <= maxlen - 2 ? str : str[0..^(maxlen - 3)] + "...";

    private void InvokeSearchMsg(ContentWorkspace workspace, EditorWindow window, string query, CancellationToken token)
    {
        foreach (var (path, stream) in workspace.Env.GetFilesWithExtension("msg", token)) {
            try {
                if (token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = new MsgFile(new FileHandler(stream, path));
                file.Read();
                foreach (var entry in file.Entries) {
                    if (Guid.TryParse(query, out var guid)) {
                        if (entry.Guid == guid) {
                            var summary = entry.Name + " = " + LimitLength(entry.GetMessage(Language.English), 50);
                            var str = "Found matching entry " + summary;
                            matches.Add((summary, path));
                            window.InvokeFromUIThread(() => Logger.Info(str));
                            return;
                        }
                    } else if (entry.Name.Contains(query, StringComparison.InvariantCultureIgnoreCase) || entry.GetMessage(Language.English).Contains(query, StringComparison.InvariantCultureIgnoreCase)) {
                        var summary = entry.Name + " = " + LimitLength(entry.GetMessage(Language.English), 50);
                        var str = "Found matching entry " + summary;
                        matches.Add((summary, path));
                        window.InvokeFromUIThread(() => Logger.Info(str));
                    }
                }
            } catch (Exception e) {
                window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    private void InvokeSearchUser(ContentWorkspace workspace, EditorWindow window, RszClass cls, int fieldIndex, bool array, object? value, CancellationToken token)
    {
        foreach (var (path, stream) in workspace.Env.GetFilesWithExtension("user", token)) {
            try {
                if (token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = new UserFile(workspace.Env.RszFileOption, new FileHandler(stream, path));
                file.Read();
                HandleInstanceList(file.RSZ, cls, fieldIndex, array, value, window, path);
            } catch (Exception e) {
                window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    private void InvokeSearchPfb(ContentWorkspace workspace, EditorWindow window, RszClass cls, int fieldIndex, bool array, object? value, CancellationToken token)
    {
        foreach (var (path, stream) in workspace.Env.GetFilesWithExtension("pfb", token)) {
            try {
                if (token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = new PfbFile(workspace.Env.RszFileOption, new FileHandler(stream, path));
                file.Read();
                HandleInstanceList(file.RSZ, cls, fieldIndex, array, value, window, path);
            } catch (Exception e) {
                window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    private void InvokeSearchScn(ContentWorkspace workspace, EditorWindow window, RszClass cls, int fieldIndex, bool array, object? value, CancellationToken token)
    {
        foreach (var (path, stream) in workspace.Env.GetFilesWithExtension("scn", token)) {
            try {
                if (token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = new ScnFile(workspace.Env.RszFileOption, new FileHandler(stream, path));
                file.Read();
                HandleInstanceList(file.RSZ, cls, fieldIndex, array, value, window, path);
            } catch (Exception e) {
                window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    private void HandleInstanceList(RSZFile rsz, RszClass cls, int fieldIndex, bool array, object? value, EditorWindow window, string path)
    {
        foreach (var inst in rsz.InstanceList) {
            if (inst.RszClass == cls && inst.RSZUserData == null) {
                if (value == null) {
                    var str = "Found instance in file " + path;
                    matches.Add((path, path));
                    window.InvokeFromUIThread(() => Logger.Info(str));
                    return;
                }

                var fieldValue = inst.Values[fieldIndex];
                if (array) {
                    var values = (IList<object>)fieldValue;
                    foreach (var v in values) {
                        if (v.Equals(value)) {
                            var str = "Found match in instance " + inst + ": " + path;
                            matches.Add((path, path));
                            window.InvokeFromUIThread(() => Logger.Info(str));
                        }
                    }
                } else {
                    if (fieldValue.Equals(value)) {
                        var str = "Found match in instance " + inst + ": " + path;
                        matches.Add((path, path));
                        window.InvokeFromUIThread(() => Logger.Info(str));
                    }
                }
            }
        }
    }

    private void InvokeSearchEfx(ContentWorkspace workspace, EditorWindow window, EfxAttributeType type, string query, CancellationToken token)
    {
        foreach (var (path, stream) in workspace.Env.GetFilesWithExtension("efx", token)) {
            try {
                if (token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = new EfxFile(new FileHandler(stream, path));
                file.Read();

                if (efxFileMatchOnly && !string.IsNullOrEmpty(query) && true == Path.GetFileNameWithoutExtension(file.FileHandler.FilePath)?.Contains(query, StringComparison.OrdinalIgnoreCase)) {
                    matches.Add((path, path));
                    continue;
                }

                foreach (var entry in file.Entries.Cast<EFXEntryBase>().Concat(file.Actions)) {
                    var match1 = (type == EfxAttributeType.Unknown || entry.Contains(type));
                    var match2 = string.IsNullOrEmpty(query) || entry.name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

                    if (match1 && match2) {
                        var desc = entry is EFXEntry ? $"entry {entry.name}" : $"action {entry.name}";
                        var str = $"Found instance ({desc}) in file {path}";
                        matches.Add((desc, path));
                        window.InvokeFromUIThread(() => Logger.Info(str));
                        if (efxFileMatchOnly) break; else continue;
                    }
                }
            } catch (Exception e) {
                window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    private void InvokeSearchUvar(ContentWorkspace workspace, EditorWindow window, Variable.TypeKind type, string query, CancellationToken token)
    {
        foreach (var (path, stream) in workspace.Env.GetFilesWithExtension("uvar", token)) {
            try {
                if (token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = new UVarFile(new FileHandler(stream, path));
                file.Read();

                foreach (var uv in file.Variables) {
                    if (type != Variable.TypeKind.Unknown && type != uv.type) continue;
                    if (!string.IsNullOrEmpty(query) && !uv.Name.Contains(query)) continue;

                    matches.Add((uv.Name, path));
                }

                foreach (var embed in file.EmbeddedUVARs) {
                    foreach (var uv in embed.Variables) {
                        if (type != Variable.TypeKind.Unknown && type != uv.type) continue;
                        if (!string.IsNullOrEmpty(query) && !uv.Name.Contains(query)) continue;

                        var desc = $"[{embed.Header.name}] {uv.Name}";
                        matches.Add((desc, path));
                    }
                }
            } catch (Exception e) {
                window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}