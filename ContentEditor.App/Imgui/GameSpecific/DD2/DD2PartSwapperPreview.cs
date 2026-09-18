using System.Numerics;
using ContentEditor.App.Graphics;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentPatcher;
using ContentPatcher.DD2;
using ReeLib;

namespace ContentEditor.App.DD2;

[ObjectImguiHandler(typeof(PlaceholderResource<PartSwapperPreview>))]
public sealed class DD2PartSwapperPreview : IObjectUIHandler
{
    private string? selectedSubtype;
    private string? swapDataJson;

    public void OnIMGUI(UIContext context)
    {
        var entity = context.GetOwnerEntity();
        var field = context.GetEntityField<PartSwapperPreview>()!;
        var workspace = context.GetWorkspace();
        if (entity == null || workspace == null) {
            ImGui.TextColored(Colors.Error, $"{field} field requires a valid item entity and workspace");
            return;
        }

        if (!ImGui.TreeNode("3D Preview"u8)) {
            return;
        }
        ImGui.BeginChild("##PartPreview"u8);
        var sceneView = context.GetChildHandler<EmbeddedWindowHandler>()?.Window as SceneView;
        var isInit = sceneView == null;
        if (sceneView == null) {
            // TODO move embedded 3D scene setup to reusable helper method?
            var scene = EditorWindow.CurrentWindow!.SceneManager.CreateScene($"PartPreview{context.GetHashCode()}", "", true);
            scene.Type = SceneType.Independent;
            scene.Root.Controller.Keyboard = EditorWindow.CurrentWindow.LastKeyboard;
            scene.Root.Controller.MoveSpeed = AppConfig.Settings.MeshViewer.MoveSpeed;
            scene.OwnRenderContext.AddDefaultSceneGizmos();
            scene.AddWidget<SceneVisibilitySettings>();

            sceneView = new SceneView(workspace, scene);
            var scnWnd = WindowData.CreateEmbeddedWindow(context, context.GetWindow()!, sceneView, "MeshPreview");

            var go = new GameObject("Preview", workspace.Env);
            scene.Add(go);
            var swp = go.AddComponent("app.PartSwapper");
            // default values currently only work on the immediate instance so re-create the _Meta field here
            swp.Data.SetFieldValue("_Meta", workspace.CreateRszInstance("app.CharacterEditDefine.MetaData"));
        }
        var partSwapper = sceneView.Scene.Find("Preview")?.GetComponent<PartSwapper>();
        if (partSwapper != null) {
            if (isInit) {
                context.AddChild("Preview Parameters", partSwapper, new RszInstanceHandler(), getter: c => c!.Data.GetFieldValue("_Meta"));
            }
            var fieldData = entity.Get("data");
            var data = fieldData;
            if (data is GroupedResource grp) {
                selectedSubtype ??= grp.Resources.FirstOrDefault().Key;

                data = selectedSubtype == null ? null : grp.Get(selectedSubtype);
            }
            if (data is IPropertyContainer propData) {
                var prevValue = partSwapper.Data.GetNestedFieldValue(field.PartField);
                var curValue = propData.Get(field.DataField)!;
                var newJson = fieldData?.ToJson(workspace.Env).ToJsonString();
                if (newJson != swapDataJson) {
                    swapDataJson = newJson;
                    partSwapper.ResetCache(false);
                }
                if (prevValue?.Equals(curValue) != true) {
                    partSwapper.Data.SetNestedFieldValue(field.PartField, curValue);
                    partSwapper.ResetCache(isInit);
                }
            }
            if (isInit) {
                sceneView.Scene.ActiveCamera.LookAt(partSwapper.GameObject, true);
            }
        }
        var w = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().WindowPadding.X * 2;
        ImGui.BeginChild("##scene", new Vector2(w * 0.6f, 0));
        sceneView.OnIMGUI();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("##settings", new Vector2(w * 0.4f, 0));
        context.ShowChildrenUI(1, 2);
        ImGui.EndChild();

        ImGui.EndChild();
        ImGui.TreePop();
    }
}
