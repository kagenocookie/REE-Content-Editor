using System.Numerics;
using ContentEditor.Core;

namespace ContentEditor.App.ImguiHandling;

public class SceneTreeEditor : TreeHandler<IVisibilityTarget>
{
    private Dictionary<Scene, SceneEditor> _nestedScenes = new();

    private SceneEditor? GetRootEditor(UIContext context) => context.FindHandlerInParents<SceneEditor>()?.RootSceneEditor;

    protected override IEnumerable<IVisibilityTarget> GetChildren(IVisibilityTarget? node)
    {
        if (node is GameObject go) return go.Children;
        if (node is Folder fo) {
            if (!string.IsNullOrEmpty(fo.ScenePath)) {
                if (fo.ChildScene == null) {
                    fo.RequestLoad();
                    ImGui.TextColored(Colors.Info, "Loading scene ...");
                    return [];
                }

                return [];
            }

            return ((IEnumerable<IVisibilityTarget>)fo.Children).Concat(fo.GameObjects);
        }
        return [];
    }

    protected override bool IsExpandable(IVisibilityTarget node)
    {
        if (node is Folder fo && !string.IsNullOrEmpty(fo.ScenePath)) {
            return true;
        }

        return GetChildren(node).Any();
    }

    protected override IEnumerable<IVisibilityTarget> GetRootChildren(UIContext context)
    {
        return GetChildren(context.GetRaw() as IVisibilityTarget);
    }

    protected override void SetupNodeItemContext(UIContext context, IVisibilityTarget node)
    {
        if (node is Folder folder && folder.ChildScene != null) {
            var ws = context.GetWorkspace()!;
            if (ws.ResourceManager.TryResolveGameFile(folder.ScenePath!, out var file)) {
                var parentSceneEditor = context.FindHandlerInParents<SceneEditor>();
                WindowData.CreateEmbeddedWindow(context, context.GetWindow()!, new SceneEditor(ws, file, parentSceneEditor), "LinkedScene");
            } else {
                ImGui.TextColored(Colors.Error, "Linked scene file not found: " + folder.ScenePath);
            }
        }
    }

    protected override IEnumerable<IVisibilityTarget> GetSelectedItemHierarchy(UIContext context)
    {
        var target = GetRootEditor(context)?.PrimaryTarget;
        if (target == null) yield break;

        if (target is GameObject go) {
            yield return go;

            target = go.Folder;
            go = go.Parent!;
            while (go != null) {
                yield return go;
                go = go.Parent!;
            }
        }

        if (target is Folder folder) {
            yield return folder;

            folder = folder.Parent!;
            while (folder != null) {
                yield return folder;
                folder = folder.Parent!;
            }
        }
    }

