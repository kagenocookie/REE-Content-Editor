using ContentEditor.App.Graphics;
using ReeLib;

namespace ContentEditor.App;

public abstract class RenderableComponent(GameObject gameObject, RszInstance data) : Component(gameObject, data), IDisposable
{
    internal abstract void Render(RenderContext context);

    internal override void OnEnterScene(Scene rootScene)
    {
        GameObject.Scene!.AddRenderComponent(this);
    }

    internal override void OnExitScene(Scene rootScene)
    {
        GameObject.Scene!.RemoveRenderComponent(this);
    }

    public void Dispose()
    {
        if (GameObject.Scene != null) {
            OnExitScene(GameObject.Scene.RootScene);
        }
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
