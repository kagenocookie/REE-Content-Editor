using System.Collections;
using System.Numerics;
using System.Text.Json;
using ContentEditor.App.ImguiHandling;
using ContentEditor.App.Windowing;
using ContentEditor.Core;
using ReeLib.via;

namespace ContentEditor.App;

public class UndoRedo
{
    private static readonly Dictionary<WindowBase, StateList> states = new();
    static UndoRedo()
    {
        AppConfig.Instance.MaxUndoSteps.ValueChanged += (steps) => {
            foreach (var s in states) {
                s.Value.MaxSteps = steps;
            }
        };
    }

    public enum CallbackType
    {
        Do = 1,
        Undo = 2,
        Both = 3
    }

    private class StateList
    {
        public int MaxSteps { get; internal set; } = AppConfig.Instance.MaxUndoSteps.Get();
        public bool CanUndo => CurrentIndex >= 0;
        public bool CanRedo => CurrentIndex < Items.Count - 1;

        private readonly List<UndoRedoCommand> Items = new();
        private int CurrentIndex = -1;
        private readonly Dictionary<UndoRedoCommand, DisposableCommand> Disposables = new();

        private struct DisposableCommand
        {
            public Action? DisposeBeforeState;
            public Action? DisposeAfterState;
        }

        public void Push(UndoRedoCommand item, UndoRedoMergeMode mode = UndoRedoMergeMode.MergeIdentical)
        {
            if (MaxSteps == 0) {
                item.Do();
                item.ExecuteHooks(CallbackType.Do);
                return;
            }

            if (mode == UndoRedoMergeMode.MergeIdentical && CurrentIndex >= 0 && CurrentIndex == Items.Count - 1 && Items.Last().Id == item.Id && item.Merge(Items[CurrentIndex])) {
                Items[CurrentIndex] = item;
                item.Do();
                item.ExecuteHooks(CallbackType.Do);
                return;
            }
            if (CurrentIndex == -1 || CurrentIndex < Items.Count - 1) {
                for (int i = Items.Count - 1; i >= CurrentIndex + 1; --i) {
                    if (Disposables.Remove(Items[i], out var disposable)) {
                        disposable.DisposeBeforeState?.Invoke();
                    }
                    Items.RemoveAt(i);
                }
            }
            if (CurrentIndex >= MaxSteps - 1) {
                var first = Items[0];
                Items.RemoveAt(0);
                if (Disposables.Remove(first, out var disposable)) {
                    disposable.DisposeAfterState?.Invoke();
                }
                Items.Add(item);
            } else {
                Items.Add(item);
                CurrentIndex++;
            }
            item.Do();
            item.ExecuteHooks(CallbackType.Do);
            // Console.WriteLine($"State {Items.Count}[{CurrentIndex}] ");
        }

        public void Undo()
        {
            if (CanUndo) {
                var item = Items[CurrentIndex--];
                item.Undo();
                item.ExecuteHooks(CallbackType.Undo);
            }
            // Console.WriteLine($"State {Items.Count}[{CurrentIndex}] ");
        }

        public void Redo()
        {
            if (CanRedo) {
                var item = Items[++CurrentIndex];
                item.Do();
                item.ExecuteHooks(CallbackType.Do);
            }
            // Console.WriteLine($"State {Items.Count}[{CurrentIndex}] ");
        }

        public void AddCommandCallback(CallbackType type, Action action)
        {
            var last = Items.LastOrDefault();
            if (last == null) return;

            last.hooks ??= new List<(CallbackType type, Action)>();
            last.hooks.Add((type, action));
            last.ExecuteHooks(CallbackType.Do);
        }

        public void SetCommandDisposable(Action? disposeBefore, Action? disposeAfter)
        {
            var last = Items.LastOrDefault();
            if (last == null) return;

            if (disposeAfter == null && disposeBefore == null) return;

            Disposables.Add(last, new DisposableCommand() {
                DisposeBeforeState = disposeBefore,
                DisposeAfterState = disposeAfter,
            });
        }

        internal void Clear()
        {
            foreach (var disposable in Disposables.Values) {
                disposable.DisposeBeforeState?.Invoke();
                disposable.DisposeAfterState?.Invoke();
            }
            Disposables.Clear();
        }
    }

    private static StateList GetState(WindowBase? window)
    {
        window ??= EditorWindow.CurrentWindow;
        if (window == null) throw new Exception("No active window!");
        if (!states.TryGetValue(window, out var state)) {
            return states[window] = state = new();
        }
        return state;
    }

