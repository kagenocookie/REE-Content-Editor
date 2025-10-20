using System.Diagnostics.CodeAnalysis;
using ContentEditor.Core;
using ContentEditor.Editor;
using ImGuiNET;

namespace ContentEditor;

public class UIContext
{
    public string? state;
    public UIOptions options;
    public object? target;
    public object? owner;
    public UIContext root;
    public FieldDisplaySettings? displaySettings;
    public string label;
    // public string field = string.Empty;
    public object? originalValue;
    private Func<UIContext, object?> getter;
    private Action<UIContext, object?> setter;
    public UIContext? parent;
    public IObjectUIHandler? uiHandler;
    public readonly List<UIContext> children = new();
    public StringFormatter? stringFormatter;
    private bool wasChanged;

    public bool HasChildren => children.Count != 0;
    public bool StateBool
    {
        get => state != null;
        set => state = value ? string.Empty : null;
    }

    public bool Changed
    {
        get => wasChanged;
        set { if (value) SetChangedInParents(); else SetUnchanged(); }
    }

    public UIContext(string label, object? target, UIContext root, Func<UIContext, object?> getter, Action<UIContext, object?> setter, UIOptions options)
    {
        this.target = target;
        this.label = label;
        this.getter = getter;
        this.setter = setter;
        this.options = options;
        this.root = root;
        originalValue = GetRaw();
    }

    public static UIContext CreateRootContext(string label, object instance, UIOptions? options = null)
    {
        var root = new UIContext(label, instance, null!, (ctx) => ctx.target, (_, _) => throw new NotImplementedException(), options ?? new UIOptions());
        root.root = root;
        return root;
    }

    public UIContext? GetChild(int index) => index >= 0 && index < children.Count ? children[index] : null;
    public UIContext? GetChild(object target)
    {
        foreach (var child in children) {
            if (child.target == target) return child;
        }
        return null;
    }
    public UIContext? GetChild<T>() where T : IObjectUIHandler => children.FirstOrDefault(ch => ch.uiHandler is T);
    public UIContext? GetChildByValue<T>() => children.FirstOrDefault(ch => ch.target is T);
    public T? GetChildValue<T>() => (T?)children.FirstOrDefault(ch => ch.target is T)?.target;
    public T? GetChildHandler<T>() => (T?)children.FirstOrDefault(ch => ch.uiHandler is T)?.uiHandler;
    public UIContext? FindNestedChildByHandler<T>() where T : class, IObjectUIHandler => children.Select(ch => (ch.uiHandler is T ? ch : ch.FindNestedChildByHandler<T>())).FirstOrDefault(cc => cc != null);

    private static object? DefaultGetter(UIContext ctx) => ctx.target;

    private static void NotImplementedSetter(UIContext ctx, object? val)
    {
        throw new NotImplementedException();
    }

    public UIContext AddChild(string label, object? instance, IObjectUIHandler? handler = null, Func<UIContext, object?>? getter = null, Action<UIContext, object?>? setter = null)
    {
        setter ??= NotImplementedSetter;
        var child = new UIContext(label, instance, root, getter ?? DefaultGetter, setter, options) {
            parent = this,
        };
        child.uiHandler = handler;
        children.Add(child);
        return child;
    }
    public UIContext AddChild<TTarget, TValue>(string label, TTarget? instance, IObjectUIHandler? handler = null, Func<TTarget?, TValue?>? getter = null, Action<TTarget, TValue?>? setter = null)
    {
        Func<UIContext, object?> boxedGetter = getter == null ? DefaultGetter : (ctx) => getter((TTarget?)ctx.target);
        Action<UIContext, object?>? boxedSetter = setter == null ? null :  (ctx, val) => {
            setter.Invoke((TTarget)ctx.target!, (TValue?)val);
        };
        boxedSetter ??= ((_, _) => throw new NotImplementedException());
        var child = new UIContext(label, instance, root, boxedGetter, boxedSetter, options) {
            parent = this,
        };
        child.uiHandler = handler;
        children.Add(child);
        return child;
    }

    public bool IsChildOf(UIContext context)
    {
        var parent = this.parent;
        while (parent != null) {
            if (parent == context) return true;
            parent = parent.parent;
        }
        return false;
    }

    /// <summary>
    /// Marks the value as changed, propagates to parents.
    /// </summary>
    private void SetChangedInParents()
    {
        SetChangedInParentsImpl(new EditorUIEvent(UIContextEvent.Changed, this));
    }

    private void SetChangedInParentsImpl(EditorUIEvent eventData)
    {
        wasChanged = true;
        if (parent != null) {
            if (uiHandler is IUIContextEventHandler handler) {
                if (!handler.HandleEvent(this, eventData)) {
                    return;
                }
            }
            parent.SetChangedInParentsImpl(eventData);
        }
    }
    public void SetChangedNoPropagate(bool changed)
    {
        wasChanged = changed;
    }

    /// <summary>
    /// Marks this value as unchanged, propagates to parents as long as all their children are also unchanged.
    /// </summary>
    private void SetUnchanged()
    {
        SetUnchangedImpl(new EditorUIEvent(UIContextEvent.Reverted, this));
    }

