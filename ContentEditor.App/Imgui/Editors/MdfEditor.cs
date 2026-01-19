using System.Numerics;
using System.Reflection;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using ReeLib.Common;
using ReeLib.Mdf;
using ReeLib.via;

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

    protected override void DrawFileContents()
    {
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

        ImGui.BeginChild("##MaterialList", new Vector2(250f, ImGui.GetContentRegionAvail().Y));
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
        ImguiHelpers.ToggleButton($"{AppIcons.SI_FileType_MDF}", ref isNewMaterialMenu, Colors.IconActive);// SILVER: Icon pending
        ImguiHelpers.Tooltip("Add new Material");
        ImGui.SameLine();
        ImGui.Button($"{AppIcons.SI_FileType_MMTR}"); // SILVER: Icon pending vol.2, paste mat from clipboard button should be disabled when clipboard is empty
        ImGui.SameLine();
        ImGui.SetNextItemAllowOverlap();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##MaterialSearch", $"{AppIcons.SI_GenericMagnifyingGlass} Search", ref materialSearch, 64); // SILVER: we don't have the space here for case matching
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
            ShowMaterialDataTab(context, mat, "General", 0, () => ShowMaterialDataTabContent(context, "General", mat.Header));
            ShowMaterialDataTab(context, mat, "Textures", 1, () => ShowMaterialDataTabContent(context, "Textures", mat.Textures));
            ShowMaterialDataTab(context, mat, "Parameters", 2, () => ShowMaterialDataTabContent(context, "Parameters", mat.Parameters));
            ShowMaterialDataTab(context, mat, "GPU Buffers", 3, () =>  ShowMaterialDataTabContent(context, "GPU Buffers", mat.GpuBuffers));
            ImGui.EndTabBar();
        }
    }
    private void ShowMaterialDataTab(UIContext context, MaterialData mat, string label, int index, Action drawTab)
    {
        bool tabLabel = ImGui.BeginTabItem(label);
        if (tabLabel) {
            if (activeTabIDX != index) {
                activeTabIDX = index;
                context.children.Clear();
            }
            drawTab();
            ImGui.EndTabItem();
        }
    }

    private static void ShowMaterialDataTabContent(UIContext context, string label, object data)
    {
        UIContext child;

        if (context.children.Count == 0 || context.children[0].label != label) {
            context.children.Clear();
            child = context.AddChild(label, data);
            AssignHandler(child, data);
        } else {
            child = context.children[0];
        }
        ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        child.ShowUI();
    }
    private static void AssignHandler(UIContext context, object data)
    {
        switch (data) {
            case MaterialHeader:
                context.uiHandler = new MatHeaderImguiHandler();
                break;
            case List<TexHeader>:
                context.uiHandler = new TexHeaderListImguiHandler();
                break;
            case List<ParamHeader>:
                context.uiHandler = new ParamHeaderListImguiHandler();
                break;
            case List<GpuBufferEntry>: context.AddDefaultHandler<List<GpuBufferEntry>>();
                break;
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

        if (ImGui.MenuItem("Delete")) {
            int index = list.IndexOf(mat);
            UndoRedo.RecordListRemove(context, list, mat);
            int newIndex = Math.Clamp(index - 1, 0, list.Count - 1);
            onSelectIndexChanged?.Invoke(newIndex);
        }
        // TODO SILVER: Allow copypasting mats from different MDFs
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
            context.AddChild<MaterialHeader, string>("MMTR path", tex, new ResourcePathPicker(ws, KnownFileFormats.MasterMaterial), (p) => p!.mmtrPath, (p, v) => p.mmtrPath = v);
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
        => new MaterialData(new MaterialHeader() { matName = key });

    protected override void InitChildContext(UIContext itemContext)
    {
        if (itemContext.GetRaw() != null) {
            itemContext.uiHandler = new MdfMaterialLazyPlainObjectHandler(typeof(MaterialData));
        }
    }
}

public class MdfMaterialLazyPlainObjectHandler : LazyPlainObjectHandler
{
    public MdfMaterialLazyPlainObjectHandler(Type type) : base(type)
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
