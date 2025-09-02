using System.Numerics;
using ReeLib;
using ReeLib.Rcol;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class McolMeshHandle : MeshHandle
{
    public McolFile Mcol { get; }
    private GL GL { get; }
    private readonly Dictionary<BaseModel, ShapeMesh> meshMapping = new();

    internal McolMeshHandle(GL gl, MeshResourceHandle mesh, McolFile mcol) : base(mesh)
    {
        Mcol = mcol;
        GL = gl;
    }

    private readonly HashSet<KeyValuePair<BaseModel, ShapeMesh>> _unhandledShapes = new();

    public override void Update()
    {
        if (Mcol.bvh == null) return;

        var bvh = Mcol.bvh;
        var first = Handle.Meshes.FirstOrDefault();
        if (bvh.triangles.Count == 0) {
            if (first is TriangleMesh) {
                Handle.Meshes.RemoveAt(0);
                first.Dispose();
            }
        } else {
            if (first is not TriangleMesh) {
                Handle.Meshes.Insert(0, new TriangleMesh(GL, [], []));
            }
        }

        // shapes commented out for now - too slow
        return;
        _unhandledShapes.Clear();
        foreach (var kv in meshMapping) _unhandledShapes.Add(kv);

        foreach (var obj in bvh.spheres) {
            if (meshMapping.TryGetValue(obj, out var shape)) {
                shape.SetWireShape(obj.sphere, ShapeType.Sphere);
                _unhandledShapes.Remove(new KeyValuePair<BaseModel, ShapeMesh>(obj, shape));
            } else {
                meshMapping[obj] = shape = new ShapeMesh(GL);
                shape.SetWireShape(obj.sphere, ShapeType.Sphere);
                Handle.Meshes.Add(shape);
            }
        }
        foreach (var obj in bvh.boxes) {
            if (meshMapping.TryGetValue(obj, out var shape)) {
                shape.SetShape(obj.box);
                _unhandledShapes.Remove(new KeyValuePair<BaseModel, ShapeMesh>(obj, shape));
            } else {
                meshMapping[obj] = shape = new ShapeMesh(GL);
                shape.SetShape(obj.box);
                Handle.Meshes.Add(shape);
            }
        }
        foreach (var obj in bvh.capsules) {
            if (meshMapping.TryGetValue(obj, out var shape)) {
                shape.SetWireShape(obj.capsule, ShapeType.Capsule);
                _unhandledShapes.Remove(new KeyValuePair<BaseModel, ShapeMesh>(obj, shape));
            } else {
                meshMapping[obj] = shape = new ShapeMesh(GL);
                shape.SetWireShape(obj.capsule, ShapeType.Capsule);
                Handle.Meshes.Add(shape);
            }
        }

        foreach (var (data, shape) in _unhandledShapes) {
            shape.Dispose();
            meshMapping.Remove(data);
            Handle.Meshes.Remove(shape);
        }
    }
}
