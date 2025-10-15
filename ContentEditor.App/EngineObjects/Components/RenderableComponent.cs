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
        get => IsStatic && !_worldSpaceBounds.IsInvalid ? _worldSpaceBounds : RecomputeWorldAABB();
    }

    internal AABB RecomputeWorldAABB()
    {
        var local = LocalBounds;
        var world = Transform.WorldTransform.ToSystem();
        // not necessarily "exactly" correct but close enough
        var p1 = Vector3.Transform(local.minpos, world);
        var p2 = Vector3.Transform(local.maxpos, world);
        return _worldSpaceBounds = new AABB(Vector3.Min(p1, p2), Vector3.Max(p1, p2));
    }

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
