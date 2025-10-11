using ContentEditor.App.Graphics;
using ReeLib;
using ReeLib.via;

namespace ContentEditor.App;

public abstract class UpdateComponent(GameObject gameObject, RszInstance data) : Component(gameObject, data), IUpdateable, IDisposable
{
    public abstract void Update(float deltaTime);

    internal override void OnActivate()
    {
        GameObject.Scene!.AddUpdateComponent(this);
    }

    internal override void OnDeactivate()
    {
        GameObject.Scene!.RemoveUpdateComponent(this);
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
