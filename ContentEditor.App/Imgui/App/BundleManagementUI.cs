using ContentEditor.App.Graphics;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;
using ReeLib;
using System;
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
    private string newBundleName = "";
    private bool isNewBundleMenu = false;

    private int selectedLegacyEntityType = 0;
    private int selectedEntityType = 0;
    private static readonly string[] allOption = ["All"];

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
                fileExtension: FileFilters.PakFile,
                allowMultiple: false
            );
        }
        ImguiHelpers.Tooltip("Create Bundle from PAK File");
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_BundleLoadOrder}")) {
            EditorWindow.CurrentWindow?.AddUniqueSubwindow(new LoadOrderUI(bundleManager));
        }
        ImguiHelpers.Tooltip("Load Order");
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
                        bundleManager.SaveBundle(newBundle);
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
        if (ImGui.Button($"{AppIcons.SI_Save}")) {
            bundleManager.SaveBundle(bundle);
            EditorWindow.CurrentWindow?.Overlays.ShowTooltip("Saved!", 1f);
        }
        ImguiHelpers.Tooltip("Save bundle metadata");
        ImGui.SameLine();
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FolderOpenFileExplorer, new[] { Colors.IconSecondary, Colors.IconPrimary })) {
            FileSystemUtils.ShowFileInExplorer(bundleManager.ResolveBundleLocalPath(bundle, ""));
        }
        ImguiHelpers.Tooltip("Open current Bundle folder in File Explorer");

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
                if (!string.IsNullOrEmpty(bundle.SaveFilepath)) {
                    if (!str.StartsWith(bundle.SaveFilepath)) {
                        var srcPath = str;
                        str = Path.Combine(bundleFolder, Path.GetFileName(str));
                        try {
                            File.Copy(srcPath, str, true);
                            EditorWindow.CurrentWindow?.Overlays.ShowTooltip("Image copied to bundle folder", 4);
                        } catch(Exception e) {
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
                loadedThumbnail.LoadFromFile(resolvedBundleFilepath);
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
        string createDate = $"Created at: {bundle.CreatedAt}";
        string updateDate = $"Updated at: {bundle.UpdatedAt}";
        ImGui.InputText("##CreationDate", ref createDate, 100);
        ImGui.InputText("##UpdateDate", ref updateDate, 100);
        ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

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

        if (bundle.ResourceListing != null && ImGui.TreeNodeEx("Files", ImGuiTreeNodeFlags.Framed)) {
            ImGui.Indent(-ImGui.GetStyle().IndentSpacing); // SILVER: :slight_smile:
            ImGui.Spacing();
            ImGui.PushStyleVar(ImGuiStyleVar.TreeLinesSize, 1.5f);
            if (ImGui.TreeNodeEx($"{AppIcons.SI_Bundle} " + bundle.Name, ImGuiTreeNodeFlags.DrawLinesFull | ImGuiTreeNodeFlags.DefaultOpen)) {
                var tree = BuildHierarchyFileTree(bundle.ResourceListing.Select(e => e.Key));
                DrawHierarchyFileTreeWidget(tree, node => ShowHierarchyFileTreeActionButtons(node, bundle));
                ImGui.TreePop();
            }
            ImGui.PopStyleVar();
            ImGui.Unindent();
            ImGui.TreePop();
        }

    }
    // TODO SILVER: CLASS TO BE MOVED
    class HierarchyTreeWidget
    {
        public string Name = string.Empty;
        public string? EntryKey;
        public Dictionary<string, HierarchyTreeWidget> Children = new();
    }
    const float ActionColumnOffset = 30f; // SILVER: The amount we offset the action buttons from the left side of the tab
    const float ActionColumnWidth = 150f; // SILVER: The space we set for the action buttons
    static HierarchyTreeWidget BuildHierarchyFileTree(IEnumerable<string> entries)
    {
        var root = new HierarchyTreeWidget();

        foreach (var key in entries) {
            var parts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = root;

            for (int i = 0; i < parts.Length; i++) {
                string part = parts[i];
                bool isFile = Path.HasExtension(part);

                if (!current.Children.TryGetValue(part, out var node)) {
                    node = new HierarchyTreeWidget { Name = part, EntryKey = isFile ? key : null};
                    current.Children[part] = node;
                }
                current = node;
            }
        }
        return root;
    }

    static void DrawHierarchyFileTreeWidget(HierarchyTreeWidget node, Action<HierarchyTreeWidget>? drawActions = null, int hierarchyLayer = 0)
    {
        foreach (var child in node.Children.Values.OrderBy(c => c.EntryKey != null).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)) {

            ImGui.PushID(child.Name);

            bool isActionHovered = false;
            float rowY = ImGui.GetCursorPosY() + 5f;
            float contentX = ImGui.GetCursorPosX();
            ImGui.SetCursorPosX(ActionColumnOffset);
            ImGui.BeginChild("##actions", new Vector2(ActionColumnWidth, ImGui.GetTextLineHeight() + 10f), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            if (!(hierarchyLayer == 0 && child.EntryKey == null)) {
                drawActions?.Invoke(child);
                
            }
            ImGui.EndChild();
            isActionHovered = ImGui.IsItemHovered();

            ImGui.SetCursorPos(new Vector2(contentX + ActionColumnWidth, rowY));
            if (child.EntryKey != null) {
                char icon = AppIcons.SI_File;
                Vector4 col = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];

                if (Path.HasExtension(child.Name)) {
                    var (fileIcon, fileCol) = AppIcons.GetIcon(PathUtils.ParseFileFormat(child.Name).format);

                    if (fileIcon != '\0') {
                        icon = fileIcon;
                        col = fileCol;
                    }
                }
                ImGui.Dummy(new Vector2(5f, 0));
                ImGui.SameLine();
                ImGui.TextColored(col, $"{icon}");
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, isActionHovered ? Colors.TextActive : ImguiHelpers.GetColor(ImGuiCol.Text));
                ImGui.Selectable(child.Name);
                ImGui.PopStyleColor();
            } else {
                bool isNestedFolder = ImGui.TreeNodeEx($"{AppIcons.SI_FolderEmpty} " + child.Name,  ImGuiTreeNodeFlags.DrawLinesToNodes | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAllColumns);
                if (isNestedFolder) {
                    DrawHierarchyFileTreeWidget(child, drawActions, hierarchyLayer + 1);
                    ImGui.TreePop();
                }
            }
            ImGui.PopID();
        }
    }
    // TODO SILVER: Clean up this method
    private void ShowHierarchyFileTreeActionButtons(HierarchyTreeWidget node, Bundle bundle)
    {

        bundle.ResourceListing.TryGetValue(node.EntryKey ?? "", out var entry);

        ImGui.PushID(node.EntryKey ?? node.Name);

        using (var _ = ImguiHelpers.Disabled(entry == null)) {
            if (openFileCallback != null) {
                if (ImGui.Button($"{AppIcons.SI_WindowOpenNew}")) {
                    var path = bundleManager.ResolveBundleLocalPath(bundle, node.EntryKey);
                    if (!File.Exists(path)) {
                        Logger.Warn("File not found in bundle folder, opening base file " + entry.Target);
                        openFileCallback.Invoke(entry.Target);
                    } else {
                        openFileCallback.Invoke(path);
                    }
                }
                ImguiHelpers.Tooltip(entry != null ? "Open file in Editor" : "Lorem Ipsum");
            }

            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_FileSource}")) {
                ImGui.OpenPopup("EditNativesPath");
            }
            ImguiHelpers.TooltipColored(entry?.Target ?? "Lorem Ipsum Vol.2", Colors.Faded);

            if (ImGui.BeginPopup("EditNativesPath")) {
                string target = entry.Target;

                ImGui.SeparatorText("Edit Natives Path");

                var pathSize = ImGui.CalcTextSize(target);
                ImGui.SetNextItemWidth(pathSize.X + 15);

                if (ImGui.InputText("##target", ref target, 512)) {
                    entry.Target = target;
                }

                ImGui.SameLine();
                if (ImGui.Button($"{AppIcons.SI_Save}")) {
                    entry.Target = target;
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
            using (var __ = ImguiHelpers.Disabled(!(entry?.Diff != null && showDiff != null && entry.Diff is JsonObject odiff && odiff.Count > 1))) {
                if (ImGui.Button($"{AppIcons.SI_FileChanges}")) {
                    showDiff!.Invoke($"{node.EntryKey} => {entry.Target}", entry.Diff!);
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
                    var filePath = bundleManager.ResolveBundleLocalPath(bundle, node.EntryKey);

                    bundle.ResourceListing.Remove(node.EntryKey);

                    if (File.Exists(filePath)) {
                        File.Delete(filePath);
                    } else {
                        Logger.Error($"Failed to delete file {filePath}!");
                    }

                    Logger.Info($"Deleted {node.EntryKey} from {bundle.Name}.");
                    bundleManager.SaveBundle(bundle);

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


    public bool RequestClose()
    {
        return false;
    }
}
