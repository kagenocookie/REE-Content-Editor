using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using ContentEditor;
using ContentEditor.Core;
using ContentPatcher;
using ImGuiNET;
using ReeLib;
using ReeLib.Common;
using ReeLib.Pfb;

namespace ContentEditor.App.ImguiHandling;

[ObjectImguiHandler(typeof(Folder))]
public class FolderDataEditor : IObjectUIHandler
{
    private static readonly MemberInfo[] BaseMembers = [
        typeof(Folder).GetProperty(nameof(Folder.Name))!,
        typeof(Folder).GetField(nameof(Folder.Tags))!,
        typeof(Folder).GetField(nameof(Folder.ScenePath))!,
        typeof(Folder).GetField(nameof(Folder.Update))!,
        typeof(Folder).GetField(nameof(Folder.Draw))!,
        typeof(Folder).GetField(nameof(Folder.Active))!,
    ];
    private static readonly MemberInfo[] BaseMembers2 = [
        typeof(Folder).GetField(nameof(Folder.Offset))!,
    ];

    public void OnIMGUI(UIContext context)
    {
        var folder = context.Get<Folder>();
        if (context.children.Count == 0) {
            var ws = context.GetWorkspace();
            if (folder.Parent != null) {
                // context.AddChild<Folder, string>("Scene path", folder, new ConfirmedStringFieldHandler(), c => c.ScenePath, (c, v) => c.ScenePath = v);
                WindowHandlerFactory.SetupObjectUIContext(context, typeof(Folder), members: BaseMembers);
                if (ws?.Env.Classes.Folder.IndexOfField(nameof(Folder.Offset)) != -1) {
                    WindowHandlerFactory.SetupObjectUIContext(context, typeof(Folder), members: BaseMembers2);
                }
            }
        }

        context.ShowChildrenUI();
    }
}

public class FolderNodeEditor : IObjectUIHandler
{
    private static readonly Vector4 nodeColor = Colors.Folder;

    public void OnIMGUI(UIContext context)
    {
        var folder = context.Get<Folder>();
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
            var inspector = context.FindInterfaceInParentHandlers<IInspectorController>();
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
        context.FindInterfaceInParentHandlers<IInspectorController>()?.SetPrimaryInspector(folder);
    }

    private static void HandleContextMenu(Folder node, UIContext context)
    {
        if (ImGui.Button("New GameObject")) {
            var ws = context.GetWorkspace();
            var newgo = new GameObject("New_GameObject", ws!.Env, node, node.Scene);
            UndoRedo.RecordListAdd(context, node.GameObjects, newgo);
            newgo.MakeNameUnique();
            context.FindInterfaceInParentHandlers<IInspectorController>()?.SetPrimaryInspector(newgo);
            ImGui.CloseCurrentPopup();
        }
        if (ImGui.Button("New folder")) {
            var ws = context.GetWorkspace();
            var newFolder = new Folder("New_Folder", ws!.Env, node.Scene);
            UndoRedo.RecordAddChild(context, newFolder, node);
            newFolder.MakeNameUnique();
            context.FindInterfaceInParentHandlers<IInspectorController>()?.SetPrimaryInspector(newFolder);
            context.GetChild<SubfolderNodeEditor>()?.ClearChildren();
            ImGui.CloseCurrentPopup();
        }
        if (node.Parent != null) {
            if (ImGui.Button("Delete")) {
                UndoRedo.RecordRemoveChild(context, node);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Button("Duplicate")) {
                var clone = node.Clone();
                UndoRedo.RecordAddChild(context, clone, node.Parent, node.Parent.GetChildIndex(node) + 1);
                clone.MakeNameUnique();
                var inspector = context.FindInterfaceInParentHandlers<IInspectorController>();
                inspector?.SetPrimaryInspector(clone);
                ImGui.CloseCurrentPopup();
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
    }

    protected override void HandleSelect(UIContext context, Folder node)
    {
        if (node.Parent == null) return;
        base.HandleSelect(context, node);
    }
}
