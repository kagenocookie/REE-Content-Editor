using System.Diagnostics.CodeAnalysis;
using ImGuiNET;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ContentEditor.App.ImguiHandling;

public abstract class DictionaryListImguiHandler<TKey, TItem, TListType> : IObjectUIHandler
    where TListType : IList<TItem>, new()
{
    private sealed class KeyHolder(TKey key)
    {
        public TKey Key { get; set; } = key;
    }

    protected bool AllowAdditions { get; set; } = true;

    public void OnIMGUI(UIContext context)
    {
        var list = context.Get<TListType>();
        if (list == null) {
            ImGui.Text(context.label + ": NULL");
            ImGui.SameLine();
            if (!ImGui.Button("Create")) {
                return;
            }
            context.Set(list = new TListType());
        }

        if (!ImGui.TreeNode(context.label)) return;
        if (AllowAdditions && context.children.Count == 0) {
            var store = new KeyHolder(default(TKey)!);
            context.AddChild<KeyHolder, TKey>("New item", store, WindowHandlerFactory.CreateUIHandler<TKey>(default(TKey)),
                getter: (c) => c!.Key,
                setter: (c, v) => c.Key = v!);
        }
        var newCtx = AllowAdditions ? context.children[0] : null;
        var indexOffset = AllowAdditions ? 1 : 0;

        for (int i = 0; i < list.Count; ++i) {
            var item = list[i];
            ImGui.PushID(i);
            var ctxIndex = i + indexOffset;
            while (ctxIndex >= context.children.Count || context.children[ctxIndex] == null) {
                var childCtx = WindowHandlerFactory.CreateListElementContext(context, context.children.Count - indexOffset);
                context.children.Add(childCtx);
                childCtx.label = GetKey(list[context.children.Count - indexOffset - 1]).ToString() ?? childCtx.label;
            }
            var child = context.children[ctxIndex];
            if (item == null) {
                ImGui.Text(i + ": ");
                ImGui.SameLine();
                if (child.uiHandler == null) {
                    child.AddDefaultHandler<TKey>();
                }
                child.ShowUI();
                ImGui.SameLine();
                if (ImGui.Button("Create")) {
                    list[i] = CreateItem(context, child.Get<TKey>());
                    context.children[ctxIndex] = null!;
                }
                ImGui.PopID();
                continue;
            }
            if (child.uiHandler == null) {
                InitChildContext(child);
                if (child.uiHandler == null) child.AddDefaultHandler<TItem>();
            }
            child.ShowUI();
            PostItem(child);

            ImGui.PopID();
        }

        newCtx?.ShowUI();
        if (AllowAdditions && ImGui.Button("Add")) {
            list.Add(CreateItem(context, newCtx!.Get<TKey>()));
        }
        ImGui.TreePop();
    }

    [return: NotNull]
    protected abstract TKey GetKey(TItem item);
    protected abstract TItem CreateItem(UIContext context, TKey key);
    protected virtual void PostItem(UIContext itemContext) {}
    protected virtual void InitChildContext(UIContext itemContext) {}
}