    public static void RecordClipboardSet<TValue>(UIContext context, JsonSerializerOptions? jsonOptions = null)
    {
        try {
            var data = EditorWindow.CurrentWindow?.GetClipboard();
            if (string.IsNullOrEmpty(data)) return;

            var val = JsonSerializer.Deserialize<TValue>(data, jsonOptions ?? JsonConfig.jsonOptionsIncludeFields);
            if (val == null) {
                Logger.Error($"Failed to deserialize {typeof(TValue).Name}.");
                return;
            }
            RecordSet(context, val, mergeMode: UndoRedoMergeMode.NeverMerge);
        } catch (Exception e) {
            Logger.Error($"Failed to deserialize {typeof(TValue).Name}: " + e.Message);
        }
    }

    public static void RecordClipboardSet(UIContext context, Type valueType)
    {
        try {
            var data = EditorWindow.CurrentWindow?.GetClipboard();
            if (string.IsNullOrEmpty(data)) return;

            var val = JsonSerializer.Deserialize(data, valueType, JsonConfig.jsonOptionsIncludeFields);
            if (val == null) {
                return;
            }
            RecordSet(context, val, mergeMode: UndoRedoMergeMode.NeverMerge);
        } catch (Exception e) {
            Logger.Error("Invalid clipboard content: " + e.Message);
        }
    }

    public static void RecordSet<TValue>(UIContext context, TValue value, WindowBase? window = null, UndoRedoMergeMode mergeMode = UndoRedoMergeMode.MergeIdentical, string? undoId = null)
    {
        GetState(window).Push(new ContextSetUndoRedo<TValue>(context, value, null, undoId ?? $"{context.GetHashCode()}"), mergeMode);
    }

    public static void RecordSet<TValue>(UIContext context, TValue value, Action<UIContext> postChangeAction, WindowBase? window = null, UndoRedoMergeMode mergeMode = UndoRedoMergeMode.MergeIdentical)
    {
        GetState(window).Push(new ContextSetUndoRedo<TValue>(context, value, postChangeAction, $"{context.GetHashCode()}"), mergeMode);
    }

    public static void RecordListAdd<T>(UIContext context, IList list, T item, WindowBase? window = null) => RecordListInsert(context, list, item, -1, window);

    public static void RecordListInsert<T>(UIContext context, IList list, T item, int index, WindowBase? window = null)
    {
        GetState(window).Push(new ListInsertUndoRedo(context, list, item, index, $"{context.GetHashCode()} Insert {index}"));
    }

    public static void RecordListRemove(UIContext context, IList list, int index, WindowBase? window = null, int childIndexOffset = 0)
    {
        GetState(window).Push(new ListRemoveUndoRedo(context, list, index, $"{context.GetHashCode()} Remove {index}", childIndexOffset));
    }

    public static void RecordListRemove<T>(UIContext context, IList list, T item, WindowBase? window = null, int childIndexOffset = 0) where T : class
    {
        GetState(window).Push(new ListRemoveUndoRedo(context, list, list.IndexOf(item), $"{context.GetHashCode()} Remove {item}", childIndexOffset));
    }

    public static void RecordListClear(UIContext context, IList list, WindowBase? window = null, int childIndexOffset = 0)
    {
        GetState(window).Push(new ListClearUndoRedo(context, list, $"{context.GetHashCode()} Clear", childIndexOffset));
    }
    public static void RecordListMove<T>(UIContext? sourceContext, UIContext targetContext, IList? sourceList, T item, IList targetList, T targetItem, WindowBase? window = null, string? id = null) where T : class
    {
        GetState(window).Push(new ListMoveUndoRedo(sourceContext ?? targetContext, targetContext, sourceList ?? targetList, item, targetList, targetItem, id ?? $"Move {item.GetHashCode()} {targetItem.GetHashCode()}"));
    }

    public static void RecordCallback(UIContext? context, Action doCallback, Action undoCallback, string? id = null, WindowBase? window = null)
    {
        GetState(window).Push(new CallbackUndoRedo(context, doCallback, undoCallback, id));
    }
    public static void RecordCallbackSetter<T, TVal>(UIContext? context, T objContext, TVal oldValue, TVal newValue, Action<T, TVal> setter, string? id = null, WindowBase? window = null)
    {
        GetState(window).Push(new CallbackUndoRedo(context, () => setter(objContext, newValue), () => setter(objContext, oldValue), id));
    }

    public static void RecordAddChild<TNode>(UIContext context, TNode node, INodeObject<TNode> parent, int index = -1, WindowBase? window = null)
        where TNode : INodeObject<TNode>, IDisposable
    {
        if (Logger.ErrorIf(node.Parent != null, "Node already has parent")) return;
        var state = GetState(window);
        state.Push(new CallbackUndoRedo(
            context,
            index == -1 ? () => parent.AddChild(node) : () => parent.AddChild(node, index),
            () => parent.RemoveChild(node),
            $"Add child {node.GetHashCode()}"), UndoRedoMergeMode.NeverMerge);
        state.SetCommandDisposable(() => node.Dispose(), null);
    }