    protected override void ShowNode(IVisibilityTarget node, UIContext context)
    {
        // ideally: find highest priority component and use that type's icon + GameObject/prefab overlay icon
        var rootEditor = GetRootEditor(context);
        var folder = node as Folder;
        var scene = (node as IVisibilityTarget)?.Scene;

        var startPos = ImGui.GetCursorScreenPos();
        string label = GetNodeText(node);

        bool click, middleClick;

        if (!string.IsNullOrEmpty(folder?.ScenePath)) {
            ImGui.BeginGroup();
            click = ImGui.Selectable(label);
            ImGui.SameLine();
            ImGui.TextColored(Colors.Faded, folder.ScenePath);
            ImGui.EndGroup();
        } else {
            click = ImGui.Selectable(label);
        }
        middleClick = click && ImGui.IsKeyDown(ImGuiKey.ModCtrl) || !click && ImGui.IsItemClicked(ImGuiMouseButton.Middle);

        if (ImGui.BeginPopupContextItem(label)) {
            if (scene?.RootScene.IsActive == true) {
                if (ImGui.Selectable($"{AppIcons.Search} Focus in 3D view")) {
                    if (node is Folder f) scene?.ActiveCamera.LookAt(f, false);
                    else if (node is GameObject go) scene?.ActiveCamera.LookAt(go, false);
                }
            }

            if (scene?.IsActive == true) {
                if (ImGui.MenuItem($"{AppIcons.Eye} Toggle children Visibility")) {
                    var visible = node.VisibilityChildren.FirstOrDefault()?.ShouldDrawSelf == true;
                    foreach (var child in node.VisibilityChildren) child.ShouldDrawSelf = !visible;
                }

                ShowVisibilityBisect(node, context);
            }

            if ((node is GameObject || node is Folder && string.IsNullOrEmpty(((Folder)node).ScenePath)) && ImGui.Selectable($"{AppIcons.SI_SceneGameObject} New GameObject")) {
                var ws = context.GetWorkspace();
                var newgo = new GameObject("New_GameObject", ws!.Env, folder ?? (node as GameObject)?.Folder, scene);
                UndoRedo.RecordAddChild(context, newgo, (INodeObject<GameObject>)node);
                newgo.MakeNameUnique();
                rootEditor?.SetPrimaryInspector(newgo);
            }

            if (node is GameObject gameObject) {
                if (ImGui.Selectable($"{AppIcons.SI_Copy} Copy GameObject")) {
                    VirtualClipboard.CopyToClipboard(gameObject.Clone());
                    ImGui.CloseCurrentPopup();
                }
                if (VirtualClipboard.TryGetFromClipboard<GameObject>(out var clipboardObject) && ImGui.Selectable($"{AppIcons.SI_Paste} Paste as child")) {
                    var clone = clipboardObject.Clone();
                    UndoRedo.RecordAddChild<GameObject>(context, clone, gameObject);
                    clone.MakeNameUnique();
                    clone.Transform.ResetLocalTransform();
                    context.ClearChildren();
                }

                var parent = ((INodeObject<GameObject>)gameObject).GetParent();
                if (parent == null) {
                    // the sole root instance mustn't be deleted or duplicated (pfb)
                    return;
                }

                if (ImGui.Selectable($"{AppIcons.SI_FileExtractTo} Duplicate")) {
                    var clone = gameObject.Clone();
                    UndoRedo.RecordAddChild<GameObject>(context, clone, parent, parent.GetChildIndex(gameObject) + 1);
                    clone.MakeNameUnique();
                    rootEditor?.SetPrimaryInspector(clone);
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.Selectable($"{AppIcons.SI_GenericDelete} Delete")) {
                    UndoRedo.RecordRemoveChild(context, gameObject);
                    ImGui.CloseCurrentPopup();
                }
            } else if (folder != null) {
                //
                // if (string.IsNullOrEmpty(node.ScenePath) && ImGui.Selectable("New GameObject")) {
                //     var ws = context.GetWorkspace();
                //     var newgo = new GameObject("New_GameObject", ws!.Env, node, node.Scene);
                //     UndoRedo.RecordListAdd(context, node.GameObjects, newgo);
                //     newgo.MakeNameUnique();
                //     GetRootEditor(context)?.SetPrimaryInspector(newgo);
                //     ImGui.CloseCurrentPopup();
                // }
                if (string.IsNullOrEmpty(folder.ScenePath) && ImGui.Selectable($"{AppIcons.SI_Folder} New folder")) {
                    var ws = context.GetWorkspace();
                    var newFolder = new Folder("New_Folder", ws!.Env, scene);
                    UndoRedo.RecordAddChild(context, newFolder, folder);
                    newFolder.MakeNameUnique();
                    GetRootEditor(context)?.SetPrimaryInspector(newFolder);
                }

                if (folder.Parent != null) {
                    // TODO need proper icon
                    if (ImGui.Selectable($"{AppIcons.SI_FileExtractTo} Duplicate")) {
                        var clone = folder.Clone();
                        UndoRedo.RecordAddChild(context, clone, folder.Parent, folder.Parent.GetChildIndex(folder) + 1);
                        clone.MakeNameUnique();
                        rootEditor?.SetPrimaryInspector(clone);
                        ImGui.CloseCurrentPopup();
                    }
                    if (ImGui.Selectable($"{AppIcons.SI_GenericDelete} Delete")) {
                        UndoRedo.RecordRemoveChild(context, folder);
                        ImGui.CloseCurrentPopup();
                    }
                }

                if (folder.ChildScene?.Folders.Any() == true) {
                    if (ImGui.BeginMenu($"{AppIcons.List} Load subfolders")) {
                        var loadType = (Scene.LoadType?)null;
                        if (ImGui.Selectable("Direct children")) loadType = Scene.LoadType.Default;
                        if (ImGui.Selectable("Direct preloaded children")) loadType = Scene.LoadType.PreloadedOnly;
                        if (ImGui.Selectable("All children")) loadType = Scene.LoadType.LoadChildren|Scene.LoadType.IncludeNested;
                        if (ImGui.Selectable("All preloaded children")) loadType = Scene.LoadType.PreloadedOnly|Scene.LoadType.LoadChildren|Scene.LoadType.IncludeNested;
                        if (loadType != null) {
                            try {
                                foreach (var subfolder in folder.ChildScene.Folders) {
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
            ImGui.EndPopup();
        }

        if (middleClick) {
            rootEditor?.AddInspector(node);
        } else if (click) {
            rootEditor?.SetPrimaryInspector(node);
        }

        if (folder?.ChildScene != null && context.StateBool) {
            if (!_nestedScenes.TryGetValue(folder.ChildScene, out var sceneEdit)) {
                SetupNodeItemContext(context, node);
                _nestedScenes[folder.ChildScene] = sceneEdit = (SceneEditor)context.children[0].uiHandler!;
                sceneEdit.EnsureUIInit();
                sceneEdit.Tree?.InheritedDepth = CurrentIndent + 1;
                context.children.Clear();
            }

            ImGui.Spacing();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (sceneEdit.Tree?.InheritedDepth ?? 0) * 20);
            sceneEdit.OnIMGUI();
            ImGui.Spacing();
        }
        if (!string.IsNullOrEmpty(folder?.ScenePath)) {
            var min = new Vector2(ImGui.GetWindowPos().X + ImGui.GetStyle().WindowPadding.X, startPos.Y);
            var max = new Vector2(ImGui.GetWindowPos().X + ImGui.GetContentRegionAvail().X, ImGui.GetItemRectMax().Y) + ImGui.GetStyle().FramePadding;
            ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(ImGuiCol.Border), ImGui.GetStyle().FrameRounding, ImDrawFlags.RoundCornersAll, 1.0f);
        }
    }

    protected override void ShowPrefixColumn(IVisibilityTarget vis, UIContext context)
    {
        if (vis.Scene?.RootScene.IsActive != true) return;

        var drawSelf = vis.ShouldDrawSelf;
        if (vis.Parent != null && !vis.Parent.ShouldDraw) {
            ImGui.BeginDisabled();
            ImGui.Button((drawSelf ? AppIcons.Eye : AppIcons.EyeBlocked) + "##" + context.label);
            ImGui.EndDisabled();
        } else {
            if (ImGui.Button((drawSelf ? AppIcons.Eye : AppIcons.EyeBlocked) + "##" + context.label)) {
                vis.ShouldDrawSelf = !drawSelf;
            }
        }

        ImGui.SameLine();
    }

    protected override void DrawEndTree(UIContext context)
    {
        if (context.TryCast<Folder>(out var folder)) {
            if (ImGui.Button($"{AppIcons.SI_GenericAdd} Add Folder")) {
                var ws = context.GetWorkspace();
                var newFolder = new Folder("New_Folder", ws!.Env, folder.Scene);
                UndoRedo.RecordAddChild(context, newFolder, folder);
                newFolder.MakeNameUnique();
                GetRootEditor(context)?.SetPrimaryInspector(newFolder);
            }
            ImGui.SameLine();
            if (ImGui.Button($"{AppIcons.SI_GenericAdd} Add GameObject")) {
                var ws = context.GetWorkspace();
                var newgo = new GameObject("New_GameObject", ws!.Env, folder, folder.Scene);
                UndoRedo.RecordAddChild(context, newgo, (INodeObject<GameObject>)folder);
                newgo.MakeNameUnique();
                GetRootEditor(context)?.SetPrimaryInspector(newgo);
            }
        }
    }

    private static Dictionary<IVisibilityTarget, (List<IVisibilityTarget> rejects, List<IVisibilityTarget> pending)> bisectTemp = new();

    private void ShowVisibilityBisect(IVisibilityTarget node, UIContext context)
    {
        if (!GetChildren(node).Skip(1).Any()) return;

        // temporary (?) workaround for not having a clickable 3D view
        if (ImGui.BeginMenu($"{AppIcons.Search} Bisect find")) {
            if (bisectTemp.Count == 0 || bisectTemp.Keys.First() != node) {
                bisectTemp.Clear();
                bisectTemp[node] = (new(), GetChildren(node).Cast<IVisibilityTarget>().Where(c => c.ShouldDrawSelf).ToList());
            }

            ImGui.Text("Remaining objects: " + bisectTemp[node].pending.Count);
            ImGui.SameLine();
            ImGui.Button("?"u8);
            if (ImGui.IsItemHovered()) {
                ImGui.SetItemTooltip("""
                    This is a tool for finding objects across large child lists.
                    Half of this object's children will be hidden one at a time
                    You just need to mark whether or not you can still see the object you're trying to locate.
                    """u8);
            }

            var biYes = ImGui.Selectable("Object is visible", ImGuiSelectableFlags.NoAutoClosePopups);
            var biNo = ImGui.Selectable("Object NOT visible", ImGuiSelectableFlags.NoAutoClosePopups);
            if (biYes || biNo) {
                var children = GetChildren(node).Cast<IVisibilityTarget>().ToList();
                var matched = children.Where(ch => biYes ? !ch.ShouldDrawSelf : ch.ShouldDrawSelf);
                bisectTemp[node].rejects.AddRange(matched);
                foreach (var ch in children) ch.ShouldDrawSelf = false;
                bisectTemp[node].pending.Clear();
                bisectTemp[node].pending.AddRange(children.Where(ch => !bisectTemp[node].rejects.Contains(ch)));

                if (bisectTemp[node].pending.Count == 0) {
                    Logger.Error("No objects left, something went wrong");
                } else if (bisectTemp[node].pending.Count == 1) {
                    var match = bisectTemp[node].pending.First();
                    Logger.Info($"Found match: {match} (index {children.IndexOf(match)} / {children.Count}");
                    match.ShouldDrawSelf = true;
                    GetRootEditor(context)?.SetPrimaryInspector(match);
                    ImGui.CloseCurrentPopup();
                    foreach (var pending in bisectTemp[node].rejects) pending.ShouldDrawSelf = true;
                    bisectTemp.Clear();
                } else {
                    foreach (var pending in bisectTemp[node].pending.Slice(0, (bisectTemp[node].pending.Count + 1) / 2)) {
                        pending.ShouldDrawSelf = true;
                    }
                }
            }
            if (ImGui.Selectable("Reset")) {
                foreach (var ch in GetChildren(node).Cast<IVisibilityTarget>()) ch.ShouldDrawSelf = true;
                bisectTemp.Clear();
            }
            ImGui.EndMenu();
        }
    }
}
