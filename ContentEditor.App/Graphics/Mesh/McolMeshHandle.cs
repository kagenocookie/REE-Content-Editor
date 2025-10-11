using ReeLib;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class McolMeshHandle : MeshHandle
{
    public BvhData? BVH { get; }
    private GL GL { get; }

    internal McolMeshHandle(GL gl, MeshResourceHandle mesh, McolFile mcol) : base(mesh)
    {
        BVH = mcol.bvh;
        GL = gl;
    }

    internal McolMeshHandle(GL gl, MeshResourceHandle mesh, TerrFile terr) : base(mesh)
    {
        BVH = terr.bvh;
        GL = gl;
    }

    public override void Update()
    {
        if (BVH == null) return;

        var bvh = BVH;
        var first = Handle.Meshes.FirstOrDefault();
        if (bvh.triangles.Count == 0) {
            if (first is TriangleMesh) {
                Handle.Meshes.RemoveAt(0);
                first.Dispose();
            }
        } else {
            if (first is not TriangleMesh) {
                // TODO re-init the mesh part properly
                Handle.Meshes.Insert(0, new TriangleMesh(GL, [], []));
            }
        }

        Handle.RemoveMeshes(bvh.triangles.Count == 0 ? 0 : 1);
        var builder = new ShapeBuilder();

        foreach (var shape in bvh.spheres) builder.Add(shape.sphere);
        foreach (var shape in bvh.boxes) builder.Add(shape.box);
        foreach (var shape in bvh.capsules) builder.Add(shape.capsule);

        float[] vert = [];
        int[] inds = [];
        AABB bounds = AABB.MaxMin;
        builder.UpdateMesh(ref vert, ref inds, ref bounds);
        var mesh = new TriangleMesh(GL, vert, inds, bounds);
        Handle.Meshes.Add(mesh);
    }
}