    public static void RecordMoveChild<TNode>(UIContext context, TNode node, INodeObject<TNode> newParent, int newIndex = -1, WindowBase? window = null)
        where TNode : class, INodeObject<TNode>
    {
        var prevParent = node.GetParent();
        if (Logger.ErrorIf(prevParent == null, "Cannot remove node without parent")) return;
        var prevIndex = prevParent.GetChildIndex(node);
        var originalGlobalTransform = (node as GameObject)?.WorldTransform ?? Matrix4x4.Identity;
        GetState(window).Push(
            new CallbackUndoRedo(context, () => {
                prevParent.RemoveChild(node);
                newParent.AddChild(node, newIndex);
                (node as GameObject)?.Transform.SetGlobalTransform(originalGlobalTransform);
            }, () => {
                newParent.RemoveChild(node);
                prevParent.AddChild(node, prevIndex);
                (node as GameObject)?.Transform.SetGlobalTransform(originalGlobalTransform);
            }, $"Move node {node.GetHashCode()}"),
            UndoRedoMergeMode.NeverMerge);
    }

    public static void RecordRemoveChild<TNode>(UIContext context, TNode node, WindowBase? window = null)
        where TNode : INodeObject<TNode>, IDisposable
    {
        var prevParent = node.GetParent();
        if (Logger.ErrorIf(prevParent == null, "Cannot remove node without parent")) return;
        var prevIndex = prevParent.GetChildIndex(node);
        var state = GetState(window);
        state.Push(new CallbackUndoRedo(
            context,
            () => prevParent.RemoveChild(node),
            () => prevParent.AddChild(node, prevIndex),
            $"Remove node {node.GetHashCode()}"), UndoRedoMergeMode.NeverMerge);
        state.SetCommandDisposable(null, () => node.Dispose());
    }

    public static void Undo(WindowBase? window = null) => GetState(window).Undo();
    public static void Redo(WindowBase? window = null) => GetState(window).Redo();
    public static bool CanUndo(WindowBase? window = null) => GetState(window).CanUndo;
    public static bool CanRedo(WindowBase? window = null) => GetState(window).CanRedo;
    public static void Clear(WindowBase window)
    {
        if (states.Remove(window, out var state)) {
            state.Clear();
        }
    }

    public static void AttachCallbackToLastAction(CallbackType type, Action action, WindowBase? window = null)
    {
        GetState(window).AddCommandCallback(type, action);
    }

    public static void AttachClearChildren(CallbackType type, UIContext context, WindowBase? window = null)
    {
        GetState(window).AddCommandCallback(type, () => context.ClearChildren());
    }
    public static void AttachClearState(UIContext context, CallbackType type = CallbackType.Both, WindowBase? window = null)
    {
        GetState(window).AddCommandCallback(type, () => context.ResetState());
    }

    #region Undo command types

    private static Random random = new Random();
    private static string RandomString(int length)
    {
        var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var stringChars = new char[length];

        for (int i = 0; i < stringChars.Length; i++) {
            stringChars[i] = chars[random.Next(chars.Length)];
        }
        return new String(stringChars);
    }

    public class ContextSetUndoRedo<T>(UIContext context, T value, Action<UIContext>? postChangeAction, string id) : UndoRedoCommand(id)
    {
        private T originalValue = context.Get<T>();

        public override void Do()
        {
            context.Set(value);
            postChangeAction?.Invoke(context);
        }

        public override void Undo()
        {
            context.Set(originalValue);
            postChangeAction?.Invoke(context);
        }

        public override bool Merge(UndoRedoCommand previousValue)
        {
            originalValue = ((ContextSetUndoRedo<T>)previousValue).originalValue;
            return true;
        }
    }

    public class ListMoveUndoRedo(UIContext sourceContext, UIContext targetContext, IList sourceList, object item, IList targetList, object targetItem, string? id) : UndoRedoCommand(id ?? $"Swap_{RandomString(10)}")
    {
        private int index1 = sourceList.IndexOf(item);

