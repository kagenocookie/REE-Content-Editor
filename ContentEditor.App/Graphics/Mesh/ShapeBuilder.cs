using System.Numerics;
using ReeLib.via;
using Silk.NET.Maths;

namespace ContentEditor.App.Graphics;

public class ShapeBuilder
{
    private static readonly Vector3[] BoxTrianglePoints = [
        new(-1, -1, -1),    new(1, -1, -1),     new(1, -1, 1),
        new(-1, -1, -1),    new(1, -1, 1),      new(-1, -1, 1),
        new(-1, 1, 1),      new(-1, -1, 1),     new(1, -1, 1),
        new(-1, 1, 1),      new(1, -1, 1),      new(1, 1, 1),
        new(1, 1, 1),       new(1, -1, 1),      new(1, -1, -1),
        new(1, 1, 1),       new(1, -1, -1),     new(1, 1, -1),
        new(-1, 1, -1),     new(-1, 1, 1),      new(1, 1, 1),
        new(-1, 1, -1),     new(1, 1, 1),       new(1, 1, -1),
        new(-1, 1, -1),     new(-1, -1, -1),    new(-1, -1, 1),
        new(-1, 1, -1),     new(-1, -1, 1),     new(-1, 1, 1),
        new(-1, 1, -1),     new(1, 1, -1),      new(1, -1, -1),
        new(-1, 1, -1),     new(1, -1, -1),     new(-1, -1, -1),
    ];
    private static readonly int[] BoxIndices = Enumerable.Range(0, 36).ToArray();

    private List<AABB>? bounds;
    private List<OBB>? boxes;
    private List<Sphere>? spheres;
    private List<Capsule>? capsules;
    private List<Cylinder>? cylinders;
    public AABB Bounds = AABB.MaxMin;

    private float[]? VertexData;
    private int[]? Indices;
    public int VertexAttributeCount = 9;

    public bool Wire = true;

    #region Calculation helper methods
    private static int CalculateSemicirclePointCount(float radius) => (int)Math.Clamp((radius + 1.5f) * 4f, 8, 40);
    private static int CalculateSemicircleVertCount(float radius) => (CalculateSemicirclePointCount(radius) - 1) * 6;
    private static readonly int BoxVertCount = BoxTrianglePoints.Length;
    #endregion

    private void AllocateArray()
    {
        if (!Wire) {
            throw new NotImplementedException("Only wire shapes currently supported");
        }

        var count = GetTotalExpectedVertCount();
        if (VertexData?.Length == count && Indices?.Length == count)  return;

        VertexData = new float[count * VertexAttributeCount];
        Indices = new int[count];
    }

    private int GetTotalExpectedVertCount()
    {
        var count = ((bounds?.Count ?? 0) + (boxes?.Count ?? 0)) * BoxVertCount;
        if (spheres != null) foreach (var sphere in spheres) count += CalculateSemicircleVertCount(sphere.R) * 6;
        if (capsules != null) foreach (var capsule in capsules) count += CalculateSemicircleVertCount(capsule.R) * 8 + 4 * 6;
        if (cylinders != null) foreach (var cylinder in cylinders) count += CalculateSemicircleVertCount(cylinder.r) * 4 + 4 * 6;
        return count;
    }

    public void Add(AABB shape) => (bounds ??= new()).Add(shape);
    public void Add(OBB shape) => (boxes ??= new()).Add(shape);
    public void Add(Sphere shape) => (spheres ??= new()).Add(shape);
    public void Add(Capsule shape) => (capsules ??= new()).Add(shape);
    public void Add(Cylinder shape) => (cylinders ??= new()).Add(shape);

    public void UpdateMesh(ref float[] vertices, ref int[] indices, ref AABB bound)
    {
        VertexData = vertices;
        Indices = indices;
        AllocateArray();
        int index = 0;
        if (bounds != null) foreach (var obj in bounds) InsertAabb(ref index, obj);
        if (boxes != null) foreach (var obj in boxes) InsertBox(ref index, obj);
        if (spheres != null) foreach (var obj in spheres) InsertSphere(ref index, obj);
        if (capsules != null) foreach (var obj in capsules) InsertCapsule(ref index, obj);
        if (cylinders != null) foreach (var obj in cylinders) InsertCylinder(ref index, obj);
        vertices = VertexData!;
        indices = Indices!;
        bound = Bounds;

        VertexData = null;
        Indices = null;
        Bounds = AABB.MaxMin;
    }

