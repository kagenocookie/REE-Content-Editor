using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace ContentEditor.App;

public class BundleManagementUI : IWindowHandler
{
    private BundleManager bundleManager;
    private readonly string? preselectBundle;
    private readonly Action<string>? openFileCallback;
    private readonly Action<string, JsonNode>? showDiff;
    public delegate void CreateBundleFromLooseFileFolderDelegate(string folder);
    public delegate void CreateBundleFromPakDelegate(string pak);

    private Texture? loadedThumbnail;

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
    private string newBundleName = string.Empty;
    private bool isNewBundleMenu = false;
    private Bundle? draggedBundle;

    private int selectedLegacyEntityType = 0;
    private int selectedEntityType = 0;
    private static readonly string[] allOption = ["All"];

    private WindowData data = null!;
    protected UIContext context = null!;
    private readonly CreateBundleFromLooseFileFolderDelegate? createFromLooseFileFolder;
    private readonly CreateBundleFromPakDelegate? createFromPak;
    private static string timeFormat => AppConfig.Instance.ClockFormat.Get() ? " hh:mm:ss tt" : " HH:mm:ss";

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
        ShowBundlesMenu();
    }
    private void ShowBundleToolbar()
    {
        ImguiHelpers.ButtonMultiColor(AppIcons.SIC_InfoBundle, new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary, Colors.Info });
        ImguiHelpers.Tooltip($"Total Bundles: {bundleManager.AllBundles.Count} | Active Bundles: {bundleManager.ActiveBundles.Count}");
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();
        ImguiHelpers.ToggleButtonMultiColor(AppIcons.SIC_BundleAdd, ref isNewBundleMenu, new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary }, Colors.IconActive);
        ImguiHelpers.Tooltip("New Bundle");
        ImGui.SameLine();
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_BundleFromLooseFiles, new[] { Colors.IconSecondary, Colors.IconSecondary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary }) && createFromLooseFileFolder != null) {
            PlatformUtils.ShowFolderDialog(folder => {
                createFromLooseFileFolder(folder);
            });
        }
        ImguiHelpers.Tooltip("Create Bundle from Loose Files");
        ImGui.SameLine();
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_BundleFromPakFile, new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary, Colors.IconSecondary }) && createFromPak != null) {
            PlatformUtils.ShowFileDialog(pak =>
                createFromPak(pak[0]),
                filters: FileFilters.PakFile,
                allowMultiple: false
            );
        }
        ImguiHelpers.Tooltip("Create Bundle from PAK File");
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
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
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(EditorWindow.CurrentWindow?.Workspace.CurrentBundle == null)) {
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconActive);
            if (ImGui.Button($"{AppIcons.SI_GenericExport}")) {
                EditorWindow.CurrentWindow?.AddUniqueSubwindow(new ModPublisherWindow(EditorWindow.CurrentWindow.Workspace));
            }
            ImGui.PopStyleColor();
            ImguiHelpers.Tooltip("Publish Mod");
        }
        ImGui.SameLine();
        ImguiHelpers.AlignElementRight(ImGui.CalcTextSize($"{AppIcons.SI_GenericClose}").X * 4 + ImGui.GetStyle().ItemSpacing.X * 9);
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_PatchLooseFiles, new[] { Colors.IconPrimary, Colors.IconSecondary, Colors.IconSecondary, })) {
            EditorWindow.CurrentWindow?.ApplyContentPatches(null);
        }
        ImguiHelpers.Tooltip("Apply patches (Loose file)");
        ImGui.SameLine();
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_PatchPakFile, new[] { Colors.IconPrimary, Colors.IconSecondary, Colors.IconSecondary, })) {
            EditorWindow.CurrentWindow?.ApplyContentPatches("pak");
        }
        ImguiHelpers.Tooltip("Apply patches (PAK file)");
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_PatchTo}")) {
            PlatformUtils.ShowFolderDialog((path) => EditorWindow.CurrentWindow?.ApplyContentPatches(path), EditorWindow.CurrentWindow?.Workspace.Env.Config.GamePath);
        }
        ImguiHelpers.Tooltip("Patch to...");
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
        if (ImGui.Button($"{AppIcons.SI_Reset}")) {
            EditorWindow.CurrentWindow?.RevertContentPatches();
        }
        ImGui.PopStyleColor();
        ImguiHelpers.Tooltip("Revert patches");
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
                    var newBundle = bundleManager.CreateBundle(newBundleName);
                    if (newBundle != null) {
                        data.SetPersistentData("selectedBundle", newBundleName);
                        newBundle.Author = AppConfig.Settings.BundleDefaults.Author ?? "";
                        newBundle.Description = AppConfig.Settings.BundleDefaults.Description ?? "";
                        newBundle.Homepage = AppConfig.Settings.BundleDefaults.Homepage ?? "";
                        newBundle.Save();
                        newBundleName = "";
                    } else {
                        WindowManager.Instance.ShowError("Bundle already exists!", data);
                    }
                }
                ImGui.PopStyleColor();
                ImguiHelpers.Tooltip("Create");
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            AppImguiHelpers.ClearableInputText("##BundleName"u8, "Enter Bundle name here...", ref newBundleName, 100);
        }
    }
    private void ShowBundlesMenu()
    {
        var filter = data.GetPersistentData<string>("bundleFilter") ?? "";
        var selectedName = data.GetPersistentData<string>("selectedBundle");
        var selectedBundle = bundleManager.GetBundle(selectedName, null);
        var bundlesY = ImGui.GetFrameHeightWithSpacing() * 9 + ImGui.GetStyle().FramePadding.X + 120;

        ImGui.BeginChild("##Bundles", new Vector2(ImGui.GetContentRegionAvail().X / 1.8f, bundlesY));
        ImGui.PushItemWidth(350);
        ImGui.SeparatorText("Bundles");
        if (bundleManager.AllBundles.Count == 0) {
            ImGui.TextColored(Colors.Info, "No Bundles found!");
        } else {
            using (var _ = ImguiHelpers.Disabled(selectedBundle == null)) {
                if (ImGui.Button($"{AppIcons.SI_Save}")) {
                    selectedBundle!.Save();
                    EditorWindow.CurrentWindow?.Overlays.ShowTooltip("Saved!", 1f);
                }
                ImguiHelpers.Tooltip("Save bundle metadata");

                ImGui.SameLine();
                if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FolderOpenFileExplorer, new[] { Colors.IconSecondary, Colors.IconPrimary })) {
                    FileSystemUtils.ShowFileInExplorer(bundleManager.ResolveBundleLocalPath(selectedBundle!, ""));
                }
                ImguiHelpers.Tooltip("Open Current Bundle folder in File Explorer");

                ImGui.SameLine();
                using (var __ = ImguiHelpers.Disabled(EditorWindow.CurrentWindow?.Workspace.CurrentBundle == null)) {
                    ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
                    if (ImGui.Button($"{AppIcons.SI_BundleLoadOrder}")) {
                        EditorWindow.CurrentWindow?.SetWorkspace(
                            EditorWindow.CurrentWindow.Workspace.Env.Config.Game, null);
                    }
                    ImGui.PopStyleColor();
                    ImguiHelpers.Tooltip("Unload current Bundle");
                }
            }
            var names = bundleManager.AllBundles.Select(b => b.Name).ToArray();
            var bundlespan = CollectionsMarshal.AsSpan(bundleManager.AllBundles);
            if (ImguiHelpers.FilterableCombo("Bundle", names, bundlespan, ref selectedBundle, ref filter)) {
                selectedName = selectedBundle?.Name;
                data.SetPersistentData("selectedBundle", selectedName);
            }
            data.SetPersistentData("bundleFilter", filter);
        }

        var bundle = selectedName != null ? bundleManager.GetBundle(selectedName, null) : null;
        if (bundle != null) {
            var previousSelectedName = data.GetPersistentData<string>("activeBundleObserved");
            if (selectedName != previousSelectedName) {
                data.SetPersistentData("activeBundleObserved", selectedName);
                if (EditorWindow.CurrentWindow?.Workspace.CurrentBundle?.Name != bundle.Name) {
                    EditorWindow.CurrentWindow?.SetWorkspace(EditorWindow.CurrentWindow.Workspace.Env.Config.Game, bundle.Name);
                }
            }
            var str = bundle.Author ?? "";
            if (ImGui.InputText("Author", ref str, 100)) {
                bundle.Author = str;
            }
            str = bundle.Homepage ?? "";
            if (ImGui.InputText("Homepage", ref str, 100)) {
                bundle.Homepage = str;
            }
            str = bundle.Version ?? "";
            if (ImGui.InputText("Version", ref str, 100)) {
                bundle.Version = str;
            }
            str = bundle.Description ?? "";
            var w = ImGui.CalcItemWidth();
            if (ImGui.InputTextMultiline("Description", ref str, 1024, new Vector2(w, 120))) {
                bundle.Description = str;
            }
            str = bundle.ImagePath ?? "";
            var bundleFolder = bundleManager.GetBundleFolder(bundle);
            if (AppImguiHelpers.InputFilepath("Image", ref str, FileFilters.ImageFiles)) {
                if (File.Exists(str)) {
                    var localImageFilepath = str;
                    if (!string.IsNullOrEmpty(bundle.StorageFolder)) {
                        if (!str.StartsWith(bundle.StorageFolder)) {
                            var srcPath = str;
                            str = Path.Combine(bundleFolder, Path.GetFileName(str));
                            try {
                                File.Copy(srcPath, str, true);
                                EditorWindow.CurrentWindow?.Overlays.ShowTooltip("Image copied to bundle folder", 4);
                            } catch (Exception e) {
                                Logger.Error("Unable to copy file into bundle: " + e.Message);
                            }
                        }
                        localImageFilepath = Path.GetRelativePath(bundleFolder, str);
                    }
                    bundle.ImagePath = localImageFilepath;
                } else {
                    bundle.ImagePath = str;
                }
            }
            var resolvedBundleFilepath = "";
            if (!string.IsNullOrEmpty(bundle.ImagePath) && !Path.IsPathFullyQualified(bundle.ImagePath)) {
                var p = Path.Combine(bundleFolder, bundle.ImagePath);
                if (File.Exists(p)) resolvedBundleFilepath = p;
            }

            if (loadedThumbnail?.Path != resolvedBundleFilepath || !File.Exists(resolvedBundleFilepath)) {
                loadedThumbnail?.Dispose();
                loadedThumbnail = null;
            }

            if (File.Exists(resolvedBundleFilepath)) {
                if (loadedThumbnail == null) {
                    loadedThumbnail = new Texture();
                    loadedThumbnail.LoadFromFile(resolvedBundleFilepath, false);
                }
            }

            if (loadedThumbnail != null) {
                ImGui.SetNextItemOpen(true, ImGuiCond.Appearing);
                if (ImGui.TreeNode("Preview")) {
                    var h = w * (loadedThumbnail.Height / (float)loadedThumbnail.Width);
                    ImGui.Image(loadedThumbnail.AsTextureRef(), new Vector2(w, h));
                    if (ImGui.Button("Reload")) {
                        // wait until next frame before unloading so it doesn't glitch out
                        MainLoop.Instance.InvokeFromUIThread(() => {
                            loadedThumbnail?.Dispose();
                            loadedThumbnail = null;
                        });
                    }
                    ImGui.TreePop();
                }
            }

            ImGui.BeginDisabled();
            string createDate = "Created at: " + FormatUTCString(bundle.CreatedAt!);
            string updateDate = "Updated at: " + FormatUTCString(bundle.UpdatedAt!);
            ImGui.InputText("##CreationDate", ref createDate, 100);
            ImGui.InputText("##UpdateDate", ref updateDate, 100);
            ImGui.EndDisabled();
            ImGui.PopItemWidth();
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator(ImguiHelpers.GetColor(ImGuiCol.Separator), 2, 4, bundlesY);
        ImGui.SameLine();

        ImGui.BeginChild("##LoadOrder", new Vector2(ImGui.GetContentRegionAvail().X, bundlesY));
        ImGui.SeparatorText("Load Order");
        ShowBundleLoadOrderList();
        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (bundle != null) {
            var legacyEntityTypes = bundle.LegacyData?.Where(ld => ld.TryGetPropertyValue("type", out _)).Select(ld => ld["type"]!.GetValue<string>()).Distinct();
            if (legacyEntityTypes?.Any() == true) {
                if (ImGui.TreeNode("Legacy entities")) {
                    var types = allOption.Concat(legacyEntityTypes).ToArray();
                    if (ImGui.Combo("Type", ref selectedLegacyEntityType, types, types.Length)) {

                    }
                    var entityFilter = selectedLegacyEntityType > 0 && selectedLegacyEntityType < types.Length ? types[selectedLegacyEntityType] : null;
                    foreach (var e in bundle.LegacyData!) {
                        if (!e.TryGetPropertyValue("type", out var type)) {
                            continue;
                        }
                        var typeStr = type!.GetValue<string>();
                        if (entityFilter != null && typeStr != entityFilter) {
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
                var entityFilter = selectedEntityType > 0 && selectedEntityType < types.Length ? types[selectedEntityType] : null;
                foreach (var e in bundle.Entities) {
                    if (entityFilter != null && e.Type != entityFilter) continue;
                    ImGui.Text($"{e.Type} {e.Id} : {e.Label}");
                }

                ImGui.TreePop();
            }

            if (bundle.ResourceListing != null && ImGui.TreeNodeEx("Files", ImGuiTreeNodeFlags.Framed)) {
                ImGui.Indent(-ImGui.GetStyle().IndentSpacing);
                ImGui.Spacing();
                ImGui.PushStyleVar(ImGuiStyleVar.TreeLinesSize, 1.5f);
                if (ImGui.TreeNodeEx($"{AppIcons.SI_Bundle} " + bundle.Name, ImGuiTreeNodeFlags.DrawLinesFull | ImGuiTreeNodeFlags.DefaultOpen)) {
                    var tree = HierarchyTreeWidget.Build(bundle.ResourceListing.Select(e => e.Key));
                    HierarchyTreeWidget.Draw(tree, node => ShowHierarchyFileTreeActionButtons(node, bundle), node => OpenFileFromNode(node, bundle));
                    ImGui.TreePop();
                }
                ImGui.PopStyleVar();
                ImGui.Unindent();
                ImGui.TreePop();
            }
        }
    }
    private unsafe void ShowBundleLoadOrderList()
    {
        var bundles = draggedBundle == null ? bundleManager.AllBundles : bundleManager.AllBundles.ToList();
        if (bundles.Count == 0) {
            ImGui.TextColored(Colors.Info, "No Bundles detected!");
        }
        bool dragActiveThisFrame = false;
        foreach (var bundle in bundles) {
            ImGui.PushID(bundle.Name);
            var active = bundleManager.IsBundleActive(bundle);
            bool isDraggedRow = draggedBundle == bundle;
            var dragHandleColor = isDraggedRow ? Colors.TextActive : ImguiHelpers.GetColor(ImGuiCol.Text);
            float borderSize = isDraggedRow ? 2f : 0f;

            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, borderSize);
            ImGui.PushStyleColor(ImGuiCol.Text, dragHandleColor);
            ImGui.PushStyleColor(ImGuiCol.Border, dragHandleColor);
            ImGui.Button($"{AppIcons.SI_GenericReorder}");
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(2);

            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.PayloadNoCrossContext | ImGuiDragDropFlags.PayloadNoCrossProcess | ImGuiDragDropFlags.SourceNoPreviewTooltip)) {
                ImGui.SetDragDropPayload("BUNDLE"u8, null, 0);
                dragActiveThisFrame = true;
                draggedBundle = bundle;
                ImGui.EndDragDropSource();
            }

            if (draggedBundle != null && draggedBundle != bundle && ImGui.BeginDragDropTarget()) {
                bundleManager.SwapBundleOrders(draggedBundle, bundle);
                bundleManager.SaveSettings();
                ImGui.EndDragDropTarget();
            }
            ImGui.SameLine();
            if (ImGui.Checkbox($"##{bundle.Name}", ref active)) {
                bundleManager.SetBundleActive(bundle, active);
                bundleManager.SaveSettings();
            }
            ImGui.SameLine();
            ImguiHelpers.VerticalSeparator();
            ImGui.SameLine();
            using (var _ = ImguiHelpers.Disabled(!active)) {
                ImGui.TextColored(draggedBundle == bundle ? Colors.TextActive : ImguiHelpers.GetColor(ImGuiCol.Text), bundle.Name);
            }
            ImGui.Separator();
            ImGui.PopID();
        }

        if (!dragActiveThisFrame) draggedBundle = null;
    }
    private void ShowHierarchyFileTreeActionButtons(HierarchyTreeWidget node, Bundle bundle)
    {
        bundle.ResourceListing ??= new();
        var entryKey = node.EntryKey ?? "";
        bundle.ResourceListing.TryGetValue(entryKey, out var entry);

        ImGui.PushID(node.EntryKey ?? node.Name);
        using (var _ = ImguiHelpers.Disabled(entry == null)) {

            ShowOpenInEditorButton(node, bundle, entry);
            ImGui.SameLine();
            ShowEditNativesPathButton(entry, bundle);
            ImGui.SameLine();
            using (var __ = ImguiHelpers.Disabled(!(entry?.Diff != null && showDiff != null && entry.Diff is JsonObject odiff && odiff.Count > 1))) {
                if (ImGui.Button($"{AppIcons.SI_FileChanges}")) {
                    showDiff!.Invoke($"{node.EntryKey} => {entry!.Target}", entry.Diff!);
                }
                ImguiHelpers.Tooltip("Show changes\nPartial patch generated at: " + entry?.DiffTime.ToString("O"));
            }
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
            if (ImGui.Button($"{AppIcons.SI_GenericDelete2}")) {
                ImGui.OpenPopup("ConfirmDelete");
            }
            ImGui.PopStyleColor();
            ImguiHelpers.Tooltip("Delete file");

            if (ImGui.BeginPopupModal("ConfirmDelete", ImGuiWindowFlags.AlwaysAutoResize)) {
                string confirmText = $"Are you sure you want to delete {node.EntryKey} from {bundle.Name}?";
                var textSize = ImGui.CalcTextSize(confirmText);
                ImGui.Text(confirmText);
                ImGui.Separator();

                if (ImGui.Button("Yes", new Vector2(textSize.X / 2, 0))) {
                    var filePath = bundleManager.ResolveBundleLocalPath(bundle, entryKey);

                    bundle.ResourceListing.Remove(entryKey);

                    if (File.Exists(filePath)) {
                        File.Delete(filePath);
                    } else {
                        Logger.Error($"Failed to delete file {filePath}!");
                    }

                    Logger.Info($"Deleted {entryKey} from {bundle.Name}.");
                    bundle.Save();

                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("No", new Vector2(textSize.X / 2, 0))) {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }
        ImGui.PopID();
    }
    private void ShowOpenInEditorButton(HierarchyTreeWidget node, Bundle bundle, ResourceListItem? entry)
    {
        string? target = entry?.Target;

        using (var _ = ImguiHelpers.Disabled(target == null)) {
            if (ImGui.Button($"{AppIcons.SI_WindowOpenNew}") && openFileCallback != null) {
                var path = bundleManager.ResolveBundleLocalPath(bundle, node.EntryKey!);

                if (!File.Exists(path)) {
                    Logger.Warn("File not found in bundle folder, opening base file " + target);
                    openFileCallback!(target!);
                } else {
                    openFileCallback!(path);
                }
            }
            ImguiHelpers.Tooltip("Open file in Editor");
        }
    }
    private void ShowEditNativesPathButton(ResourceListItem? entry, Bundle bundle)
    {
        string? target = entry?.Target;
        using (var _ = ImguiHelpers.Disabled(target == null)) {
            if (ImGui.Button($"{AppIcons.SI_FileSource}")) {
                ImGui.OpenPopup("EditNativesPath");
            }
            ImguiHelpers.TooltipColored(target ?? "", Colors.Faded);
        }

        ShowEditNativesPathPopup(entry, bundle);
    }
    private void ShowEditNativesPathPopup(ResourceListItem? entry, Bundle bundle)
    {
        if (entry == null) return;

        string target = entry.Target;
        if (ImGui.BeginPopup("EditNativesPath")) {
            ImGui.SeparatorText("Edit Natives Path");
            ImGui.SetNextItemWidth(ImGui.CalcTextSize(target).X + 15);
            if (ImGui.InputText("##target", ref target, 512)) {
                entry.Target = target.ToLowerInvariant();
            }
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_Save}")) {
                entry.Target = target;
                bundle.Save();
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
    }
    private void OpenFileFromNode(HierarchyTreeWidget node, Bundle bundle)
    {
        if (node.EntryKey == null || openFileCallback == null) return;

        var path = bundleManager.ResolveBundleLocalPath(bundle, node.EntryKey);
        bundle.ResourceListing ??= new();
        bundle.ResourceListing.TryGetValue(node.EntryKey, out var entry);

        if (!File.Exists(path)) {
            if (entry != null) {
                Logger.Warn("File not found in bundle folder, opening base file " + entry.Target);
                openFileCallback!(entry.Target);
            } else {
                Logger.Error("File could not be opened");
            }
        } else {
            openFileCallback!(path);
        }
    }
    private string FormatUTCString(string utcString)
    {
        utcString = utcString.Replace("UTC", "").Trim();
        if (DateTime.TryParseExact(utcString, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)) {
            return utc.ToLocalTime().ToString(Time.dateFormat + timeFormat, CultureInfo.InvariantCulture);
        }
        return utcString;
    }
    public bool RequestClose()
    {
        return false;
    }
}
