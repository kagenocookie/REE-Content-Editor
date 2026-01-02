using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using ReeLib.via;

namespace ContentEditor.App.Tooling.Navmesh;

public class InputGeomCombiner : IInputGeomProvider
{
    private RcTriMesh mesh;
    private AABB bounds;
    private readonly IEnumerable<IInputGeomProvider> geoms;

    public InputGeomCombiner(IEnumerable<IInputGeomProvider> geoms)
    {
        this.geoms = geoms;
        mesh = BuildMesh(geoms);
    }

    private RcTriMesh BuildMesh(IEnumerable<IInputGeomProvider> geoms)
    {
        bounds = AABB.MaxMin;
        var verts = new List<float>();
        var indices = new List<int>();
        foreach (var geo in geoms) {
            foreach (var mesh in geo.Meshes()) {
                var vert = mesh.GetVerts();
                var tri = mesh.GetTris();
                var vertOffset = verts.Count / 3;
                verts.AddRange(vert);
                foreach (var ind in tri) {
                    indices.Add(vertOffset + ind);
                }
                bounds = bounds.Extend(geo.GetMeshBoundsMin()).Extend(geo.GetMeshBoundsMax());
            }
        }

        return new RcTriMesh(verts.ToArray(), indices.ToArray());
    }

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