    // public TriangleMesh Create(GL gl)
    // {
    //     float[] vert = [];
    //     int[] inds = [];
    //     AABB bounds = AABB.MaxMin;
    //     UpdateMesh(ref vert, ref inds, ref bounds);
    //     return new TriangleMesh(gl, vert, inds, bounds);
    // }

    public static void CreateSingle(AABB shape, ref float[] vertices, ref int[] indices, ref AABB bound)
    {
        // could be optimized to not need a concrete shape builder instance but probably not a meaningful difference
        var b = new ShapeBuilder();
        b.Add(shape);
        b.UpdateMesh(ref vertices, ref indices, ref bound);
    }

    public static void CreateSingle(OBB shape, ref float[] vertices, ref int[] indices, ref AABB bound)
    {
        var b = new ShapeBuilder();
        b.Add(shape);
        b.UpdateMesh(ref vertices, ref indices, ref bound);
    }

    public static void CreateSingle(Sphere shape, ref float[] vertices, ref int[] indices, ref AABB bound)
    {
        var b = new ShapeBuilder();
        b.Add(shape);
        b.UpdateMesh(ref vertices, ref indices, ref bound);
    }

    public static void CreateSingle(Capsule shape, ref float[] vertices, ref int[] indices, ref AABB bound)
    {
        var b = new ShapeBuilder();
        b.Add(shape);
        b.UpdateMesh(ref vertices, ref indices, ref bound);
    }

    public static void CreateSingle(Cylinder shape, ref float[] vertices, ref int[] indices, ref AABB bound)
    {
        var b = new ShapeBuilder();
        b.Add(shape);
        b.UpdateMesh(ref vertices, ref indices, ref bound);
    }

    public void Clear()
    {
        bounds?.Clear();
        boxes?.Clear();
        spheres?.Clear();
        capsules?.Clear();
        cylinders?.Clear();
        Bounds = AABB.MaxMin;
    }

    private void InsertAabb(ref int index, AABB aabb)
    {
        var center = aabb.Center;
        var extent = aabb.Size / 2;
        for (int i = 0; i < BoxTrianglePoints.Length; ++i) {
            AddBoxUVAttributes(index, i);
            InsertVertex(ref index, center + BoxTrianglePoints[i] * extent);
        }
    }

    private void InsertBox(ref int index, OBB box)
    {
        var center = box.Coord.Multiply(Vector3.Zero);
        var extent = box.Extent;
        var baseAabb = new AABB() { minpos = center - extent, maxpos = center + extent };

        // an OBB is just an AABB with a transformation applied to it
        for (int i = 0; i < BoxTrianglePoints.Length; ++i) {
            var point = box.Coord.Multiply(BoxTrianglePoints[i] * extent);
            AddBoxUVAttributes(index, i);
            InsertVertex(ref index, point);
        }
    }

    public void InsertSphere(ref int index, Sphere sphere)
    {
        StoreWireSemiCircle(ref index, sphere.R, sphere.pos, new Vector3(0, 0, 1), new Vector3(0, 0, 0));
        StoreWireSemiCircle(ref index, sphere.R, sphere.pos, new Vector3(0, 0, 1), new Vector3(0, 1, 0));
        StoreWireSemiCircle(ref index, sphere.R, sphere.pos, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
        StoreWireSemiCircle(ref index, sphere.R, sphere.pos, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));
        StoreWireSemiCircle(ref index, sphere.R, sphere.pos, new Vector3(1, 0, 0), new Vector3(-0.5f, 0, 0.5f));
        StoreWireSemiCircle(ref index, sphere.R, sphere.pos, new Vector3(1, 0, 0), new Vector3(0.5f, 0, 0.5f));
    }

