using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ReeLib;

namespace ContentEditor.App;

public class LoadOrderUI : IWindowHandler
{
    private BundleManager bundleManager;

    public LoadOrderUI(BundleManager workspace)
    {
        this.bundleManager = workspace;
    }

    public string HandlerName => "Bundle Load Order";
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
        ShowBundleLoadOrderToolbar();
        ImGui.Spacing();
        ImGui.SeparatorText("Load Order");
        ImGui.Spacing();
        ShowBundleLoadOrderList();
    }
    private void ShowBundleLoadOrderToolbar()
    {
        if (ImGui.Button("Apply patches (loose file)")) {
            EditorWindow.CurrentWindow?.ApplyContentPatches(null);
        }
        ImGui.SameLine();
        if (ImGui.Button("Apply patches (PAK)")) {
            EditorWindow.CurrentWindow?.ApplyContentPatches("pak");
        }
        ImGui.SameLine();
        if (ImGui.Button("Patch to ...")) {
            PlatformUtils.ShowFolderDialog((path) => EditorWindow.CurrentWindow?.ApplyContentPatches(path), EditorWindow.CurrentWindow?.Workspace.Env.Config.GamePath);
        }
        ImGui.SameLine();
        if (ImGui.Button("Revert patches")) {
            EditorWindow.CurrentWindow?.RevertContentPatches();
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
            ImGui.Button($"{AppIcons.SI_GenericDragDropHandle}");
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
    public bool RequestClose()
    {
        return false;
    }
}
