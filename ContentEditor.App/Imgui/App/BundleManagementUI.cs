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
        ShowBundlesMenu();
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
        ImGui.Text("|");
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(string.IsNullOrEmpty(bundleManager.GamePath))) {
            if (ImGui.Button($"{AppIcons.SI_FolderOpen}")) {
                FileSystemUtils.ShowFileInExplorer(bundleManager.GamePath);
            }
            ImguiHelpers.Tooltip("Open game folder in File Explorer");
            ImGui.SameLine();
            if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FolderContain, new[] {Colors.IconPrimary, Colors.IconSecondary})) {
                FileSystemUtils.ShowFileInExplorer(bundleManager.AppBundlePath);
            }
            ImguiHelpers.Tooltip("Open Bundles folder in File Explorer");
        }
        ImGui.SameLine();
        ImGui.Text("|");
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(EditorWindow.CurrentWindow?.Workspace.CurrentBundle == null)) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconActive);
            if (ImGui.Button($"{AppIcons.SI_GenericExport}")) {
                EditorWindow.CurrentWindow?.AddUniqueSubwindow(new ModPublisherWindow(EditorWindow.CurrentWindow.Workspace));
            }
            ImGui.PopStyleColor();
            ImguiHelpers.Tooltip("Publish Mod");
        }
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
            ImGui.SetNextItemAllowOverlap();
            ImGui.InputTextWithHint("##BundleName", "Enter Bundle name here...", ref newBundleName, 100);
            if (!string.IsNullOrEmpty(newBundleName)) {
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() - (ImGui.CalcTextSize($"{AppIcons.SI_GenericError}").X * 2));
                ImGui.SetNextItemAllowOverlap();
                if (ImGui.Button($"{AppIcons.SI_GenericClose}")) {
                    newBundleName = string.Empty;
                }
            }
        }
    }
    private void ShowBundlesMenu()
    {
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
        using (var _ = ImguiHelpers.Disabled(EditorWindow.CurrentWindow?.Workspace.CurrentBundle?.Name == bundle.Name)) {
            if (ImGui.Button($"{AppIcons.SI_Bundle}")) {
                EditorWindow.CurrentWindow?.SetWorkspace(EditorWindow.CurrentWindow.Workspace.Env.Config.Game, bundle.Name);
            }
            ImguiHelpers.Tooltip("Set as Active Bundle");
        }
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(EditorWindow.CurrentWindow?.Workspace.CurrentBundle == null)) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
            if (ImGui.Button($"{AppIcons.SI_Reset}")) {
                EditorWindow.CurrentWindow?.SetWorkspace(EditorWindow.CurrentWindow.Workspace.Env.Config.Game, null);
            }
            ImGui.PopStyleColor();
            ImguiHelpers.Tooltip("Unload current Bundle");
        }
        ImGui.SameLine();
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FolderOpenFileExplorer, new[] { Colors.IconSecondary, Colors.IconPrimary })) {
            FileSystemUtils.ShowFileInExplorer(bundleManager.ResolveBundleLocalPath(bundle, ""));
        }
        ImguiHelpers.Tooltip("Open current Bundle folder in File Explorer");

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

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Indent();
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

        if (ImGui.TreeNodeEx("Entities", ImGuiTreeNodeFlags.Framed)) {
            var types = allOption.Concat(bundle.Entities.Select(e => e.Type).Distinct()).ToArray();
            ImGui.Combo("Type", ref selectedEntityType, types, types.Length);
            var filter = selectedEntityType > 0 && selectedEntityType < types.Length ? types[selectedEntityType] : null;
            foreach (var e in bundle.Entities) {
                if (filter != null && e.Type != filter) continue;
                ImGui.Text($"{e.Type} {e.Id} : {e.Label}");
            }

            ImGui.TreePop();
        }
        // SILVER: We'll probably need some sorting options here, File Type | Name A-Z/Z-A | File Size?
        if (bundle.ResourceListing != null && ImGui.TreeNodeEx("Files", ImGuiTreeNodeFlags.Framed)) {
            if (ImGui.BeginTable("FilesTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.BordersInnerH)) {
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 175f); // SILVER: Added some extra padding so we don't have 5 icons next to each other 
                ImGui.TableSetupColumn("Files", ImGuiTableColumnFlags.WidthStretch);

                foreach (var entry in bundle.ResourceListing) {
                    ImGui.PushID(entry.Key);
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    if (openFileCallback != null) {
                        if (ImGui.Button($"{AppIcons.SI_WindowOpenNew}")) {
                            var path = bundleManager.ResolveBundleLocalPath(bundle, entry.Key);
                            if (bundle.Name == preselectBundle || !File.Exists(path)) {
                                openFileCallback.Invoke(entry.Value.Target);
                            } else {
                                openFileCallback.Invoke(path);
                            }
                        }
                        ImguiHelpers.Tooltip("Open file in Editor");
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"{AppIcons.SI_FileSource}")) {
                        ImGui.OpenPopup("EditNativesPath");
                    }
                    ImguiHelpers.TooltipColored(entry.Value.Target, Colors.Faded);

                    if (ImGui.BeginPopup("EditNativesPath")) {
                        string target = entry.Value.Target;

                        ImGui.SeparatorText("Edit Natives Path");

                        var pathSize = ImGui.CalcTextSize(target);
                        ImGui.SetNextItemWidth(pathSize.X + 15);
                        if (ImGui.InputText("##target", ref target, 512)) {
                            entry.Value.Target = target;
                        }
                        ImGui.SameLine();
                        if (ImGui.Button($"{AppIcons.SI_Save}")) {
                            entry.Value.Target = target;
                            bundleManager.SaveBundle(bundle);
                            ImGui.CloseCurrentPopup();
                        }
                        ImguiHelpers.Tooltip("Save");
                        ImGui.SameLine();
                        if (ImGui.Button($"{AppIcons.SI_GenericClose}")) {
                            ImGui.CloseCurrentPopup();
                        }
                        ImguiHelpers.Tooltip("Cancel");

                        ImGui.EndPopup();
                    }

                    ImGui.SameLine();
                    using (var _ = ImguiHelpers.Disabled(!(entry.Value.Diff != null && showDiff != null && (entry.Value.Diff is JsonObject odiff && odiff.Count > 1)))) {
                        if (ImGui.Button($"{AppIcons.SI_FileChanges}")) {
                            showDiff.Invoke($"{entry.Key} => {entry.Value.Target}", entry.Value.Diff);
                        }
                        ImguiHelpers.Tooltip("Show changes\nPartial patch generated at: " + entry.Value.DiffTime.ToString("O"));
                    }
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
                    if (ImGui.Button($"{AppIcons.SI_GenericDelete}")) {
                        ImGui.OpenPopup("Confirm Action");
                    }
                    ImGui.PopStyleColor();
                    ImguiHelpers.Tooltip("Delete file");

                    if (ImGui.BeginPopupModal("Confirm Action", ImGuiWindowFlags.AlwaysAutoResize)) {
                        string confirmText = $"Are you sure you want to delete {entry.Key} from {bundle.Name}?";
                        var textSize = ImGui.CalcTextSize(confirmText);
                        ImGui.Text(confirmText);
                        ImGui.Separator();
                        // SILVER: I assume there's no method for this atm?
                        if (ImGui.Button("Yes (but actually No)", new Vector2(textSize.X / 2, 0))) {
                            Logger.Info($"Deleted {entry.Key} from {bundle.Name}.");
                            ImGui.CloseCurrentPopup();
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("No", new Vector2(textSize.X / 2, 0))) {
                            ImGui.CloseCurrentPopup();
                        }
                        ImGui.EndPopup();
                    }

                    ImGui.TableSetColumnIndex(1);
                    char icon = AppIcons.SI_File;
                    Vector4 col = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
                    if (Path.HasExtension(entry.Key)) {
                        var (fileIcon, fileCol) = AppIcons.GetIcon(PathUtils.ParseFileFormat(entry.Key).format);
                        if (fileIcon != '\0') {
                            icon = fileIcon; col = fileCol;
                        }
                    }
                    ImGui.TextColored(col, $"{icon}");
                    ImGui.SameLine();
                    ImGui.Text(entry.Key);

                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            ImGui.TreePop();
        }
        ImGui.Unindent();
    }

    private int selectedLegacyEntityType = 0;
    private int selectedEntityType = 0;
    private static readonly string[] allOption = ["All"];

    public bool RequestClose()
    {
        return false;
    }
}
