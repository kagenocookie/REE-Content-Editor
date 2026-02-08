using System.Numerics;
using System.Reflection;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ContentPatcher;

namespace ContentEditor.App.ImguiHandling;

[ObjectImguiHandler(typeof(Folder), Stateless = true)]
public class FolderDataEditor : IObjectUIHandler
{
    private static readonly MemberInfo[] BaseMembers = [
        typeof(Folder).GetProperty(nameof(Folder.Name))!,
        typeof(Folder).GetField(nameof(Folder.Tags))!,
        typeof(Folder).GetField(nameof(Folder.ScenePath))!,
        typeof(Folder).GetField(nameof(Folder.Update))!,
        typeof(Folder).GetField(nameof(Folder.Draw))!,
        typeof(Folder).GetField(nameof(Folder.Standby))!,
    ];
    private static readonly MemberInfo[] Offset = [
        typeof(Folder).GetProperty(nameof(Folder.Offset))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var folder = context.Get<Folder>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            if (folder.Parent != null) {
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(Folder), members: BaseMembers);
                if (ws != null && RszFieldCache.Folder.UniversalOffset.Exists(ws.Env.Classes.Folder)) {
                    WindowHandlerFactory.SetupObjectUIContext(context, typeof(Folder), members: Offset);
                }
            }
        }

        context.ShowChildrenUI();
    }
}

public class FolderNodeEditor : IObjectUIHandler
{
    private static readonly Vector4 nodeColor = Colors.Folder;

