using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using ImGuiNET;

namespace ContentEditor.Core;

public class BundleManagementUI : IWindowHandler
{
    private BundleManager bundleManager;
    private readonly string? preselectBundle;
    private readonly Action<string>? openFileCallback;
    private readonly Action<string, JsonNode>? showDiff;

    public BundleManagementUI(BundleManager workspace, string? preselectBundle, Action<string>? openFileCallback, Action<string, JsonNode>? showDiff)
    {
        this.bundleManager = workspace;
        this.preselectBundle = preselectBundle;
        this.openFileCallback = openFileCallback;
        this.showDiff = showDiff;
    }

    public string HandlerName => "Bundle manager";
    public int FixedID => -10001;

    public bool HasUnsavedChanges => false;
    private string newBundleName = "";

    private WindowData data = null!;
    protected UIContext context = null!;

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

        if (ImGui.TreeNode("New bundle...")) {
            ImGui.InputText("Bundle name", ref newBundleName, 100);
            if (!string.IsNullOrEmpty(newBundleName) && ImGui.Button("Create")) {
                if (bundleManager.CreateBundle(newBundleName) != null) {
                    data.SetPersistentData("selectedBundle", newBundleName);
                    newBundleName = "";
                } else {
                    WindowManager.Instance.ShowError("Bundle already exists!", data);
                }
            }
            ImGui.TreePop();
        }

        ImGui.Separator();

        var selectedName = data.GetPersistentData<string>("selectedBundle");
        if (bundleManager.AllBundles.Count == 0) {
            ImGui.TextColored(Colors.Info, "No bundles found");
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

        var str = bundle.Author ?? "";
        if (ImGui.InputText("Author", ref str, 100)) {
            bundle.Author = str;
        }
        str = bundle.Description ?? "";
        var w = ImGui.CalcItemWidth();
        if (ImGui.InputTextMultiline("Description", ref str, 1024, new Vector2(w, 120))) {
            bundle.Description = str;
        }
        ImGui.Text($"Created at: {bundle.CreatedAt}");
        ImGui.Text($"Updated at: {bundle.UpdatedAt}");

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

        if (ImGui.Button("Open folder in explorer")) {
            FileSystemUtils.ShowFileInExplorer(bundleManager.ResolveBundleLocalPath(bundle, ""));
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

    private int selectedLegacyEntityType = 0;
    private int selectedEntityType = 0;
    private static readonly string[] allOption = ["All"];

    public bool RequestClose()
    {
        return false;
    }
}
