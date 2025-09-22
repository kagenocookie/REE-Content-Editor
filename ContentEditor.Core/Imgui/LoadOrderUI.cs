using ImGuiNET;

namespace ContentEditor.Core;

public class LoadOrderUI : IWindowHandler
{
    private BundleManager bundleManager;

    public LoadOrderUI(BundleManager workspace)
    {
        this.bundleManager = workspace;
    }

    public string HandlerName => "Bundle load order";
    public int FixedID => -10000;

    public bool HasUnsavedChanges => false;

    private Bundle? draggedBundle;
    protected UIContext context = null!;

    public void Init(UIContext context)
    {
        this.context = context;
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    public void OnIMGUI()
    {
        if (!bundleManager.IsLoaded) {
            bundleManager.LoadDataBundles();
        }

        var bundles = draggedBundle == null ? bundleManager.AllBundles : bundleManager.AllBundles.ToList();
        if (bundles.Count == 0) {
            ImGui.TextColored(Colors.Info, "No bundles detected");
        }

        foreach (var bundle in bundles) {
            var active = bundleManager.IsBundleActive(bundle);

            if (ImGui.Checkbox(bundle.Name, ref active)) {
                bundleManager.SetBundleActive(bundle, active);
                bundleManager.SaveSettings();
            }

            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.PayloadNoCrossContext|ImGuiDragDropFlags.PayloadNoCrossProcess)) {
                ImGui.SetDragDropPayload("BUNDLE", IntPtr.Zero, 0);
                draggedBundle = bundle;
                ImGui.Text(bundle.Name);
                ImGui.EndDragDropSource();
            }

            if (draggedBundle != null && ImGui.BeginDragDropTarget()) {
                if (draggedBundle != bundle) {
                    bundleManager.SwapBundleOrders(draggedBundle, bundle);
                    bundleManager.SaveSettings();
                }

                ImGui.EndDragDropTarget();
            }
        }
    }

    public bool RequestClose()
    {
        return false;
    }
}
