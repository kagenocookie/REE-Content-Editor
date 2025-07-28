using System.Collections;
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

    private class StateList
    {
        public readonly List<UndoRedoCommand> Items = new();
        public int CurrentIndex = -1;
        public int MaxSteps { get; set; } = AppConfig.Instance.MaxUndoSteps.Get();
        public bool CanUndo => CurrentIndex >= 0;
        public bool CanRedo => CurrentIndex < Items.Count - 1;

        public void Push(UndoRedoCommand item, UndoRedoMergeMode mode = UndoRedoMergeMode.MergeIdentical)
        {
            if (MaxSteps == 0) {
                item.Do();
                return;
            }

            if (mode == UndoRedoMergeMode.MergeIdentical && CurrentIndex >= 0 && CurrentIndex == Items.Count - 1 && Items.Last().Id == item.Id && item.Merge(Items[CurrentIndex])) {
                Items[CurrentIndex] = item;
                item.Do();
                return;
            }
            if (CurrentIndex == -1) {
                Items.Clear();
            } else if (CurrentIndex < Items.Count - 1) {
                Items.RemoveAtAfter(CurrentIndex + 1);
            }
            if (CurrentIndex >= MaxSteps - 1) {
                Items.RemoveAt(0);
                Items.Add(item);
            } else {
                Items.Add(item);
                CurrentIndex++;
            }
            item.Do();
            Console.WriteLine($"State {Items.Count}[{CurrentIndex}] ");
        }

        public void Undo()
        {
            if (CanUndo) {
                var item = Items[CurrentIndex--];
                item.Undo();
            }
            Console.WriteLine($"State {Items.Count}[{CurrentIndex}] ");
        }

        public void Redo()
        {
            if (CanRedo) {
                var item = Items[++CurrentIndex];
                item.Do();
            }
            Console.WriteLine($"State {Items.Count}[{CurrentIndex}] ");
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

    public static void RecordSet<TValue>(UIContext context, TValue value, WindowBase? window = null)
    {
        GetState(window).Push(new ContextSetUndoRedo<TValue>(context, value, $"{context.GetHashCode()}"));
    }

    public static void RecordListAdd<T>(UIContext context, IList list, T item, WindowBase? window = null) => RecordListInsert(context, list, item, -1, window);

    public static void RecordListInsert<T>(UIContext context, IList list, T item, int index, WindowBase? window = null)
    {
        GetState(window).Push(new ListInsertUndoRedo(context, list, item, index, $"{context.GetHashCode()} Insert {index}"));
    }

    public static void RecordListRemove(UIContext context, IList list, int index, WindowBase? window = null)
    {
        GetState(window).Push(new ListRemoveUndoRedo(context, list, index, $"{context.GetHashCode()} Remove {index}"));
    }

    public static void RecordListRemove<T>(UIContext context, IList list, T item, WindowBase? window = null) where T : class
    {
        GetState(window).Push(new ListRemoveUndoRedo(context, list, list.IndexOf(item), $"{context.GetHashCode()} Remove {item}"));
    }

    public static void RecordCallback(UIContext? context, Action doCallback, Action undoCallback, string? id = null, WindowBase? window = null)
    {
        GetState(window).Push(new CallbackUndoRedo(context, doCallback, undoCallback, id));
    }
    public static void RecordCallbackSetter<T, TVal>(UIContext? context, T objContext, TVal oldValue, TVal newValue, Action<T, TVal> setter, string? id = null, WindowBase? window = null)
    {
        GetState(window).Push(new CallbackUndoRedo(context, () => setter(objContext, newValue), () => setter(objContext, oldValue), id));
    }

    public static void Undo(WindowBase? window = null) => GetState(window).Undo();
    public static void Redo(WindowBase? window = null) => GetState(window).Redo();
    public static bool CanUndo(WindowBase? window = null) => GetState(window).CanUndo;
    public static bool CanRedo(WindowBase? window = null) => GetState(window).CanRedo;
    public static void Clear(WindowBase window) => states.Remove(window);

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

    public class ContextSetUndoRedo<T>(UIContext context, T value, string id) : UndoRedoCommand(id)
    {
        private T originalValue = context.Get<T>();

        public override void Do()
        {
            context.Set(value);
        }

        public override void Undo()
        {
            context.Set(originalValue);
        }

        public override bool Merge(UndoRedoCommand previousValue)
        {
            originalValue = ((ContextSetUndoRedo<T>)previousValue).originalValue;
            return true;
        }
    }

    public class ListInsertUndoRedo(UIContext context, IList list, object? item, int index, string? id) : UndoRedoCommand(id ?? $"Callback_{RandomString(10)}")
    {
        public override void Do()
        {
            if (index < 0) {
                index = -list.Count;
                list.Add(item);
                context.children.RemoveAtAfter(list.Count - 1);
            } else {
                list.Insert(index, item);
                context.children.RemoveAtAfter(index);
            }
            context.Changed = true;
        }

        public override void Undo()
        {
            list.RemoveAt(Math.Abs(index));
            context.children.RemoveAtAfter(Math.Abs(index));
            context.Changed = true;
        }

        public override bool Merge(UndoRedoCommand previousValue)
        {
            return false;
        }
    }

    public class ListRemoveUndoRedo(UIContext context, IList list, int index, string? id) : UndoRedoCommand(id ?? $"Callback_{RandomString(10)}")
    {
        private object? item;

        public override void Do()
        {
            item = list[index];
            list.RemoveAt(index);
            context.children.RemoveAtAfter(index);
            context.Changed = true;
        }

        public override void Undo()
        {
            list.Insert(index, item);
            context.children.RemoveAtAfter(index);
            context.Changed = true;
        }

        public override bool Merge(UndoRedoCommand previousValue)
        {
            return false;
        }
    }

    public class CallbackUndoRedo(UIContext? context, Action doCallback, Action undoCallback, string? id) : UndoRedoCommand(id ?? $"Callback_{RandomString(10)}")
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

    public abstract void Do();
    public abstract void Undo();
    public abstract bool Merge(UndoRedoCommand previousValue);
}

public enum UndoRedoMergeMode
{
    MergeIdentical,
    NeverMerge,
}