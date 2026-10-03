using System.Numerics;
using ContentEditor.Editor;

namespace ContentEditor.App.ImguiHandling;

/// <summary>
/// Wraps an IObjectUIHandle into its own dedicated window.
/// </summary>
public sealed class HandlerEmbedWindow(IObjectUIHandler handler, object target) : IWindowHandler, IRectWindow, IDisposable
{
    public string HandlerName => target.ToString() ?? Handler.ToString() ?? "HandlerEmbed";

    public bool HasUnsavedChanges => false;

    public Vector2 Size => context.Get<WindowData>().Size;
    public Vector2 Position => context.Get<WindowData>().Position;

    public IObjectUIHandler Handler { get; } = handler;
    private UIContext context = null!;
    public void Init(UIContext context)
    {
        this.context = context;
    }

    public void OnIMGUI()
    {
        if (context.children.Count == 0) {
            context.AddChild("##Contents", target, Handler);
        }
        context.ShowChildrenUI();
    }

    public void OnWindow() => this.ShowDefaultWindow(context);

    public bool RequestClose()
    {
        return true;
    }

    public void Dispose()
    {
        (Handler as IDisposable)?.Dispose();
    }
}
