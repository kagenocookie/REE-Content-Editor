using ContentEditor.BackgroundTasks;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mdf;
using ReeLib.via;
using System.Numerics;
using System.Reflection;
using System.Text.Json;

namespace ContentEditor.App.ImguiHandling.Mdf2;

public class MdfEditor : FileEditor, IWorkspaceContainer, IObjectUIHandler
{
    public override string HandlerName => "MDF Editor";

    public RszInstance? Instance { get; private set; }
    public string Filename => Handle.Filepath;
    public MdfFile File => Handle.GetFile<MdfFile>();

    public ContentWorkspace Workspace { get; }

    protected override bool IsRevertable => context.Changed;

    public MdfEditor(ContentWorkspace env, FileHandle file) : base (file)
    {
        Workspace = env;
    }

    private MmtrTemplateDB? mmtrTemplateDB;
    internal MmtrTemplateDB? MaterialTemplateDB => mmtrTemplateDB;
    private bool _requestedMmtrDb;

    public MaterialData ReplaceMaterialParams(string mmtr, MaterialData material)
    {
        if (mmtrTemplateDB?.Templates.TryGetValue(mmtr, out var template) == true) {
            var hasPreviousData = material.Parameters.Count > 0 || material.Textures.Count > 0;
            foreach (var existingParam in material.Parameters.ToList()) {
                var matchingParam = template.Parameters.FirstOrDefault(pp => pp.Name == existingParam.paramName);
                if (matchingParam == null) {
                    material.Parameters.Remove(existingParam);
                } else if (matchingParam.Components != existingParam.componentCount) {
                    existingParam.componentCount = matchingParam.Components;
                }
            }

            foreach (var param in template.Parameters) {
                if (hasPreviousData && material.Parameters.Any(pp => pp.paramName == param.Name)) continue;
                material.Parameters.Add(new ParamHeader() {
                    paramName = param.Name,
                    componentCount = param.Components
                });
            }

            foreach (var existingTex in material.Textures.ToList()) {
                if (!template.TextureNames.Contains(existingTex.texType)) {
                    material.Textures.Remove(existingTex);
                }
            }

            foreach (var param in template.TextureNames) {
                if (hasPreviousData && material.Textures.Any(pp => pp.texType == param)) continue;

                material.Textures.Add(new TexHeader() {
                    texType = param,
                    texPath = template.TextureDefaults.GetValueOrDefault(param)
                });
            }

            if (hasPreviousData) {
                material.Parameters.Sort((a, b) =>
                    template.Parameters.FindIndex(p => p.Name == a.paramName)
                    .CompareTo(template.Parameters.FindIndex(p => p.Name == b.paramName)));

                material.Textures.Sort((a, b) =>
                    template.TextureNames.IndexOf(a.texType)
                    .CompareTo(template.TextureNames.IndexOf(b.texType)));
            }
        }
        return material;
    }

    public MaterialData CreateMaterial(string mmtr)
    {
        var mat = new MaterialData();
        ReplaceMaterialParams(mmtr, mat);
        return mat;
    }

    protected override void DrawFileContents()
    {
        if (mmtrTemplateDB == null) {
            var cachePath = MaterialParamCacheTask.GetCachePath(Workspace.Game);
            if (!_requestedMmtrDb) {
                _requestedMmtrDb = true;
                if (!System.IO.File.Exists(cachePath)) {
                    // OK
                } else if (!cachePath.TryDeserializeJsonFile<MmtrTemplateDB>(out var db, out var error)) {
                    Logger.Warn("Could not load previous mmtr parameter cache from path " + cachePath + ":\n" + error);
                } else if (db.GameDataHash == Workspace.VersionHash) {
                    mmtrTemplateDB = db;
                }

                if (mmtrTemplateDB == null) {
                    MainLoop.Instance.BackgroundTasks.Queue(new MaterialParamCacheTask(Workspace.Env));
                }
            } else {
                if (!MainLoop.Instance.BackgroundTasks.HasPendingTask<MaterialParamCacheTask>() &&
                    System.IO.File.Exists(cachePath) &&
                    cachePath.TryDeserializeJsonFile<MmtrTemplateDB>(out var db, out var error)) {
                    mmtrTemplateDB = db;
                }
            }
        }

        var isEmpty = context.children.Count == 0;
        if (context.children.Count == 0) {
            context.AddChild("Data", File, new MdfFileImguiHandler());
        }
        context.ShowChildrenUI();
    }

