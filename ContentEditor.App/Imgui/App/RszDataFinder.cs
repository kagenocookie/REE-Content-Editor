using System.Collections;
using System.Collections.Concurrent;
using System.Text;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ImGuiNET;
using ReeLib;
using ReeLib.Common;
using ReeLib.Efx;
using ReeLib.Msg;
using ReeLib.Pfb;
using ReeLib.Scn;
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
    private bool searchByClass = false;
    private bool searchClassOnly = false;
    private string rszFieldType = "";

    private bool searchAllGames;

    private string msgSearch = "";

    private EfxAttributeType efxAttrType;
    private string efxSearch = "";
    private string efxAttrFilter = "";
    private bool efxFileMatchOnly = true;

    private Variable.TypeKind uvarKind;
    private string uvarSearch = "";

    private string motSearch = "";

    private string guiSearch = "";

    private CancellationTokenSource? cancellationTokenSource;
    private int searchedFiles;
    private bool SearchInProgress => cancellationTokenSource != null;

    private ConcurrentBag<(GameIdentifier game, string? label, string file)> matches = new();
    private GameIdentifier currentGame;

    private WindowData data = null!;
    protected UIContext context = null!;

    private SearchContext? searchContext;

    private int findType;

    private string[]? classNames;
    private static readonly string[] FindTypes = ["RSZ Data", "Messages", "EFX", "Uvar", "Mot", "GUI"];
    private static readonly Dictionary<string, RszFieldType[]> RszFilterableFields = new () {
        { "String / Resource", [RszFieldType.String, RszFieldType.Resource, RszFieldType.RuntimeType] },
        { "Userdata Reference", [RszFieldType.UserData] },
        { "Signed Integer", [RszFieldType.S64, RszFieldType.S32, RszFieldType.S16, RszFieldType.S8, RszFieldType.Enum] },
        { "Unsigned Integer", [RszFieldType.U64, RszFieldType.U32, RszFieldType.U16, RszFieldType.UByte, RszFieldType.Enum] },
        { "Guid", [RszFieldType.Guid] },
    };
    private static readonly string[] RszFilterTypes = RszFilterableFields.Keys.ToArray();

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

        currentGame = workspace.Env.Config.Game;
        var searching = SearchInProgress;
        if (searching) ImGui.BeginDisabled();

        ImGui.Checkbox("Search all configured games", ref searchAllGames);

        ImguiHelpers.Tabs(FindTypes, ref findType);
        switch (findType) {
            case 0:
                ImGui.Checkbox("Search by specific class", ref searchByClass);
                if (searchByClass) {
                    ShowRszClassSearch(workspace.Env);
                } else {
                    ShowRszFieldSearch(workspace.Env);
                }
                break;
            case 1:
                ShowMessageFind(workspace.Env);
                break;
            case 2:
                ShowEfxFind(workspace.Env);
                break;
            case 3:
                ShowUvarFind(workspace.Env);
                break;
            case 4:
                ShowMotFind(workspace.Env);
                break;
            case 5:
                ShowGuiFind(workspace.Env);
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
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            }
        } else if (!matches.IsEmpty) {
            ImGui.Separator();
            ImGui.Text($"Last search results: ({matches.Count} matches)");
            if (ImguiHelpers.SameLine() && ImGui.Button("Clear")) matches.Clear();
        } else {
            return;
        }

        foreach (var (game, label, file) in matches) {
            var isCurrent = currentGame == game;
            var usedLabel = string.IsNullOrEmpty(label) || label == file ? file : label;
            var displayLabel = isCurrent ? usedLabel : $"[{game}] {usedLabel}";
            if (usedLabel == file) {
                ImGui.Text(displayLabel);
            } else {
                ImGui.Text(displayLabel + " :  " + file);
            }
            ImGui.PushID(displayLabel + file);
            if (ImguiHelpers.SameLine() && ImGui.Button("Copy")) {
                EditorWindow.CurrentWindow!.CopyToClipboard(displayLabel + " :  " + file, "Copied!");
            }
            if (ImguiHelpers.SameLine() && ImGui.Button("Open")) {
                if (isCurrent) {
                    EditorWindow.CurrentWindow!.OpenFiles([file]);
                } else {
                    MainLoop.Instance.InvokeFromUIThread(() => {
                        var window = UI.OpenWindow(null);
                        window.SetWorkspace(game, null);
                        window.InvokeFromUIThread(() => window.OpenFiles([file]));
                    });
                }
            }
            ImGui.PopID();
        }
    }

    private void ShowRszFieldSearch(Workspace env)
    {
        ImguiHelpers.ValueCombo("Field type", RszFilterTypes, RszFilterTypes, ref rszFieldType);
        if (!RszFilterableFields.TryGetValue(rszFieldType, out var targetTypes)) {
            return;
        }

        var value = RszValueInput(targetTypes[0]);
        ImGui.Checkbox("Search user files", ref searchUserFiles);
        ImGui.Checkbox("Search SCN files", ref searchScn);
        ImGui.Checkbox("Search PFB files", ref searchPfb);

        if (cancellationTokenSource != null) {
            return;
        }

        if (ImGui.Button("Run search")) {
            matches.Clear();
            cancellationTokenSource = new();
            var (user, pfb, scn) = (searchUserFiles, searchPfb, searchScn);
            var token = cancellationTokenSource.Token;
            searchedFiles = 0;
            Task.Run(() => {
                foreach (var ee in GetWorkspaces(env)) {
                    var ctx = CreateContext(ee, env);

                    if (user) InvokeRszSearchField(ctx, "user", (fo, fh) => new UserFile(fo, fh), rszFieldType, false, value);
                    if (token.IsCancellationRequested) return;
                    if (pfb) InvokeRszSearchField(ctx, "pfb", (fo, fh) => new PfbFile(fo, fh), rszFieldType, false, value);
                    if (token.IsCancellationRequested) return;
                    if (scn) InvokeRszSearchField(ctx, "scn", (fo, fh) => new ScnFile(fo, fh), rszFieldType, false, value);
                    if (token.IsCancellationRequested) return;
                }
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            });
        }
    }

    private void ShowRszClassSearch(Workspace env)
    {
        ImGui.InputText("Classname", ref classname, 1024);
        RszClass? cls;
        try {
            cls = env.RszParser.GetRSZClass(classname);
        } catch (Exception) {
            ImGui.TextColored(Colors.Error, "RSZ files not supported for this game.");
            return;
        }

        if (cls == null) {
            ImGui.TextColored(Colors.Warning, "Classname not found");
            classNames ??= env.RszParser.ClassDict.Values.Select(cs => cs.name).ToArray();
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
        if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Search for any instance of this class, regardless of what fields it contains");
        object? value = null;
        if (!searchClassOnly) {
            var fields = cls.fields.Select(f => f.name).ToArray();
            if (fields.Length == 0) {
                if (cls.name != "") {
                    ImGui.TextColored(Colors.Warning, "Chosen class has no serialized fields. Search by class only if you wish to find instances of it.");
                }
                return;
            }
            ImGui.Combo("Field", ref selectedFieldIndex, fields, fields.Length);

            field = selectedFieldIndex >= 0 && selectedFieldIndex < cls.fields.Length ? cls.fields[selectedFieldIndex] : null;
            value = RszValueInput(field?.type);
        }

        ImGui.Checkbox("Search user files", ref searchUserFiles);
        ImGui.Checkbox("Search SCN files", ref searchScn);
        ImGui.Checkbox("Search PFB files", ref searchPfb);

        if (cancellationTokenSource != null) {
            return;
        }

        if (ImGui.Button("Run search")) {
            matches.Clear();
            cancellationTokenSource = new();
            var (user, pfb, scn) = (searchUserFiles, searchPfb, searchScn);
            var token = cancellationTokenSource.Token;
            searchedFiles = 0;
            Task.Run(() => {
                foreach (var ee in GetWorkspaces(env)) {
                    var curCls = ee.RszParser.GetRSZClass(cls.name);
                    if (curCls == null) continue;
                    var ctx = CreateContext(ee, env);

                    if (user) InvokeRszSearchClass(ctx, "user", (fo, fh) => new UserFile(fo, fh), curCls, selectedFieldIndex, field?.array ?? false, value);
                    if (token.IsCancellationRequested) return;
                    if (pfb) InvokeRszSearchClass(ctx, "pfb", (fo, fh) => new PfbFile(fo, fh), curCls, selectedFieldIndex, field?.array ?? false, value);
                    if (token.IsCancellationRequested) return;
                    if (scn) InvokeRszSearchClass(ctx, "scn", (fo, fh) => new ScnFile(fo, fh), curCls, selectedFieldIndex, field?.array ?? false, value);
                    if (token.IsCancellationRequested) return;
                }
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            });
        }
    }

    private object? RszValueInput(RszFieldType? type)
    {
        if (type == null) {
            ImGui.TextColored(Colors.Warning, "Select a field");
            return null;
        }

        if (type is ReeLib.RszFieldType.Object or ReeLib.RszFieldType.Struct) {
            ImGui.TextColored(Colors.Warning, "Not a filterable field");
            return null;
        }
        object? value = null;
        if (cancellationTokenSource == null) {
            Type csType;
            if (!searchClassOnly) {
                if (type is RszFieldType.String or RszFieldType.RuntimeType or RszFieldType.Resource or RszFieldType.UserData) {
                    csType = typeof(string);
                } else {
                    try {
                        csType = RszInstance.RszFieldTypeToCSharpType(type.Value);
                    } catch (Exception) {
                        ImGui.TextColored(Colors.Warning, "Not a filterable field type: " + type.Value);
                        return null;
                    }

                    if (!csType.Namespace!.StartsWith("System")) {
                        ImGui.TextColored(Colors.Warning, "Not a filterable field type: " + csType);
                        return null;
                    }
                }

                var tmpvalue = valueString;
                switch (type) {
                    case RszFieldType.String:
                    case RszFieldType.RuntimeType:
                    case RszFieldType.Resource:
                    case RszFieldType.UserData:
                        ImGui.InputText("Value", ref valueString, 400);
                        value = valueString;
                        break;
                    case RszFieldType.U8 or RszFieldType.U16 or RszFieldType.U32 or RszFieldType.U64:
                        ImGui.InputText("Value", ref tmpvalue, 400);
                        if (ulong.TryParse(tmpvalue, out _)) {
                            valueString = tmpvalue;
                            value = Convert.ChangeType(tmpvalue, csType);
                        }
                        break;
                    case RszFieldType.S8 or RszFieldType.S16 or RszFieldType.S32 or RszFieldType.S64:
                        ImGui.InputText("Value", ref tmpvalue, 400);
                        if (long.TryParse(tmpvalue, out _)) {
                            valueString = tmpvalue;
                            value = Convert.ChangeType(tmpvalue, csType);
                        }
                        break;
                    case RszFieldType.Guid:
                        ImGui.InputText("Value", ref valueString, 100);
                        value = Guid.TryParse(valueString, out var gg) ? gg : null;
                        break;
                    case RszFieldType.Bool: {
                            var b = valueString == "true";
                            ImGui.Checkbox("Value", ref b);
                            valueString = b ? "true" : "false";
                            value = b;
                        }
                        break;
                    default:
                        ImGui.TextColored(Colors.Warning, "Not a filterable field type: " + type);
                        return null;
                }
                if (value == null) {
                    ImGui.TextColored(Colors.Danger, "Could not parse value");
                    return null;
                }
            }

            ImGui.Separator();
            ImGui.Text("Parsed value: " + value);
        }

        return value;
    }

    private void ShowMessageFind(Workspace env)
    {
        ImGui.InputText("Query", ref msgSearch, 100);

        if (!string.IsNullOrEmpty(msgSearch) && ImGui.Button("Search")) {
            matches.Clear();
            cancellationTokenSource = new();
            var token = cancellationTokenSource.Token;
            searchedFiles = 0;
            Task.Run(() => {
                foreach (var ee in GetWorkspaces(env)) {
                    InvokeSearchMsg(CreateContext(ee, env), msgSearch);
                }
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            });
        }
    }

    private void ShowEfxFind(Workspace env)
    {
        ImguiHelpers.FilterableCSharpEnumCombo("Type", ref efxAttrType, ref efxAttrFilter);
        ImGui.InputText("Query", ref efxSearch, 100);
        ImGui.Checkbox("Match per file", ref efxFileMatchOnly);

        if (efxAttrType == EfxAttributeType.Unknown && string.IsNullOrEmpty(efxSearch)) return;

        if (ImGui.Button("Search")) {
            matches.Clear();
            cancellationTokenSource = new();
            var token = cancellationTokenSource.Token;
            searchedFiles = 0;
            Task.Run(() => {
                foreach (var ee in GetWorkspaces(env)) {
                    InvokeSearchEfx(CreateContext(ee, env), efxAttrType, efxSearch);
                }
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            });
        }
    }

    private void ShowUvarFind(Workspace env)
    {
        ImguiHelpers.CSharpEnumCombo("Type", ref uvarKind);
        ImGui.InputText("Query", ref uvarSearch, 100);

        if (uvarKind == Variable.TypeKind.Unknown && string.IsNullOrEmpty(uvarSearch)) return;

        if (ImGui.Button("Search")) {
            matches.Clear();
            cancellationTokenSource = new();
            var token = cancellationTokenSource.Token;
            searchedFiles = 0;
            Task.Run(() => {
                foreach (var ee in GetWorkspaces(env)) {
                    InvokeSearchUvar(CreateContext(ee, env), uvarKind, uvarSearch);
                }
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            });
        }
    }

    private void ShowMotFind(Workspace env)
    {
        ImGui.InputText("Query", ref motSearch, 100);

        if (string.IsNullOrEmpty(motSearch)) return;

        if (ImGui.Button("Search")) {
            matches.Clear();
            cancellationTokenSource = new();
            var token = cancellationTokenSource.Token;
            searchedFiles = 0;
            Task.Run(() => {
                foreach (var ee in GetWorkspaces(env)) {
                    InvokeSearchMot(CreateContext(ee, env), motSearch);
                }
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            });
        }
    }

    private void ShowGuiFind(Workspace env)
    {
        ImGui.InputText("Query", ref guiSearch, 100);

        if (string.IsNullOrEmpty(guiSearch)) return;

        if (ImGui.Button("Search")) {
            matches.Clear();
            cancellationTokenSource = new();
            var token = cancellationTokenSource.Token;
            searchedFiles = 0;
            Task.Run(() => {
                foreach (var ee in GetWorkspaces(env)) {
                    InvokeSearchGui(CreateContext(ee, env), guiSearch);
                }
                cancellationTokenSource?.Cancel();
                cancellationTokenSource = null;
            });
        }
    }

    private void AddMatch(SearchContext context, string? description, string path, string? log = null)
    {
        if (log != null) {
            context.Window.InvokeFromUIThread(() => Logger.Info(log));
        }
        matches.Add((context.Env.Config.Game, description ?? "", path));
    }

    private sealed class SearchContext(Workspace env, bool isActiveEnv, EditorWindow window, CancellationToken token)
    {
        public Workspace Env { get; } = env;
        public bool IsActiveEnv { get; } = isActiveEnv;
        public EditorWindow Window { get; } = window;
        public CancellationToken Token { get; } = token;
    }

    private void InvokeSearchMsg(SearchContext context, string query)
    {
        foreach (var (path, stream) in context.Env.GetFilesWithExtension("msg", context.Token)) {
            try {
                if (context.Token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = new MsgFile(new FileHandler(stream, path));
                file.Read();
                foreach (var entry in file.Entries) {
                    if (Guid.TryParse(query, out var guid)) {
                        if (entry.Guid == guid) {
                            var summary = entry.Name + " = " + LimitLength(entry.GetMessage(Language.English), 50);
                            AddMatch(context, summary, path);
                            return;
                        }
                    } else if (entry.Name.Contains(query, StringComparison.InvariantCultureIgnoreCase) || entry.GetMessage(Language.English).Contains(query, StringComparison.InvariantCultureIgnoreCase)) {
                        var summary = entry.Name + " = " + LimitLength(entry.GetMessage(Language.English), 50);
                        AddMatch(context, summary, path);
                    }
                }
            } catch (Exception e) {
                context.Window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    private void InvokeRszSearchClass(SearchContext context, string ext, Func<RszFileOption, FileHandler, BaseRszFile> fileFact, RszClass cls, int fieldIndex, bool array, object? value)
    {
        Func<object?, object?, bool> equalityComparer;
        if (value == null) {
            equalityComparer = (object? a, object? b) => a == null && b == null;
        } else if (cls.fields[fieldIndex].type is RszFieldType.String or RszFieldType.RuntimeType or RszFieldType.Resource) {
            equalityComparer = (object? a, object? b) => (a as string)?.Equals(b as string, StringComparison.InvariantCultureIgnoreCase) == true;
        } else if (cls.fields[fieldIndex].type is RszFieldType.UserData) {
            if (context.Env.IsEmbeddedUserdata) {
                var hash = MurMur3HashUtils.GetHash((string)value!);
                equalityComparer = (object? a, object? b) => ((a as RszInstance)?.RSZUserData as RSZUserDataInfo_TDB_LE_67)?.jsonPathHash == hash;
            } else {
                equalityComparer = (object? a, object? b) => ((a as RszInstance)?.RSZUserData as RSZUserDataInfo)?.Path?.Equals(b as string, StringComparison.InvariantCultureIgnoreCase) == true;
            }
        } else {
            equalityComparer = (object? a, object? b) => a?.Equals(b) == true;
        }

        foreach (var (path, stream) in context.Env.GetFilesWithExtension(ext, context.Token)) {
            try {
                if (context.Token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = fileFact.Invoke(context.Env.RszFileOption, new FileHandler(stream, path));
                file.Read();
                var rsz = file.GetRSZ()!;

                foreach (var inst in rsz.InstanceList) {
                    if (context.Token.IsCancellationRequested) return;
                    if (inst.RszClass == cls && inst.RSZUserData == null) {
                        if (value == null) {
                            AddMatch(context, FindPathToRszObject(rsz, inst, file), path);
                            break;
                        }

                        var fieldValue = inst.Values[fieldIndex];
                        if (array) {
                            var values = (IList<object>)fieldValue;
                            foreach (var v in values) {
                                if (equalityComparer(v, value)) {
                                    AddMatch(context, FindPathToRszObject(rsz, inst, file), path);
                                }
                            }
                        } else {
                            if (equalityComparer(fieldValue, value)) {
                                AddMatch(context, FindPathToRszObject(rsz, inst, file), path);
                            }
                        }
                    }
                }
            } catch (Exception e) {
                context.Window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    private void InvokeRszSearchField(SearchContext context, string ext, Func<RszFileOption, FileHandler, BaseRszFile> fileFact, string filterType, bool array, object? queryValue)
    {
        var fieldTypes = RszFilterableFields[filterType];
        Func<object?, object?, bool> equalityComparer;
        if (fieldTypes.Contains(RszFieldType.String)) {
            equalityComparer = (object? a, object? b) => (a as string)?.Equals(b as string, StringComparison.InvariantCultureIgnoreCase) == true;
        } else if (fieldTypes.Contains(RszFieldType.UserData)) {
            if (context.Env.IsEmbeddedUserdata) {
                var hash = MurMur3HashUtils.GetHash((string)queryValue!);
                equalityComparer = (object? a, object? b) => ((a as RszInstance)?.RSZUserData as RSZUserDataInfo_TDB_LE_67)?.jsonPathHash == hash;
            } else {
                equalityComparer = (object? a, object? b) => ((a as RszInstance)?.RSZUserData as RSZUserDataInfo)?.Path?.Equals(b as string, StringComparison.InvariantCultureIgnoreCase) == true;
            }
        } else {
            equalityComparer = (object? a, object? b) => a == null || b == null ? a == b : Convert.ChangeType(a, b.GetType()).Equals(b) == true;
        }

        foreach (var (path, stream) in context.Env.GetFilesWithExtension(ext, context.Token)) {
            try {
                if (context.Token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = fileFact.Invoke(context.Env.RszFileOption, new FileHandler(stream, path));
                file.Read();

                var rsz = file.GetRSZ()!;
                if (fieldTypes[0] == RszFieldType.Guid && file is ScnFile scn) {
                    foreach (var gi in scn.GameObjectInfoList) {
                        if (equalityComparer(gi.guid, queryValue)) {
                            var obj = scn.RSZ.ObjectList[gi.objectId];
                            AddMatch(context, FindPathToRszObject(rsz, obj, file), path);
                            break;
                        }
                    }
                }

                foreach (var inst in rsz.InstanceList) {
                    if (inst.RSZUserData != null) continue;
                    foreach (var field in inst.Fields) {
                        if (!fieldTypes.Contains(field.type)) continue;

                        if (queryValue == null) {
                            AddMatch(context, $"{FindPathToRszObject(rsz, inst, file)} {field.name} = {inst.GetFieldValue(field.name)}", path);
                            return;
                        }

                        var fieldValue = inst.GetFieldValue(field.name);
                        if (field.array) {
                            var values = (IList<object>)fieldValue!;
                            foreach (var v in values) {
                                if (equalityComparer(v, queryValue)) {
                                    AddMatch(context, FindPathToRszObject(rsz, inst, file), path);
                                }
                            }
                        } else {
                            if (equalityComparer(fieldValue, queryValue)) {
                                AddMatch(context, FindPathToRszObject(rsz, inst, file), path);
                            }
                        }
                    }
                }
            } catch (Exception e) {
                context.Window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    [ThreadStatic] private static StringBuilder? stringBuilder;
    private static string? FindPathToRszObject(RSZFile file, RszInstance instance, BaseRszFile parentFile)
    {
        stringBuilder ??= new();
        stringBuilder.Clear();
        var instanceHierarchy = new List<RszInstance>();
        instanceHierarchy.Add(instance);
        while (true) {
            // start searching from the instance index + 1 -- most of the time, the parent instance would be just after the instance itself
            var searchedObject = instanceHierarchy.Last();
            var searchStartIndex = instance.Index == -1 ? 0 : instance.Index;
            var index = searchStartIndex;
            var found = false;
            while (true) {
                index = (index + 1) % file.InstanceInfoList.Count;
                if (index == searchStartIndex) break;
                var nextInstance = file.InstanceList[index];
                if (nextInstance == searchedObject || nextInstance.RSZUserData != null) {
                    continue;
                }

                foreach (var value in nextInstance.Values) {
                    if (value == searchedObject || value is IList list && list.Contains(searchedObject)) {
                        found = true;
                        instanceHierarchy.Add(nextInstance);
                        break;
                    }
                }
            }
            if (!found) break;
        }

        for (int i = 0; i < instanceHierarchy.Count - 1; ++i) {
            var nextInstance = instanceHierarchy[instanceHierarchy.Count - 1 - i];
            var childInstance = instanceHierarchy[instanceHierarchy.Count - 2 - i];
            for (int f = 0; f < nextInstance.Fields.Length; f++) {
                var field = nextInstance.Fields[f];
                var fieldValue = nextInstance.Values[f];
                if (fieldValue == childInstance) {
                    if (i != 0) stringBuilder.Append('.');
                    stringBuilder.Append(field.name);
                    break;
                } else if (fieldValue is IList list) {
                    var index = list.IndexOf(childInstance);
                    if (index != -1) {
                        if (i != 0) stringBuilder.Append('.');
                        stringBuilder.Append(field.name).Append('.').Append(index);
                    }
                }
            }
        }

        var topLevelObject = instanceHierarchy.Last();
        if (parentFile is ScnFile scn) {
            scn.SetupGameObjects();
            ScnGameObject? gameObject;
            if (instance.RszClass.name == "via.GameObject") {
                gameObject = scn.IterAllGameObjects(true).FirstOrDefault(go => go.Instance == topLevelObject);
            } else {
                gameObject = scn.IterAllGameObjects(true).FirstOrDefault(go => go.Components.Contains(topLevelObject));
            }
            ScnFolderData? folder = null;
            if (instance.RszClass.name == "via.Folder") {
                folder = scn.IterAllFolders().FirstOrDefault(fi => fi.Instance == topLevelObject);
            } else if (gameObject != null) {
                folder = scn.IterAllFolders().FirstOrDefault(fi => fi.GameObjects.Contains(gameObject));
            } else {
                Logger.Error("Could not find object inside scn file. This should not happen.");
                return stringBuilder.ToString();
            }

            if (gameObject != null) {
                if (folder != null) {
                    stringBuilder.Insert(0, topLevelObject.RszClass.name);
                    stringBuilder.Insert(0, ':');
                    stringBuilder.Insert(0, gameObject.GetHierarchyPath());
                    stringBuilder.Insert(0, "//");
                    stringBuilder.Insert(0, folder.GetHierarchyPath());
                } else {
                    stringBuilder.Insert(0, topLevelObject.RszClass.name);
                    stringBuilder.Insert(0, ':');
                    stringBuilder.Insert(0, gameObject.GetHierarchyPath());
                }
            } else if (folder != null) {
                stringBuilder.Insert(0, "//");
                stringBuilder.Insert(0, folder.GetHierarchyPath());
            } else {
                // probably never happens
            }
        } else if (parentFile is PfbFile pfb) {
            pfb.SetupGameObjects();
            PfbGameObject? gameObject;
            if (instance.RszClass.name == "via.GameObject") {
                gameObject = pfb.IterAllGameObjects(true).FirstOrDefault(go => go.Instance == topLevelObject);
            } else {
                gameObject = pfb.IterAllGameObjects(true).FirstOrDefault(go => go.Components.Contains(topLevelObject));
            }
            if (gameObject == null) {
                Logger.Error("Could not find object inside pfb file. This should not happen.");
                return stringBuilder.ToString();
            }

            stringBuilder.Insert(0, topLevelObject.RszClass.name);
            stringBuilder.Insert(0, ':');
            stringBuilder.Insert(0, gameObject.GetHierarchyPath());
        }

        return stringBuilder.ToString();
    }

    private void InvokeSearchEfx(SearchContext context, EfxAttributeType type, string query)
    {
        foreach (var (path, stream) in context.Env.GetFilesWithExtension("efx", context.Token)) {
            try {
                if (context.Token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = new EfxFile(new FileHandler(stream, path));
                file.Read();

                if (efxFileMatchOnly && !string.IsNullOrEmpty(query) && true == Path.GetFileNameWithoutExtension(file.FileHandler.FilePath)?.Contains(query, StringComparison.OrdinalIgnoreCase)) {
                    AddMatch(context, path, path);
                    continue;
                }

                foreach (var entry in file.Entries.Cast<EFXEntryBase>().Concat(file.Actions)) {
                    var match1 = (type == EfxAttributeType.Unknown || entry.Contains(type));
                    var match2 = string.IsNullOrEmpty(query) || entry.name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

                    if (match1 && match2) {
                        var desc = entry is EFXEntry ? $"entry {entry.name}" : $"action {entry.name}";
                        AddMatch(context, desc, path);
                        if (efxFileMatchOnly) break; else continue;
                    }
                }
            } catch (Exception e) {
                context.Window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    private void InvokeSearchUvar(SearchContext context, Variable.TypeKind type, string query)
    {
        _ = Guid.TryParse(query, out var guid);
        foreach (var (path, stream) in context.Env.GetFilesWithExtension("uvar", context.Token)) {
            try {
                if (context.Token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = new UVarFile(new FileHandler(stream, path));
                file.Read();

                foreach (var uv in file.Variables) {
                    if (type != Variable.TypeKind.Unknown && type != uv.type) continue;
                    if (guid != Guid.Empty && guid == uv.guid) {
                        var desc = $"{uv.Name} = {uv.Value}";
                        AddMatch(context, desc, path);
                        continue;
                    }
                    if (string.IsNullOrEmpty(query) || uv.Name.Contains(query, StringComparison.InvariantCultureIgnoreCase)) {
                        var desc = $"{uv.Name} = {uv.Value}";
                        AddMatch(context, desc, path);
                    }
                }

                foreach (var embed in file.EmbeddedUVARs) {
                    foreach (var uv in embed.Variables) {
                        if (type != Variable.TypeKind.Unknown && type != uv.type) continue;
                        if (guid != Guid.Empty && guid == uv.guid) {
                            var desc = $"[{embed.Header.name}] {uv.Name} = {uv.Value}";
                            AddMatch(context, desc, path);
                            continue;
                        }
                        if (string.IsNullOrEmpty(query) || uv.Name.Contains(query, StringComparison.InvariantCultureIgnoreCase)) {
                            var desc = $"[{embed.Header.name}] {uv.Name} = {uv.Value}";
                            AddMatch(context, desc, path);
                        }
                    }
                }
            } catch (Exception e) {
                context.Window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    private void InvokeSearchMot(SearchContext context, string query)
    {
        // var formats = context.Env.TypeCache.GetResourceSubtypes(KnownFileFormats.Motion).Except([KnownFileFormats.MotionTree]);
        // var motExts = formats.SelectMany(fmt => context.Env.GetFileExtensionsForFormat(fmt));
        foreach (var (path, stream) in context.Env.GetFilesWithExtension("motlist", context.Token)) {
            try {
                if (context.Token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = new MotlistFile(new FileHandler(stream, path));
                file.Read();

                foreach (var motBase in file.MotFiles) {
                    if (motBase is MotFile mot) {
                        if (!string.IsNullOrEmpty(query) && !mot.Name.Contains(query, StringComparison.InvariantCultureIgnoreCase)) continue;

                        var desc = $"{mot.Name}";
                        AddMatch(context, desc, path);
                    }
                }
            } catch (NotSupportedException e) {
                context.Window.InvokeFromUIThread(() => Logger.Error("File read failed " + path + ": " + e.Message));
            } catch (Exception e) {
                context.Window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }

        foreach (var (path, stream) in context.Env.GetFilesWithExtension("mot", context.Token)) {
            try {
                if (context.Token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var mot = new MotFile(new FileHandler(stream, path));
                if (!mot.Read()) {
                    context.Window.InvokeFromUIThread(() => Logger.Error("File read failed " + path));
                    continue;
                }

                if (!string.IsNullOrEmpty(query) && !mot.Name.Contains(query, StringComparison.InvariantCultureIgnoreCase)) continue;

                var desc = $"{mot.Name}";
                AddMatch(context, desc, path);

            } catch (Exception e) {
                context.Window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    private void InvokeSearchGui(SearchContext context, string query)
    {
        var hasGuid = Guid.TryParse(query, out var guid);
        foreach (var (path, stream) in context.Env.GetFilesWithExtension("gui", context.Token)) {

            try {
                if (context.Token.IsCancellationRequested) return;
                Interlocked.Increment(ref searchedFiles);
                var file = new GuiFile(new FileHandler(stream, path));
                file.Read();

                foreach (var prop in file.AttributeOverrides) {
                    if (prop.TargetPath.Contains(query, StringComparison.InvariantCultureIgnoreCase)) {
                        AddMatch(context, $"Attribute Override {prop.TargetPath}", path);
                        continue;
                    }
                }

                foreach (var prop in file.Parameters) {
                    if (hasGuid && prop.guid == guid || prop.name.Contains(query, StringComparison.InvariantCultureIgnoreCase)) {
                        AddMatch(context, $"Parameter Declaration \"{prop.name}\"", path);
                        continue;
                    }
                }

                foreach (var prop in file.ParameterReferences) {
                    if (hasGuid && prop.guid == guid || prop.path.Contains(query, StringComparison.InvariantCultureIgnoreCase)) {
                        AddMatch(context, $"Parameter Reference \"{prop.path}\"", path);
                        continue;
                    }
                }

                foreach (var prop in file.ParameterOverrides) {
                    if (hasGuid && prop.guid == guid) {
                        AddMatch(context, $"Parameter Override \"{prop.path}\"", path);
                        continue;
                    }
                }

                foreach (var container in file.Containers) {
                    if (hasGuid && container.Info.guid == guid || container.Info.Name.Contains(query, StringComparison.InvariantCultureIgnoreCase)) {
                        AddMatch(context, $"Container {container.Info.Name}", path);
                        continue;
                    }

                    foreach (var clip in container.Clips) {
                        if (hasGuid && (clip.guid == guid || clip.clip?.Guid == guid) || clip.name.Contains(query, StringComparison.InvariantCultureIgnoreCase)) {
                            AddMatch(context, $"Clip {container.Info.Name}/{clip.name}", path);
                            break;
                        }
                    }
                }

                foreach (var container in file.Containers) {
                    foreach (var elem in container.Elements) {
                        if (hasGuid && elem.ID == guid || elem.Name.Contains(query, StringComparison.InvariantCultureIgnoreCase)) {
                            AddMatch(context, $"Element {container.Info.Name}/{elem.Name}", path);
                            continue;
                        }
                        if (hasGuid && elem.guid3 == guid) {
                            AddMatch(context, $"Element GUID3 - {container.Info.Name}/{elem.Name}", path);
                            continue;
                        }
                    }
                }
            } catch (Exception e) {
                context.Window.InvokeFromUIThread(() => Logger.Error(e, "File read failed " + path));
            }
        }
    }

    private static string LimitLength(string str, int maxlen) => str.Length <= maxlen - 2 ? str : str[0..^(maxlen - 3)] + "...";

    private SearchContext CreateContext(Workspace env, Workspace activeEnv)
    {
        cancellationTokenSource ??= new();
        return searchContext = new SearchContext(env, env == activeEnv, (EditorWindow)data.ParentWindow, cancellationTokenSource.Token);
    }

    private IEnumerable<Workspace> GetWorkspaces(Workspace current)
    {
        current.PakReader.EnableConsoleLogging = false;
        yield return current;
        if (!searchAllGames) yield break;

        foreach (var other in AppConfig.Instance.ConfiguredGames) {
            if (other == current.Config.Game) continue;

            var env = WorkspaceManager.Instance.GetWorkspace(other);
            try {
                env.PakReader.EnableConsoleLogging = false;
                Logger.Info("Starting search for game " + env.Config.Game);
                yield return env;
            } finally {
                WorkspaceManager.Instance.Release(env);
            }
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}