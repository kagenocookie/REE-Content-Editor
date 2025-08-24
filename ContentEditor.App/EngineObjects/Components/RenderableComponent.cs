using ContentEditor.App.Graphics;
using ReeLib;
using ReeLib.via;

namespace ContentEditor.App;

public abstract class RenderableComponent(GameObject gameObject, RszInstance data) : Component(gameObject, data), IDisposable
{
    internal abstract void Render(RenderContext context);

    public abstract AABB LocalBounds { get; }

    internal override void OnActivate()
    {
        GameObject.Scene!.AddRenderComponent(this);
    }

    internal override void OnDeactivate()
    {
        GameObject.Scene!.RemoveRenderComponent(this);
    }

    public void Dispose()
    {
        if (GameObject.Scene != null) {
            OnDeactivate();
        }
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
