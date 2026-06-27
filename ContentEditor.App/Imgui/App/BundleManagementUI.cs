using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ReeLib;
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
    private Bundle? hoveredBundle;

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
        ImguiHelpers.Tooltip(Lang.Bundles.BundleCount.Format(bundleManager.AllBundles.Count, bundleManager.ActiveBundles.Count));
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();
        ImguiHelpers.ToggleButtonMultiColor(AppIcons.SIC_BundleAdd, ref isNewBundleMenu, new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary }, Colors.IconActive);
        ImguiHelpers.Tooltip(Lang.Bundles.NewBundle);
        ImGui.SameLine();
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_BundleFromLooseFiles, new[] { Colors.IconSecondary, Colors.IconSecondary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary }) && createFromLooseFileFolder != null) {
            PlatformUtils.ShowFolderDialog(folder => {
                createFromLooseFileFolder(folder);
            });
        }
        ImguiHelpers.Tooltip(Lang.Bundles.CreateFromLooseFiles);
        ImGui.SameLine();
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_BundleFromPakFile, new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconSecondary, Colors.IconSecondary }) && createFromPak != null) {
            PlatformUtils.ShowFileDialog(pak =>
                createFromPak(pak[0]),
                filters: FileFilters.PakFile,
                allowMultiple: false
            );
        }
        ImguiHelpers.Tooltip(Lang.Bundles.CreateFromPakFile);
        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator();
        ImGui.SameLine();
        using (var _ = ImguiHelpers.Disabled(string.IsNullOrEmpty(bundleManager.GamePath))) {
            if (ImGui.Button($"{AppIcons.SI_FolderOpen}")) {
                FileSystemUtils.ShowFileInExplorer(bundleManager.GamePath);
            }
            ImguiHelpers.Tooltip(Lang.Bundles.OpenGameFolder);
            ImGui.SameLine();
            if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FolderContain, new[] {Colors.IconPrimary, Colors.IconSecondary})) {
                FileSystemUtils.ShowFileInExplorer(bundleManager.AppBundlePath);
            }
            ImguiHelpers.Tooltip(Lang.Bundles.OpenBundlesFolder);
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
            ImguiHelpers.Tooltip(Lang.Bundles.PublishMod);
        }
        ImGui.SameLine();
        ImguiHelpers.AlignElementRight(ImGui.CalcTextSize($"{AppIcons.SI_GenericClose}").X * 4 + ImGui.GetStyle().ItemSpacing.X * 9);
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_PatchLooseFiles, new[] { Colors.IconPrimary, Colors.IconSecondary, Colors.IconSecondary, })) {
            EditorWindow.CurrentWindow?.ApplyContentPatches(null);
        }
        ImguiHelpers.Tooltip(Lang.Bundles.ApplyPatchesLoose);
        ImGui.SameLine();
        if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_PatchPakFile, new[] { Colors.IconPrimary, Colors.IconSecondary, Colors.IconSecondary, })) {
            EditorWindow.CurrentWindow?.ApplyContentPatches("pak");
        }
        ImguiHelpers.Tooltip(Lang.Bundles.ApplyPatchesPak);
        ImGui.SameLine();
        if (ImGui.Button($"{AppIcons.SI_PatchTo}")) {
            PlatformUtils.ShowFolderDialog((path) => EditorWindow.CurrentWindow?.ApplyContentPatches(path), EditorWindow.CurrentWindow?.Workspace.Env.Config.GamePath);
        }
        ImguiHelpers.Tooltip(Lang.Bundles.PatchTo);
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
        if (ImGui.Button($"{AppIcons.SI_Reset}")) {
            EditorWindow.CurrentWindow?.RevertContentPatches();
        }
        ImGui.PopStyleColor();
        ImguiHelpers.Tooltip(Lang.Bundles.RevertPatches);
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
                ImguiHelpers.Tooltip(Lang.Buttons.Create);
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            AppImguiHelpers.ClearableInputText("##BundleName"u8, Lang.Bundles.BundleNamePlaceholder.String, ref newBundleName, 100);
        }
    }
    private void ShowBundlesMenu()
    {
        var filter = data.GetPersistentData<string>("bundleFilter") ?? "";
        var selectedName = data.GetPersistentData<string>("selectedBundle");
        var selectedBundle = bundleManager.GetBundle(selectedName, null);
        var bundlesY = ImGui.GetFrameHeightWithSpacing() * 9 + ImGui.GetStyle().FramePadding.X + 120;
        var summaryBundle = selectedBundle;
        var isPreview = false;
        if (hoveredBundle != null && hoveredBundle != selectedBundle) {
            isPreview = true;
            summaryBundle = hoveredBundle;
        }

        ImGui.BeginChild("##Bundles", new Vector2(ImGui.GetContentRegionAvail().X / 1.8f, bundlesY));
        ImGui.PushItemWidth(350);
        ImGui.SeparatorText(Lang.Bundles.Title);
        if (bundleManager.AllBundles.Count == 0) {
            ImGui.TextColored(Colors.Info, Lang.Bundles.NoBundlesFound);
        } else if (!isPreview) {
            using (var _ = ImguiHelpers.Disabled(selectedBundle == null)) {
                if (ImGui.Button($"{AppIcons.SI_Save}")) {
                    selectedBundle!.Save();
                    EditorWindow.CurrentWindow?.Overlays.ShowTooltip("Saved!", 1f);
                }
                ImguiHelpers.Tooltip(Lang.Bundles.SaveBundleMetadata);

                ImGui.SameLine();
                if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_FolderOpenFileExplorer, new[] { Colors.IconSecondary, Colors.IconPrimary })) {
                    FileSystemUtils.ShowFileInExplorer(bundleManager.ResolvePathToBundleFile(selectedBundle!, ""));
                }
                ImguiHelpers.Tooltip(Lang.Bundles.OpenCurrentBundleFolder);

                ImGui.SameLine();
                using (var __ = ImguiHelpers.Disabled(EditorWindow.CurrentWindow?.Workspace.CurrentBundle == null)) {
                    if (ImguiHelpers.ButtonMultiColor(AppIcons.SIC_BundleUnload, new[] { Colors.IconPrimary, Colors.IconPrimary, Colors.IconPrimary, Colors.IconTertiary, Colors.IconTertiary, Colors.IconTertiary })) {
                        EditorWindow.CurrentWindow?.SetWorkspace(EditorWindow.CurrentWindow.Workspace.Env.Config.Game, null);
                    }
                    ImguiHelpers.Tooltip(Lang.Bundles.UnloadCurrentBundle);
                }
                ImGui.SameLine();
                if (selectedBundle?.HasResources == true && ImGui.Button(Lang.Bundles.RebuildPatchDiffs)) {
                    foreach (var r in selectedBundle.Resources) {
                        r.Diff = null;
                        r.DiffTime = default;
                    }
                    selectedBundle.Save();
                    if (selectedBundle == EditorWindow.CurrentWindow?.Workspace.CurrentBundle) {
                        context.GetWorkspace()?.SaveBundle(true);
                    }
                }
            }
            var names = bundleManager.AllBundles.Select(b => b.Name).ToArray();
            var bundlespan = CollectionsMarshal.AsSpan(bundleManager.AllBundles);
            if (ImguiHelpers.FilterableCombo(Lang.Bundles.Bundle, names, bundlespan, ref selectedBundle, ref filter)) {
                selectedName = selectedBundle?.Name;
                data.SetPersistentData("selectedBundle", selectedName);
            }
            data.SetPersistentData("bundleFilter", filter);
        }

        var bundle = selectedName != null ? bundleManager.GetBundle(selectedName, null) : null;
        if (summaryBundle != null && isPreview) {
            ImGui.Text(Lang.Bundles.NameVersion_ReadOnly.Format(summaryBundle.Name, summaryBundle.Version ?? "v1.0.0"));
            if (!string.IsNullOrEmpty(summaryBundle.Author)) {
                ImGui.Text(Lang.Bundles.Author_ReadOnly.Format(summaryBundle.Author));
            }
            if (!string.IsNullOrEmpty(summaryBundle.Homepage)) {
                ImGui.Text(Lang.Bundles.Homepage_ReadOnly.Format(summaryBundle.Homepage));
                if (ImGui.IsItemClicked()) {
                    FileSystemUtils.OpenURL(summaryBundle.Homepage);
                    EditorWindow.CurrentWindow?.Overlays.ShowTooltip(Lang.General.URLOpened.String, 2f);
                }
            }
            var bundleFolder = bundleManager.GetBundleFolder(summaryBundle);
            var w = ImGui.CalcItemWidth();
            ShowBundleThumbnail(summaryBundle, w, bundleFolder, true);

            if (!string.IsNullOrEmpty(summaryBundle.Description)) {
                ImGui.Text(summaryBundle.Description);
            }
            if (summaryBundle.UpdatedAt != null) {
                ImGui.Text(Lang.Bundles.BundleLastUpdate_ReadOnly.Format(FormatUTCString(summaryBundle.UpdatedAt)));
            }
        } else if (bundle != null && !isPreview) {
            var previousSelectedName = data.GetPersistentData<string>("activeBundleObserved");
            if (selectedName != previousSelectedName) {
                data.SetPersistentData("activeBundleObserved", selectedName);
                if (EditorWindow.CurrentWindow?.Workspace.CurrentBundle?.Name != bundle.Name) {
                    EditorWindow.CurrentWindow?.SetWorkspace(EditorWindow.CurrentWindow.Workspace.Env.Config.Game, bundle.Name);
                }
            }
            var str = bundle.Author ?? "";
            if (ImGui.InputText(Lang.Bundles.Author, ref str, 100)) {
                bundle.Author = str;
            }
            str = bundle.Homepage ?? "";
            if (ImGui.InputText(Lang.Bundles.Homepage, ref str, 100)) {
                bundle.Homepage = str;
            }
            str = bundle.Version ?? "";
            if (ImGui.InputText(Lang.Bundles.Version, ref str, 100)) {
                bundle.Version = str;
            }
            str = bundle.Description ?? "";
            var w = ImGui.CalcItemWidth();
            if (ImGui.InputTextMultiline(Lang.Bundles.Description, ref str, 1024, new Vector2(w, 120))) {
                bundle.Description = str;
            }
            str = bundle.ImagePath ?? "";
            var bundleFolder = bundleManager.GetBundleFolder(bundle);
            if (AppImguiHelpers.InputFilepath(Lang.Bundles.Image, ref str, FileFilters.ImageFiles)) {
                if (File.Exists(str)) {
                    var localImageFilepath = str;
                    if (!string.IsNullOrEmpty(bundle.StoragePath)) {
                        if (!str.StartsWith(bundle.StoragePath)) {
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
            ShowBundleThumbnail(bundle, w, bundleFolder, false);

            ImGui.BeginDisabled();
            string createDate = Lang.Bundles.CreatedAt.FormatRef(FormatUTCString(bundle.CreatedAt!)).String;
            string updateDate = Lang.Bundles.UpdatedAt.FormatRef(FormatUTCString(bundle.UpdatedAt!)).String;
            ImGui.InputText("##CreationDate"u8, ref createDate, 100);
            ImGui.InputText("##UpdateDate"u8, ref updateDate, 100);
            ImGui.EndDisabled();
            ImGui.PopItemWidth();
        }
        ImGui.EndChild();

        ImGui.SameLine();
        ImguiHelpers.VerticalSeparator(ImguiHelpers.GetColor(ImGuiCol.Separator), 2, 4, bundlesY);
        ImGui.SameLine();

        ImGui.BeginChild("##LoadOrder"u8, new Vector2(ImGui.GetContentRegionAvail().X, bundlesY));
        ImGui.SeparatorText(Lang.Bundles.LoadOrder);
        ShowBundleLoadOrderList();
        ImGui.EndChild();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (bundle != null) {
            var legacyEntityTypes = bundle.LegacyData?.Where(ld => ld.TryGetPropertyValue("type", out _)).Select(ld => ld["type"]!.GetValue<string>()).Distinct();
            if (legacyEntityTypes?.Any() == true) {
                if (ImGui.TreeNode(Lang.Bundles.LegacyEntities)) {
                    var types = allOption.Concat(legacyEntityTypes).ToArray();
                    ImGui.Combo(Lang.Bundles.EntityType.String, ref selectedLegacyEntityType, types, types.Length);
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
                            ImGui.Text(Lang.Bundles.UnknownLegacyEntityType);
                        } else {
                            ImGui.Text(label);
                        }
                    }
                    ImGui.TreePop();
                }
            }

            if (ImGui.TreeNodeEx(Lang.Bundles.Entities, ImGuiTreeNodeFlags.Framed)) {
                var types = allOption.Concat(bundle.Entities.Select(e => e.Type).Distinct()).ToArray();
                ImGui.Combo(Lang.Bundles.EntityType.String, ref selectedEntityType, types, types.Length);
                var entityFilter = selectedEntityType > 0 && selectedEntityType < types.Length ? types[selectedEntityType] : null;
                foreach (var e in bundle.Entities) {
                    if (entityFilter != null && e.Type != entityFilter) continue;
                    ImGui.Text($"{e.Type} {e.Id} : {e.Label}");
                }

                ImGui.TreePop();
            }

            if (bundle.HasResources && ImGui.TreeNodeEx(Lang.Bundles.Files, ImGuiTreeNodeFlags.Framed)) {
                ImGui.Indent(-ImGui.GetStyle().IndentSpacing);
                ImGui.Spacing();
                ImGui.PushStyleVar(ImGuiStyleVar.TreeLinesSize, 1.5f);
                if (ImGui.TreeNodeEx($"{AppIcons.SI_Bundle} " + bundle.Name, ImGuiTreeNodeFlags.DrawLinesFull | ImGuiTreeNodeFlags.DefaultOpen)) {
                    var tree = HierarchyTreeWidget.Build(bundle.ResourceLocalPaths);
                    HierarchyTreeWidget.Draw(tree, node => ShowHierarchyFileTreeActionButtons(node, bundle), node => OpenFileFromNode(node, bundle));
                    ImGui.TreePop();
                }
                ImGui.PopStyleVar();
                ImGui.Unindent();
                ImGui.TreePop();
            }
        }
    }

    private void ShowBundleThumbnail(Bundle bundle, float width, string bundleFolder, bool isPreview)
    {
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
            if (isPreview || ImGui.TreeNode(Lang.Bundles.Preview)) {
                var h = width * (loadedThumbnail.Height / (float)loadedThumbnail.Width);
                ImGui.Image(loadedThumbnail.AsTextureRef(), new Vector2(width, h));
                if (!isPreview) {
                    if (ImGui.Button(Lang.Buttons.Reload)) {
                        // wait until next frame before unloading so it doesn't glitch out
                        MainLoop.Instance.InvokeFromUIThread(() => {
                            loadedThumbnail?.Dispose();
                            loadedThumbnail = null;
                        });
                    }
                    ImGui.TreePop();
                }
            }
        }
    }

    private unsafe void ShowBundleLoadOrderList()
    {
        var bundles = draggedBundle == null ? bundleManager.AllBundles : bundleManager.AllBundles.ToList();
        if (bundles.Count == 0) {
            ImGui.TextColored(Colors.Info, Lang.Bundles.NoBundlesFound);
        }
        hoveredBundle = null;
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
            var isHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            if (isHovered) {
                hoveredBundle = bundle;
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                    data.SetPersistentData<string>("selectedBundle", bundle.Name);
                }
                var rectA = ImGui.GetItemRectMin();
                var rectB = ImGui.GetItemRectMax();
                ImGui.GetWindowDrawList().AddRectFilled(rectA, rectB, ImGui.ColorConvertFloat4ToU32(*ImGui.GetStyleColorVec4(ImGuiCol.ButtonHovered) with { W = 0.25f }));
            }
            ImGui.Separator();
            ImGui.PopID();
        }

        if (!dragActiveThisFrame) draggedBundle = null;
    }
    private void ShowHierarchyFileTreeActionButtons(HierarchyTreeWidget node, Bundle bundle)
    {
        var entryKey = node.EntryKey ?? "";
        bundle.TryFindResourceByLocalPath(entryKey, out var entry);

        ImGui.PushID(node.EntryKey ?? node.Name);
        using (var _ = ImguiHelpers.Disabled(entry == null)) {

            ShowOpenInEditorButton(node, bundle, entry);
            ImGui.SameLine();
            ShowEditTargetPathButton(entry, bundle);
            ImGui.SameLine();
            using (var __ = ImguiHelpers.Disabled(!(entry?.Diff != null && showDiff != null && entry.Diff is JsonObject odiff && odiff.Count > 1))) {
                if (ImGui.Button($"{AppIcons.SI_FileChanges}")) {
                    showDiff!.Invoke($"{node.EntryKey} => {entry!.Target}", entry.Diff!);
                }
                ImguiHelpers.Tooltip(Lang.Bundles.ShowChangesTooltip.Format(entry?.DiffTime.ToString("O") ?? ""));
            }
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconTertiary);
            if (ImGui.Button($"{AppIcons.SI_GenericDelete2}")) {
                ImGui.OpenPopup(Lang.General.ConfirmTitle);
            }
            ImGui.PopStyleColor();
            ImguiHelpers.Tooltip(Lang.Buttons.DeleteFile);
            if (node.EntryKey != null) {
                AppImguiHelpers.ShowActionModal(Lang.General.ConfirmTitle, $"{AppIcons.SI_GenericDelete2}", Colors.IconTertiary,
                    Lang.Bundles.ConfirmDeleteBundleFile.Format(node.EntryKey, bundle.Name),
                    () => {
                        var filePath = bundleManager.ResolvePathToBundleFile(bundle, entryKey);
                        bundle.RemoveResource(entryKey);

                        if (File.Exists(filePath)) {
                            File.Delete(filePath);
                        } else {
                            Logger.Error($"File not found - could not delete file {filePath}!");
                        }
                        bundle.Save();
                    }
                );
            }
        }
        ImGui.PopID();
    }
    private void ShowOpenInEditorButton(HierarchyTreeWidget node, Bundle bundle, ResourceListItem? entry)
    {
        string? target = entry?.Target;

        using (var _ = ImguiHelpers.Disabled(target == null)) {
            if (ImGui.Button($"{AppIcons.SI_WindowOpenNew}") && openFileCallback != null) {
                var path = bundleManager.ResolvePathToBundleFile(bundle, node.EntryKey!);

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
    private void ShowEditTargetPathButton(ResourceListItem? entry, Bundle bundle)
    {
        string? target = entry?.Target;
        using (var _ = ImguiHelpers.Disabled(target == null)) {
            if (ImGui.Button($"{AppIcons.SI_FileSource}")) {
                ImGui.OpenPopup("EditTargetPath");
            }
            ImguiHelpers.TooltipColored(target ?? "", Colors.Faded);
        }

        ShowEditTargetPathPopup(entry, bundle);
    }
    private void ShowEditTargetPathPopup(ResourceListItem? entry, Bundle bundle)
    {
        if (entry == null) return;

        if (ImGui.BeginPopup("EditTargetPath")) {
            if (ImGui.Button($"{AppIcons.SI_GenericClose}")) {
                ImGui.CloseCurrentPopup();
            }
            ImguiHelpers.Tooltip(Lang.Buttons.Cancel);
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_Save}")) {
                bundle.Save();
                ImGui.CloseCurrentPopup();
            }
            ImguiHelpers.Tooltip(Lang.Buttons.Save);

            var inputLen = Math.Max(ImGui.CalcTextSize(entry.Target).X, ImGui.CalcTextSize(entry.BaseFile ?? "").X) + 15;

            string path = entry.Target;
            ImGui.SeparatorText(Lang.Bundles.EditTargetPathHeader);
            ImGui.SetNextItemWidth(inputLen);
            if (ImGui.InputText("##target", ref path, 512)) {
                var workspace = context.GetWorkspace();
                entry.Target = workspace?.Env.GetTargetPath(path)
                    ?? PathUtils.RemovePlatformPrefix(path).ToString();
            }
            ImGui.TextColored(Colors.Note, Lang.Bundles.EditTargetPath_Description);

            path = entry.BaseFile ?? "";
            ImGui.SeparatorText(Lang.Bundles.BaseFilePath);
            ImGui.SetNextItemWidth(inputLen);
            if (ImGui.InputText("##baseFile", ref path, 512)) {
                var workspace = context.GetWorkspace();
                entry.BaseFile = workspace?.Env.GetTargetPath(path)
                    ?? PathUtils.RemovePlatformPrefix(path).ToString();
            }
            ImGui.TextColored(Colors.Note, Lang.Bundles.BaseFilePath_Description);

            ImGui.EndPopup();
        }
    }

    private void OpenFileFromNode(HierarchyTreeWidget node, Bundle bundle)
    {
        if (node.EntryKey == null || openFileCallback == null) return;

        var path = bundleManager.ResolvePathToBundleFile(bundle, node.EntryKey);
        bundle.TryFindResourceByLocalPath(node.EntryKey, out var entry);

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
            return Lang.FormatDate(utc);
        }
        return utcString;
    }
    public bool RequestClose()
    {
        return false;
    }
}