        public override void Do()
        {
            sourceList.Remove(item);
            if (sourceList != targetList) {
                var index2 = targetList.IndexOf(targetItem);
                targetList.Insert(index2, item);
                targetContext.children.RemoveAtAfter(index2);
                sourceContext.children.RemoveAtAfter(index1);
                targetContext.Changed = true;
            } else {
                var index2 = sourceList.IndexOf(targetItem);
                if (index2 < index1) {
                    sourceList.Insert(index2, item);
                } else if (index2 < sourceList.Count - 1) {
                    sourceList.Insert(index2 + 1, item);
                } else {
                    sourceList.Add(item);
                }
                sourceContext.children.RemoveAtAfter(Math.Min(index1, index2));
            }
            sourceContext.Changed = true;
        }

        public override void Undo()
        {
            if (sourceList != targetList) {
                var index2 = targetList.IndexOf(item);
                targetList.RemoveAt(index2);
                if (index1 == sourceList.Count) sourceList.Add(item); else sourceList.Insert(index1, item);
                targetContext.children.RemoveAtAfter(index2);
                sourceContext.children.RemoveAtAfter(index1);
                targetContext.Changed = true;
            } else {
                sourceList.Remove(item);
                var index2 = sourceList.IndexOf(targetItem);
                if (index1 == sourceList.Count) sourceList.Add(item); else sourceList.Insert(index1, item);
                sourceContext.children.RemoveAtAfter(Math.Min(index1, index2));
            }
            sourceContext.Changed = true;
        }

        public override bool Merge(UndoRedoCommand previousValue)
        {
            return false;
        }
    }

    public class ListInsertUndoRedo(UIContext context, IList list, object? item, int index, string? id) : UndoRedoCommand(id ?? $"Callback_{RandomString(10)}")
    {
        public override void Do()
        {
            if (index < 0) {
                index = -list.Count;
                list.Add(item);
                // context.children.RemoveAtAfter(list.Count - 1);
            } else {
                list.Insert(index, item);
                context.children.RemoveAtAfter(index);
            }
            context.Changed = true;
        }

        public override void Undo()
        {
            var idx = list.IndexOf(item);
            if (idx == -1) return;

            list.RemoveAt(idx);
            context.children.RemoveAtAfter(idx);
            context.Changed = true;
        }

        public override bool Merge(UndoRedoCommand previousValue)
        {
            return false;
        }
    }

    public class ListClearUndoRedo(UIContext context, IList list, string? id, int childIndexOffset) : UndoRedoCommand(id ?? $"Callback_{RandomString(10)}")
    {
        private List<object?>? backup;

        public override void Do()
        {
            if (backup == null) {
                backup = new List<object?>();
                foreach (var item in list) backup.Add(item);
            }
            list.Clear();
            context.children.RemoveAtAfter(childIndexOffset);
            context.Changed = true;
        }

        public override void Undo()
        {
            foreach (var item in backup!) list.Add(item);
            context.children.RemoveAtAfter(childIndexOffset);
            context.Changed = true;
        }

        public override bool Merge(UndoRedoCommand previousValue)
        {
            return false;
        }
    }
    public class ListRemoveUndoRedo(UIContext context, IList list, int index, string? id, int childIndexOffset) : UndoRedoCommand(id ?? $"Callback_{RandomString(10)}")
    {
        private object? item;

        public override void Do()
        {
            item = list[index];
            list.RemoveAt(index);
            context.children.RemoveAtAfter(index + childIndexOffset);
            context.Changed = true;
        }

        public override void Undo()
        {
            list.Insert(index, item);
            context.children.RemoveAtAfter(index + childIndexOffset);
            context.Changed = true;
        }

        public override bool Merge(UndoRedoCommand previousValue)
        {
            return false;
        }
    }

    public class CallbackUndoRedo(UIContext? context, Action doCallback, Action undoCallback, string? id)
        : UndoRedoCommand(id ?? $"Callback_{RandomString(10)}")
    {
        private Action undoCallback = undoCallback;

        public override void Do()
        {
            doCallback.Invoke();
            if (context != null) context.Changed = true;
        }

        public override void Undo()
        {
            undoCallback.Invoke();
            if (context != null) context.Changed = true;
        }

        public override bool Merge(UndoRedoCommand previousValue)
        {
            undoCallback = ((CallbackUndoRedo)previousValue).undoCallback;
            return true;
        }
    }

    #endregion
}

public abstract class UndoRedoCommand(string id)
{
    public string Id { get; } = id;

    internal List<(UndoRedo.CallbackType type, Action callback)>? hooks;
    internal void ExecuteHooks(UndoRedo.CallbackType type)
    {
        if (hooks == null) return;
        foreach (var hook in hooks) {
            if ((hook.type & type) != 0) {
                hook.callback.Invoke();
            }
        }
    }

    public abstract void Do();
    public abstract void Undo();
    public abstract bool Merge(UndoRedoCommand previousValue);
}

public enum UndoRedoMergeMode
{
    MergeIdentical,
    NeverMerge,
}