    public void InsertCapsule(ref int index, Capsule capsule)
    {
        var up = (capsule.p1 - capsule.p0);
        var center = (capsule.p1 + capsule.p0) * 0.5f;
        var alignedUp = Vector3.UnitY * (up.Length() * 0.5f);
        var startIndex = index;
        StoreWireSemiCircle(ref index, capsule.R, alignedUp, new Vector3(0, 0, 1), new Vector3(0, 0, 0));
        StoreWireSemiCircle(ref index, capsule.R, alignedUp, new Vector3(1, 0, 0), new Vector3(0.5f, 0, 0));
        StoreWireSemiCircle(ref index, capsule.R, alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
        StoreWireSemiCircle(ref index, capsule.R, alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));

        StoreWireSemiCircle(ref index, capsule.R, -alignedUp, new Vector3(0, 0, 1), new Vector3(0, 1, 0));
        StoreWireSemiCircle(ref index, capsule.R, -alignedUp, new Vector3(1, 0, 0), new Vector3(0.5f, 1, 0));
        StoreWireSemiCircle(ref index, capsule.R, -alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
        StoreWireSemiCircle(ref index, capsule.R, -alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));

        var side1 = capsule.R * Vector3.Cross(Vector3.UnitY, Vector3.UnitX);
        var side2 = capsule.R * Vector3.Cross(Vector3.UnitY, Vector3.UnitZ);
        var sideOff1 = Vector3.Normalize(Vector3.Cross(side1, Vector3.UnitY)) * 0.01f;
        var sideOff2 = Vector3.Normalize(Vector3.Cross(side2, Vector3.UnitY)) * 0.01f;
        InsertQuad(ref index, -alignedUp + side1 + sideOff1, -alignedUp + side1 - sideOff1, alignedUp + side1 - sideOff1, alignedUp + side1 + sideOff1);
        InsertQuad(ref index, -alignedUp - side1 + sideOff1, -alignedUp - side1 - sideOff1, alignedUp - side1 - sideOff1, alignedUp - side1 + sideOff1);
        InsertQuad(ref index, -alignedUp + side2 + sideOff2, -alignedUp + side2 - sideOff2, alignedUp + side2 - sideOff2, alignedUp + side2 + sideOff2);
        InsertQuad(ref index, -alignedUp - side2 + sideOff2, -alignedUp - side2 - sideOff2, alignedUp - side2 - sideOff2, alignedUp - side2 + sideOff2);

        var rotation = Quaternion<float>.Normalize(TransformExtensions.CreateFromToQuaternion(Vector3.Normalize(alignedUp), Vector3.Normalize(up))).ToSystem();
        TransformVertices(startIndex, index, rotation, center);
    }

    public void InsertCylinder(ref int index, Cylinder cylinder)
    {
        var up = (cylinder.p1 - cylinder.p0);
        var center = (cylinder.p1 + cylinder.p0) * 0.5f;
        var alignedUp = Vector3.UnitY * (up.Length() * 0.5f);
        var startIndex = index;
        // mostly identical to capsule, except without the two spherical caps on both sides
        StoreWireSemiCircle(ref index, cylinder.r, alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
        StoreWireSemiCircle(ref index, cylinder.r, alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));

        StoreWireSemiCircle(ref index, cylinder.r, -alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
        StoreWireSemiCircle(ref index, cylinder.r, -alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));

        var side1 = cylinder.r * Vector3.Cross(Vector3.UnitY, Vector3.UnitX);
        var side2 = cylinder.r * Vector3.Cross(Vector3.UnitY, Vector3.UnitZ);
        var sideOff1 = Vector3.Normalize(Vector3.Cross(side1, Vector3.UnitY)) * 0.01f;
        var sideOff2 = Vector3.Normalize(Vector3.Cross(side2, Vector3.UnitY)) * 0.01f;
        InsertQuad(ref index, cylinder.p0 + side1 + sideOff1, cylinder.p0 + side1 - sideOff1, cylinder.p1 + side1 - sideOff1, cylinder.p1 + side1 + sideOff1);
        InsertQuad(ref index, cylinder.p0 - side1 + sideOff1, cylinder.p0 - side1 - sideOff1, cylinder.p1 - side1 - sideOff1, cylinder.p1 - side1 + sideOff1);
        InsertQuad(ref index, cylinder.p0 + side2 + sideOff2, cylinder.p0 + side2 - sideOff2, cylinder.p1 + side2 - sideOff2, cylinder.p1 + side2 + sideOff2);
        InsertQuad(ref index, cylinder.p0 - side2 + sideOff2, cylinder.p0 - side2 - sideOff2, cylinder.p1 - side2 - sideOff2, cylinder.p1 - side2 + sideOff2);

        var rotation = Quaternion<float>.Normalize(TransformExtensions.CreateFromToQuaternion(Vector3.Normalize(alignedUp), Vector3.Normalize(up))).ToSystem();
        TransformVertices(startIndex, index, rotation, center);
    }