    void IObjectUIHandler.OnIMGUI(UIContext container)
    {
        this.OnIMGUI();
    }
}

[ObjectImguiHandler(typeof(MdfFile), Stateless = false)]
public class MdfFileImguiHandler : IObjectUIHandler
{
    private int selectedIDX = 0;
    private int activeTabIDX = 0;
    private string newMaterialName = string.Empty;
    private bool isNewMaterialMenu = false;
    private string materialSearch = string.Empty;
    public void OnIMGUI(UIContext context)
    {
        var file = context.Get<MdfFile>();

        ImGui.BeginChild("##MaterialList", new Vector2(300f, ImGui.GetContentRegionAvail().Y));
        ShowMaterialList(context, file);
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##MaterialData");
        ShowSelectedMaterialData(context, file);
        ImGui.EndChild();
    }

    private void ShowMaterialList(UIContext context, MdfFile file)
    {
        var list = file.Materials;

        ImGui.TextColored(Colors.Faded, "Material List");
        ImGui.Separator();
        ImguiHelpers.ToggleButton($"{AppIcons.SI_FileType_MDF}", ref isNewMaterialMenu, Colors.IconActive);
        ImguiHelpers.Tooltip("Add new Material");
        ImGui.SameLine();
        using (var __ = ImguiHelpers.Disabled(!VirtualClipboard.TryGetFromClipboard<MaterialData>(out _))) {
            if (ImGui.Button($"{AppIcons.SI_Paste}")) {
                if (VirtualClipboard.TryGetFromClipboard<MaterialData>(out var pasted)) {
                    var clone = pasted.Clone();
                    clone.Header.matName = clone.Header.matName.GetUniqueName(str => list.Any(l => l.Header.matName == str));
                    UndoRedo.RecordListAdd(context, list, clone);
                    selectedIDX = list.Count - 1;
                    activeTabIDX = 0;
                    context.children.Clear();
                }
            }
            ImguiHelpers.Tooltip("Paste Material from clipboard");
        }
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_GenericImport}")) {
            PlatformUtils.ShowFileDialog(paths => { var path = paths[0];
                ImportMatParamsFromEMVJson(path, file, context);
            }, fileExtension: new[] { new FileFilter("JSON", new[] { "json" }) });
        }
        ImguiHelpers.Tooltip("Import Material parameters from EMV JSON");
        ImGui.SameLine();
        ImGui.SetNextItemAllowOverlap();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##MaterialSearch", $"{AppIcons.SI_GenericMagnifyingGlass} Search", ref materialSearch, 64);
        if (!string.IsNullOrEmpty(materialSearch)) {
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMax().X - ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.X, ImGui.GetItemRectMin().Y));
            ImGui.SetNextItemAllowOverlap();
            if (ImGui.Button($"{AppIcons.SI_GenericClose}")) {
                materialSearch = string.Empty;
            }
        }
        if (isNewMaterialMenu) {
            ImGui.Separator();
            using (var _ = ImguiHelpers.Disabled(string.IsNullOrEmpty(newMaterialName))) {
                bool isInvalidName = list.Any(m => m.Header.matName.Equals(newMaterialName, StringComparison.OrdinalIgnoreCase));
                ImGui.PushStyleColor(ImGuiCol.Text, isInvalidName ? Colors.IconTertiary : Colors.IconActive);
                if (!isInvalidName) {
                    if (ImGui.Button($"{AppIcons.SI_GenericAdd}")) {
                        var mat = new MaterialData(new MaterialHeader { matName = newMaterialName });
                        UndoRedo.RecordListAdd(context, list, mat);
                        selectedIDX = list.Count - 1;
                        context.children.Clear();
                        newMaterialName = "";
                    }
                    ImGui.PopStyleColor();
                    ImguiHelpers.Tooltip("Add");
                } else {
                    ImGui.Button($"{AppIcons.SI_GenericClose}");
                    ImguiHelpers.Tooltip("A Material with the same name already exists");
                    ImGui.PopStyleColor();
                }
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputText("##NewMaterialNameField", ref newMaterialName, 64);
        }

        ImGui.Separator();
        for (int i = 0; i < list.Count; i++) {
            var mat = list[i];
            if (!string.IsNullOrEmpty(materialSearch) && !mat.Header.matName.Contains(materialSearch, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            bool selected = i == selectedIDX;
            ImGui.PushStyleColor(ImGuiCol.Text, selected ? Colors.TextActive : ImguiHelpers.GetColor(ImGuiCol.Text));
            if (ImGui.Selectable(mat.Header.matName, selected)) {
                selectedIDX = i;
                activeTabIDX = 0;
                context.children.Clear();
            }
            ImGui.PopStyleColor();

            if (ImGui.BeginPopupContextItem($"##MaterialID{i}")) {
                ShowMaterialContextMenu(context,file.Materials, mat, newIndex => { selectedIDX = newIndex;  context.children.Clear(); });
                ImGui.EndPopup();
            }
        }
        ImGui.Separator();
    }

    private void ShowSelectedMaterialData(UIContext context, MdfFile file)
    {
        var mat = file.Materials[selectedIDX];

        if (ImGui.BeginTabBar("##MaterialDataTabs")) {
            ShowMaterialDataTab(context, mat, "General", 0, (c) => mat.Header);
            ShowMaterialDataTab(context, mat, "Textures", 1, (c) => mat.Textures);
            ShowMaterialDataTab(context, mat, "Parameters", 2, (c) => mat.Parameters);
            ShowMaterialDataTab(context, mat, "GPU Buffers", 3, (c) => mat.GpuBuffers);
            ImGui.EndTabBar();
        }
    }
    private void ShowMaterialDataTab<T>(UIContext context, MaterialData mat, string label, int index, Func<UIContext, T> getter)
    {
        bool tabLabel = ImGui.BeginTabItem(label);
        if (tabLabel) {
            if (activeTabIDX != index) {
                activeTabIDX = index;
                context.children.Clear();
            }
            if (context.children.Count == 0 || context.children[0].label != label) {
                context.AddChild(label, mat, getter: (c) => (object)getter(c)!).AddDefaultHandler<T>();
            }
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            context.ShowChildrenUI();
            ImGui.EndTabItem();
        }
    }

    private static void ShowMaterialContextMenu(UIContext context, List<MaterialData> list, MaterialData mat, Action<int>? onSelectIndexChanged = null)
    {
        if (ImGui.MenuItem("Duplicate")) {
            var clone = mat.Clone();
            clone.Header.matName = $"{mat.Header.matName}_copy".GetUniqueName(str => list.Any(l => l.Header.matName == str));
            UndoRedo.RecordListAdd(context, list, clone);
            onSelectIndexChanged?.Invoke(list.Count - 1);
        }
        if (ImGui.MenuItem("Copy")) {
            VirtualClipboard.CopyToClipboard(mat.Clone());
        }
        using (var i = ImguiHelpers.Disabled(!VirtualClipboard.TryGetFromClipboard<MaterialData>(out _))) {
            if (ImGui.MenuItem("Paste")) {
                if (VirtualClipboard.TryGetFromClipboard<MaterialData>(out var pasted)) {
                    var clone = pasted.Clone();
                    clone.Header.matName = clone.Header.matName.GetUniqueName(str => list.Any(l => l.Header.matName == str));
                    UndoRedo.RecordListAdd(context, list, clone);
                    onSelectIndexChanged?.Invoke(list.Count - 1);
                }
            }
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Delete")) {
            int index = list.IndexOf(mat);
            UndoRedo.RecordListRemove(context, list, mat);
            int newIndex = Math.Clamp(index - 1, 0, list.Count - 1);
            onSelectIndexChanged?.Invoke(newIndex);
        }
    }
    private class EMVMaterialJson
    {
        public Dictionary<string, Dictionary<string, JsonElement>> m { get; set; } = new();
    }

    private static Vector4 ParseEMVMatVec4(string vecString)
    {
        var parts = vecString.Replace("vec:", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new Vector4( float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3]));
    }
    private static void ImportMatParamsFromEMVJson(string path, MdfFile file, UIContext context)
    {
        var jsonData = File.ReadAllText(path);
        var root = JsonSerializer.Deserialize<EMVMaterialJson>(jsonData);
        if (root == null || root.m == null) return;

        foreach (var mat in file.Materials) {
            if (!root.m.TryGetValue(mat.Header.matName, out var matParamDict)) continue;

            foreach (var param in mat.Parameters) {
                if (!matParamDict.TryGetValue(param.paramName, out var jsonValue)) continue;

                if (jsonValue.ValueKind == JsonValueKind.Number) {
                    param.parameter = new Vector4(jsonValue.GetSingle(), 0, 0, 0);
                } else if (jsonValue.ValueKind == JsonValueKind.String) {
                    var str = jsonValue.GetString()!;
                    if (str.StartsWith("vec:")) {
                        param.parameter = ParseEMVMatVec4(str);
                    }
                }
            }
        }
        context.Changed = true;
        context.children.Clear();
    }
}

