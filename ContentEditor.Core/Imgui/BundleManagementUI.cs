using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace ContentEditor.Core;

public class BundleManagementUI : IWindowHandler
{
    private BundleManager bundleManager;

    public BundleManagementUI(BundleManager workspace)
    {
        this.bundleManager = workspace;
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
            var filter = data.GetPersistentData<string>("bundleFilter");
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

        if (ImGui.TreeNode("Entities")) {
            var types = allOption.Concat(bundle.Entities.Select(e => e.Type).Distinct()).ToArray();
            ImGui.Combo("Type", ref selectedEntityType, types, types.Length);
            var filter = selectedEntityType > 0 && selectedEntityType < types.Length ? types[selectedEntityType] : null;
            foreach (var e in bundle.Entities) {
                if (filter != null && e.Type != filter) continue;
                ImGui.Text($"{e.Type} | {e.Id} : {e.Label}");
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
