using ContentEditor.App.ImguiHandling;
using ContentPatcher;

namespace ContentEditor.App;

public abstract class BaseWindowHandler : IWindowHandler
{
    public abstract string HandlerName { get; }

    public virtual bool HasUnsavedChanges => false;

    protected UIContext context = null!;
    protected WindowData data = null!;
    protected ContentWorkspace workspace = null!;

    public virtual void Init(UIContext context)
    {
        this.context = context;
        data = context.Get<WindowData>();
        workspace = context.GetWorkspace() ?? throw new Exception("Workspace not found");
    }

    public abstract void OnIMGUI();

    public virtual void OnWindow() => this.ShowDefaultWindow(context);

    public virtual bool RequestClose()
    {
        return false;
    }
}