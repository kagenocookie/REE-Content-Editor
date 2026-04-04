using System.Numerics;
using ContentEditor.App.Graphics;
using ReeLib.via;

namespace ContentEditor.App;

public sealed class PickableData
{
    public List<PickItem> Candidates { get; } = new();

    public Ray queryRay;
    private Vector3 inverseRayDir;

    /// <param name="context">The source object context of this item.</param>
    /// <param name="contextId">The context's internal ID for determining which sub-object was clicked.</param>
    /// <param name="mesh">The mesh of this item.</param>
    /// <param name="matrix">The world space matrix of the mesh.</param>
    public readonly record struct PickItem(Component context, int contextId, MeshHandle mesh, Matrix4x4 matrix);

    public void TryAdd(Component context, int contextId, MeshHandle mesh, in Matrix4x4 mat, in AABB worldBounds)
    {
        if (!IsPlausiblePick(worldBounds)) {
            return;
        }

        Candidates.Add(new PickItem(context, contextId, mesh, mat));
    }

    public void Clear()
    {
        Candidates.Clear();
    }

    public bool IsPlausiblePick(AABB bounds)
    {
        // https://gist.github.com/DomNomNom/46bb1ce47f68d255fd5d
        var tMin = (bounds.minpos - queryRay.from) * inverseRayDir;
        var tMax = (bounds.maxpos - queryRay.from) * inverseRayDir;
        var t1 = Vector3.Min(tMin, tMax);
        var t2 = Vector3.Max(tMin, tMax);
        float tNear = MathF.Max(MathF.Max(t1.X, t1.Y), t1.Z);
        float tFar = MathF.Min(MathF.Min(t2.X, t2.Y), t2.Z);
        return tFar >= tNear;
    }

    public void SetRay(Ray ray)
    {
        queryRay = ray;
        // pre-compute the inverse so we can later avoid div by 0 more easily
        inverseRayDir = new Vector3(
            ray.dir.X == 0 ? float.MaxValue : 1 / ray.dir.X,
            ray.dir.Y == 0 ? float.MaxValue : 1 / ray.dir.Y,
            ray.dir.Z == 0 ? float.MaxValue : 1 / ray.dir.Z
        );
    }
}
