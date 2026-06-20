using ContentEditor.App.Graphics;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using ReeLib.via;

namespace ContentEditor.App.Tooling.Navmesh;

public class ShapeGeomProvider : IInputGeomProvider
{
    private ShapeBuilder builder = new(ShapeBuilder.GeometryType.Filled, MeshLayout.PositionOnly);

    private AABB bounds;

    public ShapeGeomProvider(OBB shape)
    {
        builder.Add(shape);
        (mesh, bounds) = BuildMesh(builder);
    }

    public ShapeGeomProvider(AABB shape)
    {
        builder.Add(shape);
        (mesh, bounds) = BuildMesh(builder);
    }

    public ShapeGeomProvider(Cylinder shape)
    {
        builder.Add(shape);
        (mesh, bounds) = BuildMesh(builder);
    }

    public ShapeGeomProvider(Cone shape)
    {
        builder.Add(shape);
        (mesh, bounds) = BuildMesh(builder);
    }

    private static (RcTriMesh, AABB) BuildMesh(ShapeBuilder builder)
    {
        float[] vert = [];
        int[] indices = [];
        AABB bounds = AABB.MaxMin;
        builder.UpdateMesh(ref vert, ref indices, ref bounds);
        return (new RcTriMesh(vert, indices), bounds);
    }

    private RcTriMesh mesh;

    public RcTriMesh GetMesh()
    {
        return mesh;
    }

    public IEnumerable<RcTriMesh> Meshes()
    {
        yield return GetMesh();
    }

    public RcVec3f GetMeshBoundsMax() => bounds.maxpos;
    public RcVec3f GetMeshBoundsMin() => bounds.minpos;

    public void AddConvexVolume(RcConvexVolume convexVolume)
    {
        throw new NotImplementedException();
    }

    public void AddOffMeshConnection(RcVec3f start, RcVec3f end, float radius, bool bidir, int area, int flags)
    {
        throw new NotImplementedException();
    }

    public IList<RcConvexVolume> ConvexVolumes()
    {
        throw new NotImplementedException();
    }

    public List<RcOffMeshConnection> GetOffMeshConnections()
    {
        throw new NotImplementedException();
    }

    public void RemoveOffMeshConnections(Predicate<RcOffMeshConnection> filter)
    {
        throw new NotImplementedException();
    }
}