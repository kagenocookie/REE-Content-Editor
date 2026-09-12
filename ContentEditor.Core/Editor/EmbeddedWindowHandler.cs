namespace ContentEditor;

public sealed class EmbeddedWindowHandler(IWindowHandler window) : IObjectUIHandler, IWindowHandler, IDisposable
{
    private UIContext? _lastContext;
    public string HandlerName => window.HandlerName;
    public bool HasUnsavedChanges => window.HasUnsavedChanges;

    public void Init(UIContext context) => window.Init(context);
    public void OnIMGUI(UIContext context)
    {
        if (context != _lastContext) {
            _lastContext = context;
            window.Init(context);
        }
        window.OnIMGUI();
    }

    public void OnIMGUI() => window.OnIMGUI();
    public void OnWindow() => window.OnWindow();
    public bool RequestClose() => window.RequestClose();

    public void Dispose() => (window as IDisposable)?.Dispose();

    public override string ToString() => window.ToString() ?? "Embedded Window";
}
