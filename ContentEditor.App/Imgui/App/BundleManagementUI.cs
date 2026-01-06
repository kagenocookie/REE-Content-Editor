using ContentEditor.App;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ContentEditor.Core;
using ReeLib;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace ContentEditor.Core;

public class BundleManagementUI : IWindowHandler
{
    private BundleManager bundleManager;
    private readonly string? preselectBundle;
    private readonly Action<string>? openFileCallback;
    private readonly Action<string, JsonNode>? showDiff;
    public delegate void CreateBundleFromLooseFileFolderDelegate(string folder);
    public delegate void CreateBundleFromPakDelegate(string pak);

    public BundleManagementUI(BundleManager workspace, string? preselectBundle, Action<string>? openFileCallback, Action<string, JsonNode>? showDiff,
        CreateBundleFromLooseFileFolderDelegate? createFromLooseFileFolder, CreateBundleFromPakDelegate? createFromPak)
    {
        this.bundleManager = workspace;
        this.preselectBundle = preselectBundle;
        this.openFileCallback = openFileCallback;
        this.showDiff = showDiff;
        this.createFromLooseFileFolder = createFromLooseFileFolder;
        this.createFromPak = createFromPak;
    }

    public string HandlerName => "Bundle Manager";
    public int FixedID => -10001;

    public bool HasUnsavedChanges => false;
    private string newBundleName = "";
    private bool isNewBundleMenu = false;

    private WindowData data = null!;
    protected UIContext context = null!;
    private readonly CreateBundleFromLooseFileFolderDelegate? createFromLooseFileFolder;
    private readonly CreateBundleFromPakDelegate? createFromPak;

