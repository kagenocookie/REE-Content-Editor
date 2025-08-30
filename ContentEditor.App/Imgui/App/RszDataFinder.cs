using System.Collections;
using System.Collections.Concurrent;
using System.Text;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ImGuiNET;
using ReeLib;
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
    private bool searchClassOnly = false;
    private bool rszSearchByClass = true;
    private RszFieldType rszFieldType = RszFieldType.String;
    private string rszFieldTypeFilter = "";

    private bool searchAllGames;

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

    private ConcurrentBag<(GameIdentifier game, string? label, string file)> matches = new();
    private GameIdentifier currentGame;

    private WindowData data = null!;
    protected UIContext context = null!;

    private SearchContext? searchContext;

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

        currentGame = workspace.Env.Config.Game;
        var searching = SearchInProgress;
        if (searching) ImGui.BeginDisabled();

        ImGui.Checkbox("Search all configured games", ref searchAllGames);

        ImguiHelpers.Tabs(FindTypes, ref findType);
        switch (findType) {
            case 0:
                ImGui.Checkbox("Search by specific class", ref rszSearchByClass);
                if (rszSearchByClass) {
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
        ImguiHelpers.FilterableCSharpEnumCombo("Field type", ref rszFieldType, ref rszFieldTypeFilter!);
        var value = RszValueInput(rszFieldType);
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

        if (type is ReeLib.RszFieldType.Object or ReeLib.RszFieldType.Struct or ReeLib.RszFieldType.UserData) {
            ImGui.TextColored(Colors.Warning, "Not a filterable field");
            return null;
        }
        object? value = null;
        if (cancellationTokenSource == null) {
            Type csType;
            if (!searchClassOnly) {
                if (type is RszFieldType.String or RszFieldType.RuntimeType or RszFieldType.Resource) {
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
                        ImGui.InputText("Value", ref valueString, 100);
                        value = valueString;
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
            var (user, pfb, scn) = (searchUserFiles, searchPfb, searchScn);
            var token = cancellationTokenSource.Token;
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
            var (user, pfb, scn) = (searchUserFiles, searchPfb, searchScn);
            var token = cancellationTokenSource.Token;
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
            var (user, pfb, scn) = (searchUserFiles, searchPfb, searchScn);
            var token = cancellationTokenSource.Token;
            Task.Run(() => {
                foreach (var ee in GetWorkspaces(env)) {
                    InvokeSearchUvar(CreateContext(ee, env), uvarKind, uvarSearch);
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
        Func<object?, object?, bool> equalityComparer = cls.fields[fieldIndex].type is RszFieldType.String or RszFieldType.RuntimeType or RszFieldType.Resource
            ? (object? a, object? b) => (a as string)?.Equals(b as string, StringComparison.InvariantCultureIgnoreCase) == true
            : (object? a, object? b) => a?.Equals(b) == true;

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
                            return;
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

    private void InvokeRszSearchField(SearchContext context, string ext, Func<RszFileOption, FileHandler, BaseRszFile> fileFact, RszFieldType fieldType, bool array, object? value)
    {
        Func<object?, object?, bool> equalityComparer = fieldType is RszFieldType.String or RszFieldType.RuntimeType or RszFieldType.Resource
            ? (object? a, object? b) => (a as string)?.Equals(b as string, StringComparison.InvariantCultureIgnoreCase) == true
            : (object? a, object? b) => a?.Equals(b) == true;

        foreach (var (path, stream) in context.Env.GetFilesWithExtension(ext, context.Token)) {
            try {
                if (context.Token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = fileFact.Invoke(context.Env.RszFileOption, new FileHandler(stream, path));
                file.Read();

                var rsz = file.GetRSZ()!;
                foreach (var inst in rsz.InstanceList) {
                    if (inst.RSZUserData != null) continue;
                    foreach (var field in inst.Fields) {
                        if (field.type != fieldType) continue;

                        if (value == null) {
                            AddMatch(context, $"{FindPathToRszObject(rsz, inst, file)} {field.name} = {inst.GetFieldValue(field.name)}", path);
                            return;
                        }

                        var fieldValue = inst.GetFieldValue(field.name);
                        if (field.array) {
                            var values = (IList<object>)fieldValue!;
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
        foreach (var (path, stream) in context.Env.GetFilesWithExtension("uvar", context.Token)) {
            try {
                if (context.Token.IsCancellationRequested) return;

                Interlocked.Increment(ref searchedFiles);
                var file = new UVarFile(new FileHandler(stream, path));
                file.Read();

                foreach (var uv in file.Variables) {
                    if (type != Variable.TypeKind.Unknown && type != uv.type) continue;
                    if (!string.IsNullOrEmpty(query) && !uv.Name.Contains(query, StringComparison.InvariantCultureIgnoreCase)) continue;

                    var desc = $"{uv.Name} = {uv.Value}";
                    AddMatch(context, desc, path);
                }

                foreach (var embed in file.EmbeddedUVARs) {
                    foreach (var uv in embed.Variables) {
                        if (type != Variable.TypeKind.Unknown && type != uv.type) continue;
                        if (!string.IsNullOrEmpty(query) && !uv.Name.Contains(query, StringComparison.InvariantCultureIgnoreCase)) continue;

                        var desc = $"[{embed.Header.name}] {uv.Name} = {uv.Value}";
                        AddMatch(context, desc, path);
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