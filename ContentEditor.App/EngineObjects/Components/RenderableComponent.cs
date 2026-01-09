using System.Numerics;
using ContentEditor.App.Graphics;
using ReeLib;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App;

public abstract class RenderableComponent(GameObject gameObject, RszInstance data) : Component(gameObject, data), IDisposable
{
    internal abstract void Render(RenderContext context);

    public abstract AABB LocalBounds { get; }
    public bool IsStatic { get; set; } = true;
    private AABB _worldSpaceBounds = AABB.Invalid;
    public AABB WorldSpaceBounds
    {
        get {
            if (IsStatic && Transform.IsWorldTransformUpToDate && !_worldSpaceBounds.IsInvalid) {
                return _worldSpaceBounds;
            }
            return RecomputeWorldAABB();
        }
    }

    internal AABB RecomputeWorldAABB()
    {
        var local = LocalBounds;
        if (local.IsInvalid) {
            _worldSpaceBounds = AABB.Invalid;
        } else {
            var world = Transform.WorldTransform.ToSystem();
            _worldSpaceBounds = local.ToWorldBounds(world);
        }
        OnUpdateTransform();
        return _worldSpaceBounds;
    }

    protected virtual void OnUpdateTransform() {}

    internal override void OnActivate()
    {
        GameObject.Scene!.Renderable.Add(this);
    }

    internal override void OnDeactivate()
    {
        GameObject.Scene!.Renderable.Remove(this);
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
