using System.Numerics;
using System.Runtime.InteropServices;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using ReeLib;

namespace ContentEditor.App.Tooling.Navmesh;

public class McolInputGeomProvider : IInputGeomProvider
{
    public McolFile Mcol { get; }

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

    private static RcTriMesh BuildMesh(BvhData bvh)
    {
        var indices = new int[bvh.triangles.Count * 3];
        for (int i = 0; i < bvh.triangles.Count; ++i) {
            indices[i * 3 + 0] = bvh.triangles[i].posIndex1;
            indices[i * 3 + 1] = bvh.triangles[i].posIndex2;
            indices[i * 3 + 2] = bvh.triangles[i].posIndex3;
        }
        var maxVertIndex = indices.Max() + 1;
        // TODO handle other shapes

        var vertFloats = MemoryMarshal.Cast<Vector3, float>(CollectionsMarshal.AsSpan(bvh.vertices).Slice(0, maxVertIndex)).ToArray();
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
        return Mcol.bvh?.tree?.bounds.maxpos ?? new Vector3();
    }

    public RcVec3f GetMeshBoundsMin()
    {
        return Mcol.bvh?.tree?.bounds.minpos ?? new Vector3();
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