namespace ContentEditor;

public abstract class SimpleWindow(UIContext parentContext) : IWindowHandler
{
    public abstract string HandlerName { get; }

    public bool HasUnsavedChanges => false;

    public UIContext ParentContext { get; } = parentContext;
    public UIContext Context { get; private set; } = null!;

    void IWindowHandler.Init(UIContext context)
    {
        Context = context;
    }

    public abstract void OnIMGUI();

    public void OnWindow() => this.ShowDefaultWindow(Context);

    public virtual bool RequestClose()
    {
        return false;
    }
}