    private void SetUnchangedImpl(EditorUIEvent eventData)
    {
        if (!children.Any(c => c.Changed)) {
            wasChanged = false;
            if (parent != null) {
                if (uiHandler is IUIContextEventHandler handler) {
                    if (handler.HandleEvent(this, eventData)) {
                        return;
                    }
                }
                parent.SetUnchangedImpl(eventData);
            }
        } else {
            PropagateUpdateEvent(new EditorUIEvent(UIContextEvent.Updated, eventData.origin));
        }
    }

    private void PropagateUpdateEvent(EditorUIEvent eventData)
    {
        if (parent != null) {
            if (parent.uiHandler is IUIContextEventHandler handler) {
                if (handler.HandleEvent(parent, eventData)) {
                    return;
                }
            }

            parent.PropagateUpdateEvent(eventData);
        }
    }

    /// <summary>
    /// Revert this and children's value to their original values.
    /// </summary>
    public void Revert()
    {
        RevertImpl(new EditorUIEvent(UIContextEvent.Reverting, this));
    }

    private void RevertImpl(EditorUIEvent eventData)
    {
        if (uiHandler is IUIContextEventHandler changedHandler) {
            if (!changedHandler.HandleEvent(this, eventData)) {
                return;
            }
        }
        foreach (var child in children) {
            child.RevertImpl(eventData);
        }
        if (originalValue != null) {
            Set(originalValue);
        }
    }

    /// <summary>
    /// Notifies the value and all child values of a save event and update their original values.
    /// </summary>
    public void Save()
    {
        SaveImpl(new EditorUIEvent(UIContextEvent.Saved, this));
    }

    private void SaveImpl(EditorUIEvent eventData)
    {
        if (uiHandler is IUIContextEventHandler changedHandler) {
            if (!changedHandler.HandleEvent(this, eventData)) {
                return;
            }
        }
        if (getter != null) {
            originalValue = getter?.Invoke(this);
            wasChanged = false;
        }
        foreach (var child in children) {
            child.SaveImpl(eventData);
        }
    }

    public object? GetRaw() => getter.Invoke(this);
    public T Get<T>() => (T?)getter.Invoke(this)!;
    public T? Cast<T>() where T : class => getter.Invoke(this) as T;
    public bool TryCast<T>([MaybeNullWhen(false)] out T value) where T : class => (value = getter.Invoke(this) as T) != null;
    public void Set<T>(T value)
    {
        setter.Invoke(this, value);
        if (originalValue != null && originalValue.Equals(value)) {
            SetUnchanged();
        } else {
            SetChangedInParents();
        }
    }

    public void ClearChildren()
    {
        foreach (var child in children) {
            (child.uiHandler as IDisposable)?.Dispose();
            child.ClearChildren();
        }
        children.Clear();
    }

    public void ShowUI()
    {
        if (uiHandler != null) {
            uiHandler.OnIMGUI(this);
        } else {
            ImGui.TextColored(Colors.Error, $"{label} (unsupported type {GetRaw()?.GetType().Name ?? "NULL"} for value {GetRaw()})");
        }
    }

    public void ShowChildrenUI()
    {
        for (int i = 0; i < children.Count; i++) {
            children[i].ShowUI();
        }
    }

    public void ShowChildrenNestedUI()
    {
        if (ImguiHelpers.TreeNodeSuffix(label, GetRaw()?.ToString() ?? "")) {
            for (int i = 0; i < children.Count; i++) {
                children[i].ShowUI();
            }
            ImGui.TreePop();
        }
    }

    public void RemoveChild(UIContext context)
    {
        children.Remove(context);
    }

    public override string ToString() => $"{label} ({target})";
}

public class FieldDisplaySettings
{
    public string? tooltip;
    public int marginBefore;
    public int marginAfter;
}

public class UIOptions
{
    public bool isReadonly;
}

public interface IObjectUIHandler
{
    void OnIMGUI(UIContext context);
}

public interface ITooltipHandler
{
    bool HandleTooltip(UIContext context);
}

public interface IContextMenuHandler
{
    bool AllowContextMenu => true;
    bool ShowContextMenuItems(UIContext context);
}

public enum UIContextEvent
{
    /// <summary>
    /// Any value was changed. Propagates upward.
    /// </summary>
    Changed,
    /// <summary>
    /// Any child value was reverted. Propagates upward.
    /// </summary>
    Reverted,
    /// <summary>
    /// The value was updated (any sort of change was triggered, unrelated to whether it's different from original or not). Propagates upward.
    /// </summary>
    Updated,
    /// <summary>
    /// The value is reverting. Propagates downward.
    /// </summary>
    Reverting,
    /// <summary>
    /// The value was saved. Propagates downward.
    /// </summary>
    Saved,
}


/// <summary>
/// A custom implementation for UIContext event handling.
/// </summary>
public interface IUIContextEventHandler
{
    /// <summary>
    /// Handles a triggered editor event.
    /// </summary>
    /// <param name="context">The context that is currently invoking the event</param>
    /// <param name="eventData">The event data</param>
    /// <returns>True if the event should propagate, false if it should stop here.</returns>
    bool HandleEvent(UIContext context, EditorUIEvent eventData);
}

/// <summary>
/// Contains an editor event data.
/// </summary>
/// <param name="type">The event type that was triggered.</param>
/// <param name="origin">The UIContext instance from which the event originates.</param>
public readonly record struct EditorUIEvent(UIContextEvent type, UIContext origin)
{
    public bool IsChangeFromChild => type is UIContextEvent.Changed or UIContextEvent.Reverted or UIContextEvent.Updated;
}