[ObjectImguiHandler(typeof(MaterialHeader), Stateless = true)]
public class MatHeaderImguiHandler : IObjectUIHandler
{
    private static MemberInfo[] DisplayedFields = [
        typeof(MaterialHeader).GetField(nameof(MaterialHeader.matName))!,
        typeof(MaterialHeader).GetField(nameof(MaterialHeader.shaderType))!,
        typeof(MaterialHeader).GetProperty(nameof(MaterialHeader.Flags1))!,
        typeof(MaterialHeader).GetProperty(nameof(MaterialHeader.Flags2))!,
        typeof(MaterialHeader).GetProperty(nameof(MaterialHeader.Phong))!,
        typeof(MaterialHeader).GetProperty(nameof(MaterialHeader.Tesselation))!,
        typeof(MaterialHeader).GetField(nameof(MaterialHeader.ukn1))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var tex = context.Get<MaterialHeader>();
            var ws = context.GetWorkspace();
            WindowHandlerFactory.SetupObjectUIContext(context, typeof(MaterialHeader), members: DisplayedFields);
            context.GetChildByValue<MaterialFlags1>()?.uiHandler = new CsharpFlagsEnumFieldHandler<MaterialFlags1, int>() { HideNumberInput = true };
            context.GetChildByValue<MaterialFlags2>()?.uiHandler = new CsharpFlagsEnumFieldHandler<MaterialFlags2, int>() { HideNumberInput = true };
            context.AddChildContextSetter<MaterialHeader, string>(
                "MMTR path",
                tex,
                new ResourcePathPicker(ws, KnownFileFormats.MasterMaterial),
                (p) => p!.mmtrPath,
                (c, p, v) => {
                    p.mmtrPath = v;
                    var mat = c.FindParentContextByValue<MaterialHeader>()?.target as MaterialData;
                    var root = c.FindHandlerInParents<MdfEditor>();
                    if (mat != null && root != null && !string.IsNullOrEmpty(v)) {
                        root.ReplaceMaterialParams(v, mat);
                    }
                });
        }
        ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        context.ShowChildrenUI();
    }
}