    private void TransformVertices(int start, int end, Quaternion rotation, Vector3 offset)
    {
        for (int i = start; i < end; ++i) {
            var point = new Vector3(VertexData![i * VertexAttributeCount + 0], VertexData[i * VertexAttributeCount + 1], VertexData[i * VertexAttributeCount + 2]);
            point = Vector3.Transform(point, rotation) + offset;

            VertexData[i * VertexAttributeCount + 0] = point.X;
            VertexData[i * VertexAttributeCount + 1] = point.Y;
            VertexData[i * VertexAttributeCount + 2] = point.Z;
        }
    }

    private void AddBoxUVAttributes(int index, int vertex)
    {
        var side = vertex / 6;
        var seq = vertex % 6;

        // uv
        VertexData![index * VertexAttributeCount + 3] = seq is 1 or 2 or 4 ? 1 : 0;
        VertexData[index * VertexAttributeCount + 4] = seq is 0 or 1 or 3 ? 1 : 0;
        // normals - note: correctness untested
        VertexData[index * VertexAttributeCount + 5] = side is 0 or 1 ? 1 : 0;
        VertexData[index * VertexAttributeCount + 6] = side is 2 or 3 ? 1 : 0;
        VertexData[index * VertexAttributeCount + 7] = side is 4 or 5 ? 1 : 0;
    }

    private void StoreWireSemiCircle(ref int index, float radius, Vector3 center, Vector3 left, Vector3 rotEuler)
    {
        // in case it's not obvious, I'm not really sure what I'm doing here; it works, but could be improved
        var segments = CalculateSemicirclePointCount(radius) - 1;
        var segMult = 1f / segments * MathF.PI;
        var leftNorm = Vector3.Normalize(left) * 0.008f; // should probably already be normalized but just in case
        var rot = Quaternion.CreateFromYawPitchRoll(rotEuler.X * MathF.PI, rotEuler.Y * MathF.PI, rotEuler.Z * MathF.PI);
        for (int i = 0; i < segments; ++i) {
            var angle1 = i * segMult;
            var angle2 = (i + 1) * segMult;
            var p1 = center + radius * Vector3.Transform(new Vector3(MathF.Cos(angle1), MathF.Sin(angle1), 0), rot);
            var p2 = center + radius * Vector3.Transform(new Vector3(MathF.Cos(angle2), MathF.Sin(angle2), 0), rot);

            InsertVertex(ref index, (p1 + leftNorm));
            InsertVertex(ref index, (p1 - leftNorm));
            InsertVertex(ref index, (p2 - leftNorm));
            InsertVertex(ref index, (p2 - leftNorm));
            InsertVertex(ref index, (p1 + leftNorm));
            InsertVertex(ref index, (p2 + leftNorm));
        }
    }

    private void InsertQuad(ref int index, Vector3 vec1, Vector3 vec2, Vector3 vec3, Vector3 vec4)
    {
        InsertVertex(ref index, vec1);
        InsertVertex(ref index, vec2);
        InsertVertex(ref index, vec3);
        InsertVertex(ref index, vec1);
        InsertVertex(ref index, vec3);
        InsertVertex(ref index, vec4);
    }

    private void InsertVertex(ref int index, Vector3 vec)
    {
        VertexData![index * VertexAttributeCount + 0] = vec.X;
        VertexData[index * VertexAttributeCount + 1] = vec.Y;
        VertexData[index * VertexAttributeCount + 2] = vec.Z;
        VertexData[index * VertexAttributeCount + 6] = 1; // default normal = (0, 1, 0)
        VertexData[index * VertexAttributeCount + 8] = BitConverter.Int32BitsToSingle(index);
        Indices![index] = index;
        index++;
        Bounds = Bounds.Extend(vec);
    }

    /// <summary>
    /// Attempt to add an unknown shape type.
    /// </summary>
    public void AddBoxed(object shape)
    {
        switch (shape) {
            case AABB obj: Add(obj); return;
            case OBB obj: Add(obj); return;
            case Sphere obj: Add(obj); return;
            case Capsule obj: Add(obj); return;
            case Cylinder obj: Add(obj); return;
            default:
                Logger.Error("Unsupported shape type " + (shape?.GetType().Name ?? "NULL"));
                break;
        }
    }
}