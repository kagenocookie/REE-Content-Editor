using System.Numerics;
using ReeLib;
using ReeLib.Rcol;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class RcolMeshHandle : MeshHandle
{
    public RcolFile File { get; }
    private GL GL { get; }

    public readonly Dictionary<int, bool> VisibilityToggles = new();

    internal RcolMeshHandle(GL gl, MeshResourceHandle mesh, RcolFile file) : base(mesh)
    {
        File = file;
        GL = gl;
    }

    public override void Update()
    {
        Handle.RemoveMeshes(0);

        var builder = new ShapeBuilder();
        // TODO add proper toggling support
        foreach (var group in File.Groups) {
            foreach (var shape in group.Shapes) {
                if (shape.shape != null) {
                    builder.AddBoxed(shape.shape);
                }
            }
            foreach (var shape in group.ExtraShapes) {
                if (shape.shape != null) {
                    builder.AddBoxed(shape.shape);
                }
            }
        }

        float[] vert = [];
        int[] inds = [];
        AABB bounds = AABB.MaxMin;
        builder.UpdateMesh(ref vert, ref inds, ref bounds);
        var mesh = new TriangleMesh(GL, vert, inds, bounds);
        Handle.Meshes.Add(mesh);
    }
}