[ObjectImguiHandler(typeof(TexHeader), Stateless = true)]
public class TexHeaderImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var tex = context.Get<TexHeader>();
            context.AddChild<TexHeader, string>(tex.texType, tex, new ResourcePathPicker(), (p) => p!.texPath, (p, v) => p.texPath = v);
        }
        context.children[0].ShowUI();
    }
}

[ObjectImguiHandler(typeof(GpuBufferEntry), Stateless = true)]
public class MeshGpbfImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var tex = context.Get<GpuBufferEntry>();
            context.AddChild<GpuBufferEntry, string>("Name", tex, new StringFieldHandler(), (p) => p.name, (p, v) => p.name = v ?? string.Empty);
            context.AddChild<GpuBufferEntry, string>("Path", tex, new ResourcePathPicker(context.GetWorkspace(), KnownFileFormats.ByteBuffer), (p) => p!.path, (p, v) => p.path = v ?? string.Empty);
        }

        var w = ImGui.CalcItemWidth();
        var w1 = Math.Min(200, w * 0.3f);
        ImGui.SetNextItemWidth(w1);
        context.children[0].ShowUI();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(w - w1 - ImGui.GetStyle().FramePadding.X);
        context.children[1].ShowUI();
    }
}

[ObjectImguiHandler(typeof(List<MaterialData>), Stateless = true)]
public class MdfMaterialListImguiHandler : DictionaryListImguiHandler<string, MaterialData, List<MaterialData>>
{
    public MdfMaterialListImguiHandler()
    {
        Filterable = true;
    }

    protected override bool Filter(UIContext context, string filter) => context.Get<MaterialData>().Header.matName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    protected override string GetKey(MaterialData item) => item.Header.matName;

