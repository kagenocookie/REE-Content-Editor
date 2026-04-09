using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using ContentEditor.Core;

namespace ContentEditor.App.ImguiHandling;

public abstract class DictionaryListImguiHandler<TKey, TItem, TListType> : IObjectUIHandler
    where TListType : IList<TItem>, new()
{
    private sealed class KeyHolder(TKey key)
    {
        public TKey Key { get; set; } = key;
    }

    protected bool AllowAdditions { get; set; } = true;
    protected bool FlatList { get; set; }
    protected bool Filterable { get; set; }

    protected virtual bool Filter(UIContext context, string filter)
        => context.GetRaw()?.ToString()?.Contains(filter, StringComparison.InvariantCultureIgnoreCase) == true;

    protected virtual IObjectUIHandler CreateNewItemInput(UIContext context)
    {
        return WindowHandlerFactory.CreateUIHandler<TKey>(default(TKey));
    }

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

        if (FlatList) {
            ImGui.SeparatorText(context.label);
        } else {
            if (!ImGui.TreeNode(context.label)) return;
        }
        if (Filterable) {
            ImGui.SetNextItemAllowOverlap();
            ImGui.InputTextWithHint("Filter"u8, $"{AppIcons.SI_GenericMagnifyingGlass}", ref context.Filter, 120);
            if (!string.IsNullOrEmpty(context.Filter)) {
                ImGui.SameLine();
                ImGui.SetCursorScreenPos(new Vector2(ImGui.GetItemRectMax().X - ImGui.GetFrameHeight() - ImGui.GetStyle().FramePadding.X - ImGui.GetStyle().ItemInnerSpacing.X - ImGui.CalcTextSize("Filter").X, ImGui.GetItemRectMin().Y));
                ImGui.SetNextItemAllowOverlap();
                if (ImGui.Button($"{AppIcons.SI_GenericClose}")) {
                    context.Filter = string.Empty;
                }
            }
            ImGui.Spacing();
        }

        if (AllowAdditions) {
            if (context.children.Count == 0) {
                var store = new KeyHolder(default(TKey)!);
                context.AddChild<KeyHolder, TKey>("New item", store, CreateNewItemInput(context),
                    getter: (c) => c!.Key,
                    setter: (c, v) => c.Key = v!);
            }

            var newCtx = context.children[0];
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.IconSecondary);
            if (ImGui.Button($"{AppIcons.SI_GenericAdd}")) {
                var item = CreateItem(context, newCtx.Get<TKey>());
                if (item != null) list.Add(item);
                if (context.children.Count == 0) {
                    context.children.Add(newCtx);
                }
            }
            ImGui.PopStyleColor();
            ImguiHelpers.Tooltip("Add"u8);
            ImGui.SameLine();
            newCtx.ShowUI();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

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
                    var newItem = CreateItem(context, child.Get<TKey>());
                    if (newItem != null) list[i] = newItem;
                    context.children[ctxIndex] = null!;
                }
                ImGui.PopID();
                continue;
            }
            if (Filterable && !string.IsNullOrEmpty(context.Filter) && !Filter(child, context.Filter)) {
                ImGui.PopID();
                continue;
            }
            if (child.uiHandler == null) {
                InitChildContext(child);
                if (child.uiHandler == null) AddItemHandler(child);
            }
            AppImguiHelpers.PrependIcon(item);
            child.ShowUI();
            PostItem(child);

            ImGui.PopID();
            if (context.children.Count <= ctxIndex || context.children[ctxIndex] != child) {
                // this should "cleanly" handle deletes
                break;
            }
        }

        if (!FlatList) ImGui.TreePop();
    }

    [return: NotNull]
    protected abstract TKey GetKey(TItem item);
    protected virtual void AddItemHandler(UIContext item) => item.AddDefaultHandler();
    protected abstract TItem? CreateItem(UIContext context, TKey key);
    protected virtual void PostItem(UIContext itemContext) {}
    protected virtual void InitChildContext(UIContext itemContext) {}
}
