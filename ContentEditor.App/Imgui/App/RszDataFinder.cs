using System.Collections.Concurrent;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;

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

    private CancellationTokenSource? cancellationTokenSource;
    private int searchedFiles;

    private ConcurrentBag<string> matches = new();

    private WindowData data = null!;
    protected UIContext context = null!;

    private string[]? classNames;

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

        if (cancellationTokenSource != null) ImGui.BeginDisabled();

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
                            if (long.TryParse(tmpvalue, out var vvv)) {
                                valueString = tmpvalue;
                                value = Convert.ChangeType(tmpvalue, csType);
                            }
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

            if (!matches.IsEmpty) {
                ImGui.Text("Last search results list:");
                if (ImguiHelpers.SameLine() && ImGui.Button("Clear")) matches.Clear();
                foreach (var match in matches) {
                    ImGui.Text(match);
                    ImGui.SameLine();
                    ImGui.PushID(match);
                    if (ImGui.Button("Copy")) {
                        EditorWindow.CurrentWindow!.CopyToClipboard(match);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Open")) {
                        EditorWindow.CurrentWindow!.OpenFiles([match]);
                    }
                    ImGui.PopID();
                }
            }
        } else {
            ImGui.EndDisabled();

            ImGui.Text("Query: " + valueString);
            ImGui.Text("Search in progress...");
            ImGui.Text("Searched file count: " + searchedFiles);
            if (ImguiHelpers.SameLine() && ImGui.Button("Stop")) {
                cancellationTokenSource.Cancel();
                cancellationTokenSource = null;
            }

            foreach (var match in matches) {
                ImGui.Text(match);
                if (ImguiHelpers.SameLine() && ImGui.Button("Copy")) {
                    EditorWindow.CurrentWindow!.CopyToClipboard(match);
                }
            }
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
                    matches.Add(str);
                    window.InvokeFromUIThread(() => Logger.Info(str));
                    return;
                }

                var fieldValue = inst.Values[fieldIndex];
                if (array) {
                    var values = (IList<object>)fieldValue;
                    foreach (var v in values) {
                        if (v.Equals(value)) {
                            var str = "Found match in instance " + inst + ": " + path;
                            matches.Add(str);
                            window.InvokeFromUIThread(() => Logger.Info(str));
                        }
                    }
                } else {
                    if (fieldValue.Equals(value)) {
                        var str = "Found match in instance " + inst + ": " + path;
                        matches.Add(str);
                        window.InvokeFromUIThread(() => Logger.Info(str));
                    }
                }
            }
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}