    protected override MaterialData CreateItem(UIContext context, string key)
    {
        var baseMat = context.Get<List<MaterialData>>().FirstOrDefault();
        var mat = baseMat?.Clone() ?? new MaterialData(new MaterialHeader());
        mat.Header.matName = key;
        return mat;
    }

    protected override void InitChildContext(UIContext itemContext)
    {
        if (itemContext.GetRaw() != null) {
            itemContext.uiHandler = new MdfMaterialLazyPlainObjectHandler();
        }
    }
}

public class MdfMaterialLazyPlainObjectHandler : LazyPlainObjectHandler
{
    public MdfMaterialLazyPlainObjectHandler() : base(typeof(MaterialData))
    {
    }

    protected override bool DoTreeNode(UIContext context, object instance)
    {
        var tree = base.DoTreeNode(context, instance);
        var mat = (MaterialData)instance;
        if (ImGui.BeginPopupContextItem(context.label)) {
            if (ImGui.Button("Duplicate")) {
                var list = context.parent!.Get<List<MaterialData>>();
                var clone = mat.Clone();
                clone.Header.matName = $"{mat.Header.matName}_copy".GetUniqueName(str => list.Any(l => l.Header.matName == str));
                UndoRedo.RecordListAdd(context.parent, list, clone);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Button("Delete")) {
                var list = context.parent!.Get<List<MaterialData>>();
                UndoRedo.RecordListRemove(context.parent, list, mat, childIndexOffset: 1);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (!tree && context.label != mat.Header.matName) {
            context.label = mat.Header.matName;
        }
        return tree;
    }
}

[ObjectImguiHandler(typeof(List<TexHeader>), Stateless = true)]
public class TexHeaderListImguiHandler : DictionaryListImguiHandler<string, TexHeader, List<TexHeader>>
{
    public TexHeaderListImguiHandler()
    {
        AllowAdditions = false;
        Filterable = true;
    }

    protected override bool Filter(UIContext context, string filter) => context.Get<TexHeader>().texType.Contains(filter, StringComparison.OrdinalIgnoreCase);
    protected override string GetKey(TexHeader item) => item.texType;

    protected override TexHeader CreateItem(UIContext context, string key)
        => new TexHeader();
}


[ObjectImguiHandler(typeof(List<ParamHeader>), Stateless = true)]
public class ParamHeaderListImguiHandler : DictionaryListImguiHandler<string, ParamHeader, List<ParamHeader>>
{
    public ParamHeaderListImguiHandler()
    {
        AllowAdditions = false;
        Filterable = true;
    }

    protected override bool Filter(UIContext context, string filter) => context.Get<ParamHeader>().paramName.Contains(filter, StringComparison.OrdinalIgnoreCase);

    protected override string GetKey(ParamHeader item) => item.paramName;

    protected override ParamHeader CreateItem(UIContext context, string key)
        => new ParamHeader();
}

[ObjectImguiHandler(typeof(ParamHeader), Stateless = true)]
public class ParamHeaderImguiHandler : IObjectUIHandler
{
    public void OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var param = context.Get<ParamHeader>();
            switch (param.componentCount) {
                case 1:
                    context.AddChild<ParamHeader, float>(param.paramName, param, new NumericFieldHandler<float>(ImGuiDataType.Float), (p) => p!.parameter.X, (p, v) => p.parameter = new Vector4(v, 0, 0, 0));
                    break;
                case 2:
                    context.AddChild<ParamHeader, Vector2>(param.paramName, param, new Vector2FieldHandler(), (p) => p!.parameter.ToVec2(), (p, v) => p.parameter = v.ToVec4());
                    break;
                case 3:
                    context.AddChild<ParamHeader, Vector3>(param.paramName, param, new Vector3FieldHandler(), (p) => p!.parameter.ToVec3(), (p, v) => p.parameter = v.ToVec4());
                    break;
                default:
                case 4:
                    if (param.paramName.Contains("color", StringComparison.OrdinalIgnoreCase)) {
                        context.AddChild<ParamHeader, Color>(param.paramName, param, new ColorFieldHandler(), (p) => Color.FromVector4(p!.parameter), (p, v) => p.parameter = v.ToVector4());
                    } else {
                        context.AddChild<ParamHeader, Vector4>(param.paramName, param, new Vector4FieldHandler(), (p) => p!.parameter, (p, v) => p.parameter = v);
                    }
                    break;
            }
        }
        context.children[0].ShowUI();
    }
}