    protected void ShowPrefixes(UIContext context)
    {
        var folder = context.Get<Folder>();
        if (folder.Scene?.RootScene.IsActive != true) return;
        if (!string.IsNullOrEmpty(folder.ScenePath)) {
            var subscene = folder.Scene?.GetChildScene(folder.ScenePath);
            if (subscene != null) {
                if (ImGui.Button((subscene.IsActive ? AppIcons.Eye : AppIcons.EyeBlocked) + "##" + context.label)) {
                    subscene.SetActive(!subscene.IsActive);
                }
            } else {
                ImGui.BeginDisabled();
                ImGui.Button(AppIcons.EyeBlocked + "##" + context.label);
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                    ImGui.SetItemTooltip("Linked scene is not loaded yet, visibility settings unavailable");
                }
            }
        } else {
            var drawSelf = folder.ShouldDrawSelf;
            var drawParentHierarchy = folder.Parent?.ShouldDraw != false;

            if (!drawParentHierarchy) {
                ImGui.BeginDisabled();
                ImGui.Button((drawSelf ? AppIcons.Eye : AppIcons.EyeBlocked) + "##" + context.label);
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                    ImGui.SetItemTooltip("Object is hidden because a parent is already marked hidden");
                }
            } else {
                if (ImGui.Button((drawSelf ? AppIcons.Eye : AppIcons.EyeBlocked) + "##" + context.label)) {
                    folder.ShouldDrawSelf = !drawSelf;
                }
            }
        }
        ImGui.SameLine();
    }

    public void OnIMGUI(UIContext context)
    {
        var folder = context.Get<Folder>();
        var filter = context.FindHandlerInParents<IFilterRoot>();
        if (filter?.HasFilterActive == true) {
            if (filter.MatchedObject == null) {
                NodeEditorUtils.ShowFilteredNode(filter, folder);
                foreach (var go in folder.GameObjects) {
                    NodeEditorUtils.ShowFilteredNode<GameObject>(filter, go);
                }
                return;
            }

            if (filter.MatchedObject == folder) {
                ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                HandleSelect(context, folder);
            } else if (filter.MatchedObject is Folder matchNode) {
                if (folder.IsParentOf(matchNode)) {
                    ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                    context.StateBool = true;
                }
            } else if (filter.MatchedObject is GameObject node2) {
                if (folder.IsParentOf(node2)) {
                    ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                    context.StateBool = true;
                }
            }
        }
        if (!string.IsNullOrEmpty(folder.ScenePath)) {
            HandleLinkedScene(context, folder);
            return;
        }
        if (folder.Children.Count == 0) {
            var subfolder = context.GetChild<SubfolderNodeEditor>();
            if (subfolder != null) {
                context.RemoveChild(subfolder);
            }
        } else {
            var subfolder = context.GetChild<SubfolderNodeEditor>();
            if (subfolder == null) {
                context.ClearChildren();
                context.AddChild("Folders", folder, new SubfolderNodeEditor());
            }
        }

        var gameObjectsCount = 0;
        for (int i = 0; i < context.children.Count; i++) {
            var childCtx = context.children[i];
            if (childCtx.uiHandler is not GameObjectNodeEditor) {
                continue;
            }

            var currentGo = childCtx.Get<GameObject>();
            var expectedGo = gameObjectsCount >= folder.GameObjects.Count ? null : folder.GameObjects[gameObjectsCount];
            if (expectedGo == null) {
                context.children.RemoveAtAfter(i);
                break;
            }

            if (expectedGo != currentGo) {
                context.children.RemoveAtAfter(i);
                context.AddChild(currentGo.Name, currentGo, new GameObjectNodeEditor());
            }

            gameObjectsCount++;
        }
        for (int i = gameObjectsCount; i < folder.GameObjects.Count; ++i) {
            var go = folder.GameObjects[i];
            context.AddChild(go.Name + "##" + i, go, new GameObjectNodeEditor());
        }

        var showChildren = context.StateBool;
        ImGui.PushStyleColor(ImGuiCol.Text, nodeColor);
        if (folder.Parent != null) {
            ShowPrefixes(context);
            if (context.children.Count == 0 && folder.Children.Count == 0) {
                // ImGui.Button(context.label);
            } else if (!context.StateBool) {
                if (ImGui.ArrowButton($"arrow##{context.label}", ImGuiDir.Right)) {
                    showChildren = context.StateBool = true;
                }
                ImGui.SameLine();
            } else {
                if (ImGui.ArrowButton($"arrow##{context.label}", ImGuiDir.Down)) {
                    showChildren = context.StateBool = false;
                }
                ImGui.SameLine();
            }
            var inspector = context.FindHandlerInParents<IInspectorController>();
            AppImguiHelpers.PrependIcon(folder);
            if (ImGui.Selectable(context.label, folder == inspector?.PrimaryTarget)) {
                HandleSelect(context, folder);
            }
        }
        if (ImGui.BeginPopupContextItem(context.label)) {
            HandleContextMenu(folder, context);
            ImGui.EndPopup();
        }
        ImGui.PopStyleColor();
        var indent = folder.Parent == null ? 2 : ImGui.GetStyle().IndentSpacing;
        if (showChildren || folder.Parent == null) {
            ImGui.Indent(indent);
            ShowChildren(context, folder);
            ImGui.Unindent(indent);
        }
    }

    private void HandleLinkedScene(UIContext context, Folder folder)
    {
        var showChildren = context.StateBool;
        ImGui.PushStyleColor(ImGuiCol.Text, nodeColor);
        ImguiHelpers.BeginRect();
        ShowPrefixes(context);
        ImGui.BeginGroup();
        if (!context.StateBool) {
            if (ImGui.ArrowButton($"arrow##{context.label}", ImGuiDir.Right)) {
                showChildren = context.StateBool = true;
            }
            ImGui.SameLine();
        } else {
            if (ImGui.ArrowButton($"arrow##{context.label}", ImGuiDir.Down)) {
                showChildren = context.StateBool = false;
            }
            ImGui.SameLine();
        }

        var inspector = context.FindHandlerInParents<IInspectorController>();
        AppImguiHelpers.PrependIcon(folder);
        if (ImGui.Selectable(context.label, folder == inspector?.PrimaryTarget)) {
            HandleSelect(context, folder);
        }
        ImGui.SameLine();
        ImGui.TextColored(Colors.Faded, folder.ScenePath);
        ImGui.EndGroup();

        if (ImGui.BeginPopupContextItem(context.label)) {
            HandleContextMenu(folder, context);
            ImGui.EndPopup();
        }
        ImGui.PopStyleColor();
        if (context.children.Count == 0 && showChildren) {
            OpenLinkedScene(context, folder);
        }

        var indent = folder.Parent == null ? 2 : ImGui.GetStyle().IndentSpacing;
        if (showChildren || folder.Parent == null) {
            ImGui.Indent(indent);
            context.ShowChildrenUI();
            ImGui.Unindent(indent);
        }
        ImguiHelpers.EndRect(4);
        ImGui.Spacing();
    }

    private static void OpenLinkedScene(UIContext context, Folder folder)
    {
        if (string.IsNullOrEmpty(folder.ScenePath)) return;
        var ws = context.GetWorkspace();
        if (ws == null) {
            Logger.Error("No active workspace for opening linked scene");
            EditorWindow.CurrentWindow?.AddSubwindow(new ErrorModal("Linked scene open failed", "Workspace not found"));
            return;
        }

        // note: folder.ChildScene can stay null for a few frames (at least 1), which is why we need to display a status until it's fully loaded
        if (ws.ResourceManager.TryResolveGameFile(folder.ScenePath, out var file)) {
            if (folder.ChildScene == null) {
                folder.RequestLoad();
                ImGui.TextColored(Colors.Info, $"Loading linked scene {folder.ScenePath}...");
                return;
            }

            var parentSceneEditor = context.FindHandlerInParents<SceneEditor>();
            WindowData.CreateEmbeddedWindow(context, context.GetWindow()!, new SceneEditor(ws, file, parentSceneEditor), "LinkedScene");
        } else {
            ImGui.TextColored(Colors.Error, "Linked scene file not found: " + folder.ScenePath);
        }
    }

    protected void ShowChildren(UIContext context, Folder node)
    {
        var offset = 0;
        foreach (var child in context.children) {
            if (child.uiHandler is GameObjectNodeEditor) {
                break;
            }

            offset++;
            child.ShowUI();
        }

        for (int i = 0; i < node.GameObjects.Count; i++) {
            var child = node.GameObjects[i];
            while (i + offset >= context.children.Count || context.children[i + offset].target != child) {
                context.children.RemoveAtAfter(i + offset);
                context.AddChild(child.Name + "##" + context.children.Count, child, new GameObjectNodeEditor());
            }
            var childCtx = context.children[i + offset];
            var isNameMismatch = !childCtx.label.StartsWith(child.Name) || !childCtx.label.AsSpan().Slice(child.Name.Length, 2).SequenceEqual("##");
            if (isNameMismatch) {
                childCtx.label = child.Name + "##" + i;
            }
            ImGui.PushID(childCtx.label);
            childCtx.ShowUI();
            ImGui.PopID();
        }
    }

    private static void HandleSelect(UIContext context, Folder folder)
    {
        if (folder.Parent == null) return;
        context.FindHandlerInParents<IInspectorController>()?.SetPrimaryInspector(folder);
    }

    private static void HandleContextMenu(Folder node, UIContext context)
    {
        if (node.Scene?.IsActive == true && (node.ChildScene != null || string.IsNullOrEmpty(node.ScenePath)) && ImGui.Selectable("Focus in 3D view")) {
            var focusFolder = node.ChildScene?.RootFolder ?? node;
            node.Scene.ActiveCamera.LookAt(focusFolder, false);
            ImGui.CloseCurrentPopup();
        }
        if (node.Scene?.IsActive == true) {
            if (ImGui.MenuItem("Toggle children Visibility")) {
                var visible = node.Children.FirstOrDefault()?.ShouldDrawSelf ?? node.GameObjects.FirstOrDefault()?.ShouldDrawSelf ?? false;
                foreach (var child in node.Children) child.ShouldDrawSelf = !visible;
                foreach (var child in node.GameObjects) child.ShouldDrawSelf = !visible;
            }
        }

        if (string.IsNullOrEmpty(node.ScenePath) && ImGui.Selectable("New GameObject")) {
            var ws = context.GetWorkspace();
            var newgo = new GameObject("New_GameObject", ws!.Env, node, node.Scene);
            UndoRedo.RecordListAdd(context, node.GameObjects, newgo);
            newgo.MakeNameUnique();
            context.FindHandlerInParents<IInspectorController>()?.SetPrimaryInspector(newgo);
            ImGui.CloseCurrentPopup();
        }
        if (string.IsNullOrEmpty(node.ScenePath) && ImGui.Selectable("New folder")) {
            var ws = context.GetWorkspace();
            var newFolder = new Folder("New_Folder", ws!.Env, node.Scene);
            UndoRedo.RecordAddChild(context, newFolder, node);
            newFolder.MakeNameUnique();
            context.FindHandlerInParents<IInspectorController>()?.SetPrimaryInspector(newFolder);
            context.GetChild<SubfolderNodeEditor>()?.ClearChildren();
            ImGui.CloseCurrentPopup();
        }
        if (node.Parent != null) {
            if (ImGui.Selectable("Delete")) {
                UndoRedo.RecordRemoveChild(context, node);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Selectable("Duplicate")) {
                var clone = node.Clone();
                UndoRedo.RecordAddChild(context, clone, node.Parent, node.Parent.GetChildIndex(node) + 1);
                clone.MakeNameUnique();
                var inspector = context.FindHandlerInParents<IInspectorController>();
                inspector?.SetPrimaryInspector(clone);
                ImGui.CloseCurrentPopup();
            }
        }
        if (node.ChildScene?.Folders.Any() == true) {
            if (ImGui.BeginMenu("Load subfolders")) {
                var loadType = (Scene.LoadType?)null;
                if (ImGui.Selectable("Direct children")) loadType = Scene.LoadType.Default;
                if (ImGui.Selectable("Direct preloaded children")) loadType = Scene.LoadType.PreloadedOnly;
                if (ImGui.Selectable("All children")) loadType = Scene.LoadType.LoadChildren|Scene.LoadType.IncludeNested;
                if (ImGui.Selectable("All preloaded children")) loadType = Scene.LoadType.PreloadedOnly|Scene.LoadType.LoadChildren|Scene.LoadType.IncludeNested;
                if (loadType != null) {
                    try {
                        foreach (var subfolder in node.ChildScene.Folders) {
                            if (subfolder.ChildScene != null) continue;
                            if ((loadType.Value & Scene.LoadType.PreloadedOnly) != 0 && !subfolder.Standby) continue;

                            subfolder.RequestLoad(loadType.Value);
                        }
                    } catch (Exception e) {
                        Logger.Error(e, "Failed to load child scenes");
                    }
                }
                ImGui.EndMenu();
            }
        }
    }
}

public class SubfolderNodeEditor : NodeTreeEditor<Folder, FolderNodeEditor>
{
    public SubfolderNodeEditor()
    {
        nodeColor = Colors.Folder;
        EnableContextMenu = false;
        UseContextLabel = true;
        UnnestChildren = true;
    }

    protected override void HandleSelect(UIContext context, Folder node)
    {
        if (node.Parent == null) return;
        base.HandleSelect(context, node);
    }
}
