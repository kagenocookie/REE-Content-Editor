using ReeLib;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class HeightFieldMeshHandle : MeshHandle
{
    private GL GL { get; }
    public BaseHeightFieldFile File { get; }

    internal HeightFieldMeshHandle(GL gl, MeshResourceHandle mesh, BaseHeightFieldFile file) : base(mesh)
    {
        GL = gl;
        File = file;
    }

    public override void Update()
    {
        var layout = MeshLayout.Get(MeshAttributeFlag.Position);
        var builder = new ShapeBuilder(ShapeBuilder.GeometryType.Line, layout);
        var min = File.min with { Y = 0 };
        for (int x = 0; x < File.splitCount; ++x) {
            for (int y = 0; y < File.splitCount; ++y) {
                var pt1 = File[x, y] + min;
                var pt2 = File[x + 1, y] + min;
                var pt3 = File[x, y + 1] + min;
                var pt4 = File[x + 1, y + 1] + min;
                builder.Add(new ReeLib.via.LineSegment(pt1, pt2));
                builder.Add(new ReeLib.via.LineSegment(pt1, pt3));
                if (x == File.splitCount) builder.Add(new ReeLib.via.LineSegment(pt3, pt4));
                if (y == File.splitCount) builder.Add(new ReeLib.via.LineSegment(pt2, pt4));
            }
        }

        float[] vert = [];
        int[] inds = [];
        AABB bounds = AABB.MaxMin;
        builder.UpdateMesh(ref vert, ref inds, ref bounds);
        var mesh = new TriangleMesh(GL, vert, inds, bounds, layout) { MeshType = PrimitiveType.Lines };
        Handle.Meshes.Add(mesh);
    }
}