    public void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
        data.SetPersistentData("selectedBundle", preselectBundle);
    }

    public void OnWindow() => this.ShowDefaultWindow(context);
    public void OnIMGUI()
    {
        if (!bundleManager.IsLoaded) {
            bundleManager.LoadDataBundles();
        }
        ShowBundleToolbar();
        ImGui.SeparatorText("Bundles");

        var selectedName = data.GetPersistentData<string>("selectedBundle");
        if (bundleManager.AllBundles.Count == 0) {
            ImGui.TextColored(Colors.Info, "No Bundles found!");
        } else {
            var filter = data.GetPersistentData<string>("bundleFilter") ?? "";
            var selectedBundle = bundleManager.GetBundle(selectedName, null);
            var names = bundleManager.AllBundles.Select(b => b.Name).ToArray();
            var bundlespan = CollectionsMarshal.AsSpan(bundleManager.AllBundles);
            if (ImguiHelpers.FilterableCombo("Bundle", names, bundlespan, ref selectedBundle, ref filter)) {
                selectedName = selectedBundle?.Name;
                data.SetPersistentData("selectedBundle", selectedName);
            }
            data.SetPersistentData("bundleFilter", filter);
        }

        if (selectedName == null) return;

        var bundle = bundleManager.GetBundle(selectedName, null);
        if (bundle == null) {
            return;
        }
        ImGui.SameLine();
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FolderOpenFileExplorer, new[] { Colors.IconSecondary, Colors.IconPrimary})){
            FileSystemUtils.ShowFileInExplorer(bundleManager.ResolveBundleLocalPath(bundle, ""));
        }
        ImguiHelpers.Tooltip("Open current Bundle folder in Explorer");
        var str = bundle.Author ?? "";
        if (ImGui.InputText("Author", ref str, 100)) {
            bundle.Author = str;
        }
        str = bundle.Description ?? "";
        var w = ImGui.CalcItemWidth();
        if (ImGui.InputTextMultiline("Description", ref str, 1024, new Vector2(w, 120))) {
            bundle.Description = str;
        }
        ImGui.BeginDisabled();
        string createDate = $"Created at: {bundle.CreatedAt}";
        string updateDate = $"Updated at: {bundle.UpdatedAt}";
        ImGui.InputText("##CerationDate", ref createDate, 100);
        ImGui.InputText("##UpdateDate", ref updateDate, 100);
        ImGui.EndDisabled();

        var legacyEntityTypes = bundle.LegacyData?
            .Where(ld => ld.TryGetPropertyValue("type", out _))
            .Select(ld => ld["type"]!.GetValue<string>())
            .Distinct();

        if (legacyEntityTypes?.Any() == true) {
            if (ImGui.TreeNode("Legacy entities")) {
                var types = allOption.Concat(legacyEntityTypes).ToArray();
                if (ImGui.Combo("Type", ref selectedLegacyEntityType, types, types.Length)) {

                }
                var filter = selectedLegacyEntityType > 0 && selectedLegacyEntityType < types.Length ? types[selectedLegacyEntityType] : null;
                foreach (var e in bundle.LegacyData!) {
                    if (!e.TryGetPropertyValue("type", out var type)) {
                        continue;
                    }
                    var typeStr = type!.GetValue<string>();
                    if (filter != null && typeStr != filter) {
                        continue;
                    }

                    ImGui.Text(typeStr);
                    ImGui.SameLine();
                    var label = e.TryGetPropertyValue("label", out var labelNode) ? labelNode!.GetValue<string>() : null;
                    if (label == null) {
                        label = e.TryGetPropertyValue("id", out var idNode) ? idNode!.GetValue<string>() : null;
                    }
                    if (label == null) {
                        ImGui.Text("Unknown legacy entity type");
                    } else {
                        ImGui.Text(label);
                    }
                }
                ImGui.TreePop();
            }
        }

        if (ImGui.TreeNode("Entities")) {
            var types = allOption.Concat(bundle.Entities.Select(e => e.Type).Distinct()).ToArray();
            ImGui.Combo("Type", ref selectedEntityType, types, types.Length);
            var filter = selectedEntityType > 0 && selectedEntityType < types.Length ? types[selectedEntityType] : null;
            foreach (var e in bundle.Entities) {
                if (filter != null && e.Type != filter) continue;
                ImGui.Text($"{e.Type} {e.Id} : {e.Label}");
            }

            ImGui.TreePop();
        }

        if (bundle.ResourceListing != null && ImGui.TreeNode("Files")) {
            foreach (var e in bundle.ResourceListing) {
                ImGui.PushID(e.Key);
                if (openFileCallback != null) {
                    if (ImGui.Button("Open")) {
                        var path = bundleManager.ResolveBundleLocalPath(bundle, e.Key);
                        if (bundle.Name == preselectBundle || !File.Exists(path)) {
                            openFileCallback.Invoke(e.Value.Target);
                        } else {
                            openFileCallback.Invoke(path);
                        }
                    }
                    ImGui.SameLine();
                }
                if (e.Value.Diff != null && showDiff != null && (e.Value.Diff is JsonObject odiff && odiff.Count > 1)) {
                    if (ImGui.Button("Show diff")) {
                        showDiff.Invoke($"{e.Key} => {e.Value.Target}", e.Value.Diff);
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetItemTooltip("Partial patch generated at: " + e.Value.DiffTime.ToString("O"));
                    ImGui.SameLine();
                }
                ImGui.Text(e.Key);
                ImGui.SameLine();
                ImGui.TextColored(Colors.Faded, e.Value.Target);
                ImGui.PopID();
            }

            ImGui.TreePop();
        }
    }
    private void ShowBundleToolbar()
    {
        ImguiHelpers.ToggleButtonMultiColor(AppIcons.SIC_BundleAdd, ref isNewBundleMenu, new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary }, Colors.IconActive);
        ImguiHelpers.Tooltip("New Bundle");
        ImGui.SameLine();
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_BundleContain, new[] { Colors.IconPrimary, Colors.IconSecondary, Colors.IconSecondary }, "000") && createFromLooseFileFolder != null) {
            PlatformUtils.ShowFolderDialog(folder => {
                createFromLooseFileFolder(folder);
            });
        }
        ImguiHelpers.Tooltip("Create Bundle from Loose Files");
        ImGui.SameLine();
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_BundleContain, new[] { Colors.IconSecondary, Colors.IconPrimary, Colors.IconPrimary }, "001") && createFromPak != null) {
            PlatformUtils.ShowFileDialog(pak =>
                createFromPak(pak[0]),
                fileExtension: FileFilters.PakFile,
                allowMultiple: false
            );
        }
        ImguiHelpers.Tooltip("Create Bundle from PAK File");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_BundleRemove, new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconTertiary, Colors.IconTertiary })) {
            EditorWindow.CurrentWindow?.SetWorkspace(EditorWindow.CurrentWindow.Workspace.Env.Config.Game, null);
        }
        ImGui.PopStyleColor();
        ImguiHelpers.Tooltip("Unload current Bundle");
        ImGui.SameLine();
        ImGui.Text("|");
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(string.IsNullOrEmpty(bundleManager.GamePath))) {
            if (ImGui.Button($"{AppIcons.SI_FolderOpen}")) {
                FileSystemUtils.ShowFileInExplorer(bundleManager.GamePath);
            }
            ImguiHelpers.Tooltip("Open game folder in Explorer");
            ImGui.SameLine();
            if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FolderContain, new[] {Colors.IconPrimary, Colors.IconSecondary})) {
                FileSystemUtils.ShowFileInExplorer(bundleManager.AppBundlePath);
            }
            ImguiHelpers.Tooltip("Open Bundles folder in Explorer");
        }
        ImGui.SameLine();
        ImGui.Text("|");
        
        ShowNewBundleMenu();
    }
    private void ShowNewBundleMenu()
    {
        if (isNewBundleMenu) {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            using (var _ = ImguiHelpers.Disabled(string.IsNullOrEmpty(newBundleName))) {
                ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconActive);
                if (ImGui.Button($"{AppIcons.SI_GenericAdd}")) {
                    if (bundleManager.CreateBundle(newBundleName) != null) {
                        data.SetPersistentData("selectedBundle", newBundleName);
                        newBundleName = "";
                    } else {
                        WindowManager.Instance.ShowError("Bundle already exists!", data);
                    }
                }
                ImGui.PopStyleColor();
                ImguiHelpers.Tooltip("Create");
            }
            ImGui.SameLine();
            ImGui.InputText("Bundle Name", ref newBundleName, 100);
        }
    }
    private int selectedLegacyEntityType = 0;
    private int selectedEntityType = 0;
    private static readonly string[] allOption = ["All"];

    public bool RequestClose()
    {
        return false;
    }
}
