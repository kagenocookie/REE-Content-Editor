using ContentEditor.App.Graphics;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using ReeLib;
using ReeLib.via;

namespace ContentEditor.App.Tooling.Navmesh;

public class McolInputGeomProvider : IInputGeomProvider
{
    public McolFile Mcol { get; }
    private AABB bounds;

    public McolInputGeomProvider(McolFile mcol)
    {
        if (mcol.bvh == null) {
            throw new Exception("Mcol is missing tree info");
        }

        Mcol = mcol;
        if (mcol.bvh.tree == null) {
            // the tree is not automatically read because we don't always need it, force it here
            mcol.bvh.ReadTree();
        }

        mesh = BuildMesh(mcol.bvh);
    }

    private RcTriMesh BuildMesh(BvhData bvh)
    {
        ShapeBuilder builder = new(ShapeBuilder.GeometryType.Filled, MeshLayout.PositionOnly);
        for (int i = 0; i < bvh.triangles.Count; ++i) {
            var tri = bvh.triangles[i];
            builder.Add(new Triangle(bvh.vertices[tri.posIndex1], bvh.vertices[tri.posIndex2], bvh.vertices[tri.posIndex3]));
        }
        foreach (var shape in bvh.boxes) {
            builder.Add(shape.box);
        }
        float[] vertFloats = [];
        int[] indices = [];
        AABB shapeBounds = AABB.MaxMin;
        builder.UpdateMesh(ref vertFloats, ref indices, ref shapeBounds);
        bounds = AABB.Combine([bounds, bvh.tree?.bounds ?? AABB.MaxMin]);

        return new RcTriMesh(vertFloats, indices);
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

    public RcVec3f GetMeshBoundsMax()
    {
        return bounds.maxpos;
    }

    public RcVec3f GetMeshBoundsMin()
    {
        return bounds.minpos;
    }

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