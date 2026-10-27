using ContentEditor.Core;
using ContentPatcher;
using ReeLib;

namespace ContentEditor.App.ImguiHandling;

[ObjectImguiHandler(typeof(MeshComponent), Stateless = true, Priority = 0)]
public class MeshComponentHandler : BaseComponentEditor, IUIContextEventHandler, IObjectUIHandler
{
    void IObjectUIHandler.OnIMGUI(UIContext context)
    {
        if (context.children.Count == 0) {
            var content = SetupDefaultUI(context);
            var meshCtx = content.children.FirstOrDefault(c => c.uiHandler is ResourcePathPicker rp && rp.FileFormats.Contains(KnownFileFormats.Mesh));
            var matCtx = content.children.FirstOrDefault(c => c.uiHandler is ResourcePathPicker rp && rp.FileFormats.Contains(KnownFileFormats.MeshMaterial));
            if (meshCtx != null && matCtx != null) {
                var errorNoteHandler = new DynamicLabelHandler(
                    color: Colors.Warning,
                    textFunc: c => {
                        var meshPath = meshCtx.Get<string>();
                        var matPath = matCtx.Get<string>();
                        var ws = c.GetWorkspace();
                        if (ws != null && !string.IsNullOrEmpty(meshPath) && !string.IsNullOrEmpty(matPath)) {
                            if (ws.ResourceManager.TryResolveGameFile(meshPath, out var mesh) && ws.ResourceManager.TryResolveGameFile(matPath, out var mat)) {
                                var meshMats = mesh.GetFile<MeshFile>().MaterialNames.Count;
                                var mats = mat.GetFile<MdfFile>().Materials.Count;
                                if (meshMats != mats) {
                                    return $"There is a mismatch between the mesh and mdf2 material counts (mesh: {meshMats}, mdf2: {mats} materials).\nThe mesh will appear as checkerboards ingame.";
                                }
                            }
                        }
                        return null;
                    }
                );
                content.AddChild("__errorNote", null, errorNoteHandler).MoveAfter(matCtx);
            }
        }

        context.ShowChildrenUI();
    }

    public bool HandleEvent(UIContext context, EditorUIEvent eventData)
    {
        if (eventData.type is UIContextEvent.Changed or UIContextEvent.Reverted or UIContextEvent.Updated) {
            // we could be explicit about which fields to check (mesh, material, enableParts)
            // but we may in the future also add material parameters or other fields
            // may as well just refresh on any change
            context.Get<MeshComponent>().RefreshIfActive();
        }
        return true;
    }
}

[ObjectImguiHandler(typeof(CompositeMesh), Stateless = true, Priority = 0)]
public class CompositeMeshComponentHandler : BaseComponentEditor, IUIContextEventHandler
{
    public bool HandleEvent(UIContext context, EditorUIEvent eventData)
    {
        if (eventData.type is UIContextEvent.Changed or UIContextEvent.Reverted or UIContextEvent.Updated) {
            context.Get<CompositeMesh>().ReloadMeshes();
        }
        return true;
    }
}

[RszClassHandler("via.render.CompositeMeshInstanceGroup")]
public class CompositeMeshInstanceGroupHandler : NestedRszInstanceHandler
{
    public CompositeMeshInstanceGroupHandler() : base("via.render.CompositeMeshInstanceGroup")
    {
    }

    protected override bool ShowTree(UIContext context, RszInstance instance)
    {
        var component = context.FindValueInParentValues<CompositeMesh>();
        if (component != null && component.Scene?.IsActive == true) {
            var isFocused = component.focusedGroup == instance;
            if (ImguiHelpers.ToggleButton(isFocused ? $"{AppIcons.Star}" : $"{AppIcons.StarEmpty}", ref isFocused, Colors.IconActive)) {
                if (isFocused) {
                    component.focusedGroup = instance;
                } else {
                    component.focusedGroup = null;
                }
                component.focusedGroupElementIndex = -1;
            }
            ImguiHelpers.Tooltip("Focus on this mesh group gizmo");
            ImGui.SameLine();
            if (component.focusedGroup == instance) {
                var count = instance.Get(RszFieldCache.CompositeMesh.InstanceGroup.Transforms).Count;
                ImGui.SetNextItemWidth(UI.FontSize * 3 + ImGui.GetFrameHeight());
                if (ImGui.BeginCombo("##Focused item", component.focusedGroupElementIndex.ToString())) {
                    for (int i = 0; i < count; i++) {
                        if (ImGui.Selectable(i.ToString(), i == component.focusedGroupElementIndex)) {
                            component.focusedGroupElementIndex = i;
                        }
                    }
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
            }
        }
        return base.ShowTree(context, instance);
    }
}
