using System.Numerics;
using ReeLib.MplyMesh;
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
    private static readonly Vector3[] BoxLinePoints = [
        new(-1, -1, -1),    new(1, -1, -1),     new(1, -1, -1),     new(1, -1, 1),    new(1, -1, 1),  new (-1, -1, 1),
        new(-1, 1, 1),      new(-1, -1, 1),     new(-1, -1, 1),     new(1, -1, 1),    new(1, -1, 1),  new(1, 1, 1),
        new(1, 1, 1),       new(1, -1, 1),      new(1, -1, 1),      new(1, -1, -1),   new(1, -1, -1), new(1, 1, -1),
        new(-1, 1, -1),     new(-1, 1, 1),      new(-1, 1, 1),      new(1, 1, 1),     new(1, 1, 1),   new(1, 1, -1),
        new(-1, 1, -1),     new(-1, -1, -1),    new(-1, -1, -1),    new(-1, -1, 1),   new(-1, -1, 1), new(-1, 1, 1),
        new(-1, 1, -1),     new(1, 1, -1),      new(1, 1, -1),      new(1, -1, -1),   new(1, -1, -1), new(-1, -1, -1),
    ];
    private static readonly int[] BoxIndices = Enumerable.Range(0, 36).ToArray();

    public AABB Bounds = AABB.MaxMin;
    private readonly List<ShapeBuilderShapeBase> Shapes = new();

    private T GetShapeContainer<TShape, T>() where TShape : unmanaged where T : ShapeBuilderShapeType<TShape>, new()
    {
        foreach (var container in Shapes) {
            if (container is T target) return target;
        }

        var inst = new T();
        Shapes.Add(inst);
        return inst;
    }

    public bool IsEmpty => Shapes.Count == 0;
    public int ShapeCount => Shapes.Sum(sc => sc.Count);

    private float[]? VertexData;
    private int[]? Indices;
    public int VertexAttributeCount = 9;

    public GeometryType GeoType = GeometryType.FakeWire;

    public int CalculateShapeHash()
    {
        int hash = 17;
        hash = unchecked(hash * 31 + ShapeCount);
        foreach (var cont in Shapes) {
            hash = (int)HashCode.Combine(hash, cont.CalculateShapeHash());
        }
        return hash;
    }

    public enum GeometryType
    {
        FakeWire,
        Line,
        Filled,
    }

    #region Calculation helper methods
    private static int CalculateSemicirclePointCount(float radius) => (int)Math.Clamp((radius + 1.5f) * 4f, 8, 40);

    private static int CalculateSemicircleVertCount(float radius) => (CalculateSemicirclePointCount(radius) - 1) * 6;
    private static int CalculateLineSemicircleVertCount(float radius) => (CalculateSemicirclePointCount(radius) - 1) * 2;
    #endregion


    private void AllocateArray()
    {
        int count = Shapes.Sum(sc => sc.CalculateVertexCount(this));

        if (VertexData?.Length == count && Indices?.Length == count) return;

        VertexData = new float[count * VertexAttributeCount];
        Indices = new int[count];
    }

    public void Add(LineSegment shape) => GetShapeContainer<LineSegment, ShapeBuilderLineSegment>().shapes.Add(shape);
    public void Add(AABB shape) => GetShapeContainer<AABB, ShapeBuilderAABB>().shapes.Add(shape);
    public void Add(OBB shape) => GetShapeContainer<OBB, ShapeBuilderOBB>().shapes.Add(shape);
    public void Add(Sphere shape) => GetShapeContainer<Sphere, ShapeBuilderSphere>().shapes.Add(shape);
    public void Add(Capsule shape) => GetShapeContainer<Capsule, ShapeBuilderCapsule>().shapes.Add(shape);
    public void Add(Cylinder shape) => GetShapeContainer<Cylinder, ShapeBuilderCylinder>().shapes.Add(shape);
    public void Add(Cone shape) => GetShapeContainer<Cone, ShapeBuilderCone>().shapes.Add(shape);
    public void AddCircle(Vector3 position, Vector3 forward, float radius)
    {
        GetShapeContainer<(Vector3 pos, Vector3 dir, float radius), ShapeBuilderCircle>().shapes.Add((position, forward, radius));
    }

    public void UpdateMesh(ref float[] vertices, ref int[] indices, ref AABB bound)
    {
        VertexData = vertices;
        Indices = indices;
        AllocateArray();
        int index = 0;
        foreach (var sc in Shapes) sc.Build(ref index, this);
        vertices = VertexData!;
        indices = Indices!;
        bound = Bounds;

        VertexData = null;
        Indices = null;
        Bounds = AABB.MaxMin;
    }

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
        foreach (var shape in Shapes) {
            shape.Clear();
        }
        Bounds = AABB.MaxMin;
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

    private void StoreLineSemiCircle(ref int index, float radius, Vector3 center, Vector3 rotEuler)
    {
        // in case it's not obvious, I'm not really sure what I'm doing here; it works, but could be improved
        var segments = CalculateSemicirclePointCount(radius) - 1;
        var segMult = 1f / segments * MathF.PI;
        var rot = Quaternion.CreateFromYawPitchRoll(rotEuler.X * MathF.PI, rotEuler.Y * MathF.PI, rotEuler.Z * MathF.PI);
        for (int i = 0; i < segments; ++i) {
            var angle1 = i * segMult;
            var angle2 = (i + 1) * segMult;
            var p1 = center + radius * Vector3.Transform(new Vector3(MathF.Cos(angle1), MathF.Sin(angle1), 0), rot);
            var p2 = center + radius * Vector3.Transform(new Vector3(MathF.Cos(angle2), MathF.Sin(angle2), 0), rot);

            InsertLine(ref index, p1, p2);
        }
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
    private void StoreFilledCircle(ref int index, float radius, int segments, Vector3 center, Vector3 up)
    {
        var rot = TransformExtensions.CreateFromToQuaternion(Vector3.UnitY, up).ToSystem();
        var segMult = 1f / segments * MathF.PI;
        for (int i = 0; i < segments; ++i) {
            var angle1 = i * segMult;
            var angle2 = (i + 1) * segMult;
            var p1 = center + radius * Vector3.Transform(new Vector3(MathF.Cos(angle1), 0, MathF.Sin(angle1)), rot);
            var p2 = center + radius * Vector3.Transform(new Vector3(MathF.Cos(angle2), 0, MathF.Sin(angle2)), rot);

            InsertVertex(ref index, p1);
            InsertVertex(ref index, p2);
            InsertVertex(ref index, center);
        }
    }
    private void StoreFilledTube(ref int index, Cone param)
    {
        var segments = (CalculateSemicirclePointCount(param.r0) - 1) * 2;
        var rot = Quaternion.Identity;
        var segMult = 2f / segments * MathF.PI;
        for (int i = 0; i < segments; ++i) {
            var angle1 = i * segMult;
            var angle2 = (i + 1) * segMult;
            var p1 = param.p0 + param.r0 * Vector3.Transform(new Vector3(MathF.Cos(angle1), 0, MathF.Sin(angle1)), rot);
            var p2 = param.p0 + param.r0 * Vector3.Transform(new Vector3(MathF.Cos(angle2), 0, MathF.Sin(angle2)), rot);
            var p3 = param.p1 + param.r1 * Vector3.Transform(new Vector3(MathF.Cos(angle1), 0, MathF.Sin(angle1)), rot);
            var p4 = param.p1 + param.r1 * Vector3.Transform(new Vector3(MathF.Cos(angle2), 0, MathF.Sin(angle2)), rot);

            InsertQuad(ref index, p1, p2, p4, p3);
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

    private void InsertLine(ref int index, Vector3 from, Vector3 to)
    {
        InsertVertex(ref index, from);
        InsertVertex(ref index, to);
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
            case LineSegment obj: Add(obj); return;
            default:
                Logger.Error("Unsupported shape type " + (shape?.GetType().Name ?? "NULL"));
                break;
        }
    }

    public abstract class ShapeBuilderShapeBase
    {
        public abstract int CalculateVertexCount(ShapeBuilder builder);
        public abstract int Count { get; }

        public abstract void Build(ref int index, ShapeBuilder builder);
        public abstract int CalculateShapeHash();
        public abstract void Clear();
    }

    public abstract class ShapeBuilderShapeType<T> : ShapeBuilderShapeBase where T : unmanaged
    {
        public readonly List<T> shapes = new();
        public override int Count => shapes.Count;

        public override void Clear() => shapes.Clear();
    }

    public class ShapeBuilderSphere : ShapeBuilderShapeType<Sphere>
    {
        public override void Build(ref int index, ShapeBuilder builder)
        {
            if (builder.GeoType == GeometryType.FakeWire) {
                foreach (var shape in shapes) {
                    builder.StoreWireSemiCircle(ref index, shape.R, shape.pos, new Vector3(0, 0, 1), new Vector3(0, 0, 0));
                    builder.StoreWireSemiCircle(ref index, shape.R, shape.pos, new Vector3(0, 0, 1), new Vector3(0, 1, 0));
                    builder.StoreWireSemiCircle(ref index, shape.R, shape.pos, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
                    builder.StoreWireSemiCircle(ref index, shape.R, shape.pos, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));
                    builder.StoreWireSemiCircle(ref index, shape.R, shape.pos, new Vector3(1, 0, 0), new Vector3(-0.5f, 0, 0.5f));
                    builder.StoreWireSemiCircle(ref index, shape.R, shape.pos, new Vector3(1, 0, 0), new Vector3(0.5f, 0, 0.5f));
                }
            } else {
                foreach (var shape in shapes) {
                    builder.StoreLineSemiCircle(ref index, shape.R, shape.pos, new Vector3(0, 0, 0));
                    builder.StoreLineSemiCircle(ref index, shape.R, shape.pos, new Vector3(0, 1, 0));
                    builder.StoreLineSemiCircle(ref index, shape.R, shape.pos, new Vector3(0.5f, 0.5f, 0));
                    builder.StoreLineSemiCircle(ref index, shape.R, shape.pos, new Vector3(0.5f, -0.5f, 0));
                    builder.StoreLineSemiCircle(ref index, shape.R, shape.pos, new Vector3(-0.5f, 0, 0.5f));
                    builder.StoreLineSemiCircle(ref index, shape.R, shape.pos, new Vector3(0.5f, 0, 0.5f));
                }
            }
        }

        public override int CalculateVertexCount(ShapeBuilder builder)
        {
            int count = 0;
            if (builder.GeoType == GeometryType.Line) {
                foreach (var sphere in shapes) count += CalculateLineSemicircleVertCount(sphere.R) * 6;
            } else {
                foreach (var sphere in shapes) count += CalculateSemicircleVertCount(sphere.R) * 6;
            }
            return count;
        }

        public override int CalculateShapeHash()
        {
            int hash = 17;
            foreach (var obj in shapes) hash = (int)HashCode.Combine(hash, obj.pos, obj.r);
            return hash;
        }
    }
    public class ShapeBuilderCapsule : ShapeBuilderShapeType<Capsule>
    {
        public override void Build(ref int index, ShapeBuilder builder)
        {
            var geoType = builder.GeoType;
            foreach (var shape in shapes) {
                var up = (shape.p1 - shape.p0);
                if (up == Vector3.Zero) up = new Vector3(0, 0.001f, 0);
                var center = (shape.p1 + shape.p0) * 0.5f;
                var alignedUp = Vector3.UnitY * (up.Length() * 0.5f);
                var startIndex = index;
                if (geoType == GeometryType.FakeWire) {
                    builder.StoreWireSemiCircle(ref index, shape.R, alignedUp, new Vector3(0, 0, 1), new Vector3(0, 0, 0));
                    builder.StoreWireSemiCircle(ref index, shape.R, alignedUp, new Vector3(1, 0, 0), new Vector3(0.5f, 0, 0));
                    builder.StoreWireSemiCircle(ref index, shape.R, alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
                    builder.StoreWireSemiCircle(ref index, shape.R, alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));

                    builder.StoreWireSemiCircle(ref index, shape.R, -alignedUp, new Vector3(0, 0, 1), new Vector3(0, 1, 0));
                    builder.StoreWireSemiCircle(ref index, shape.R, -alignedUp, new Vector3(1, 0, 0), new Vector3(0.5f, 1, 0));
                    builder.StoreWireSemiCircle(ref index, shape.R, -alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
                    builder.StoreWireSemiCircle(ref index, shape.R, -alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));

                    var side1 = shape.R * Vector3.Cross(Vector3.UnitY, Vector3.UnitX);
                    var side2 = shape.R * Vector3.Cross(Vector3.UnitY, Vector3.UnitZ);
                    var sideOff1 = Vector3.Normalize(Vector3.Cross(side1, Vector3.UnitY)) * 0.01f;
                    var sideOff2 = Vector3.Normalize(Vector3.Cross(side2, Vector3.UnitY)) * 0.01f;
                    builder.InsertQuad(ref index, -alignedUp + side1 + sideOff1, -alignedUp + side1 - sideOff1, alignedUp + side1 - sideOff1, alignedUp + side1 + sideOff1);
                    builder.InsertQuad(ref index, -alignedUp - side1 + sideOff1, -alignedUp - side1 - sideOff1, alignedUp - side1 - sideOff1, alignedUp - side1 + sideOff1);
                    builder.InsertQuad(ref index, -alignedUp + side2 + sideOff2, -alignedUp + side2 - sideOff2, alignedUp + side2 - sideOff2, alignedUp + side2 + sideOff2);
                    builder.InsertQuad(ref index, -alignedUp - side2 + sideOff2, -alignedUp - side2 - sideOff2, alignedUp - side2 - sideOff2, alignedUp - side2 + sideOff2);
                } else {
                    builder.StoreLineSemiCircle(ref index, shape.R, alignedUp, new Vector3(0, 0, 0));
                    builder.StoreLineSemiCircle(ref index, shape.R, alignedUp, new Vector3(0.5f, 0, 0));
                    builder.StoreLineSemiCircle(ref index, shape.R, alignedUp, new Vector3(0.5f, 0.5f, 0));
                    builder.StoreLineSemiCircle(ref index, shape.R, alignedUp, new Vector3(0.5f, -0.5f, 0));

                    builder.StoreLineSemiCircle(ref index, shape.R, -alignedUp, new Vector3(0, 1, 0));
                    builder.StoreLineSemiCircle(ref index, shape.R, -alignedUp, new Vector3(0.5f, 1, 0));
                    builder.StoreLineSemiCircle(ref index, shape.R, -alignedUp, new Vector3(0.5f, 0.5f, 0));
                    builder.StoreLineSemiCircle(ref index, shape.R, -alignedUp, new Vector3(0.5f, -0.5f, 0));

                    var side1 = shape.R * Vector3.Cross(Vector3.UnitY, Vector3.UnitX);
                    var side2 = shape.R * Vector3.Cross(Vector3.UnitY, Vector3.UnitZ);
                    builder.InsertLine(ref index, -alignedUp + side1, alignedUp + side1);
                    builder.InsertLine(ref index, -alignedUp - side1, alignedUp - side1);
                    builder.InsertLine(ref index, -alignedUp + side2, alignedUp + side2);
                    builder.InsertLine(ref index, -alignedUp - side2, alignedUp - side2);
                }

                var rotation = Quaternion<float>.Normalize(TransformExtensions.CreateFromToQuaternion(Vector3.Normalize(alignedUp), Vector3.Normalize(up))).ToSystem();
                builder.TransformVertices(startIndex, index, rotation, center);
            }
        }

        public override int CalculateVertexCount(ShapeBuilder builder)
        {
            int count = 0;
            if (builder.GeoType == GeometryType.Line) {
                foreach (var sphere in shapes) count += CalculateLineSemicircleVertCount(sphere.R) * 8 + 4 * 2;
            } else {
                foreach (var sphere in shapes) count += CalculateSemicircleVertCount(sphere.R) * 8 + 4 * 6;
            }
            return count;
        }

        public override int CalculateShapeHash()
        {
            int hash = 17;
            foreach (var obj in shapes) hash = (int)HashCode.Combine(hash, obj.p0, obj.p1, obj.r);
            return hash;
        }
    }
    public class ShapeBuilderCylinder : ShapeBuilderShapeType<Cylinder>
    {
        public override void Build(ref int index, ShapeBuilder builder)
        {
            var geoType = builder.GeoType;
            foreach (var shape in shapes) {
                var up = (shape.p1 - shape.p0);
                if (up == Vector3.Zero) up = new Vector3(0, 0.001f, 0);
                var center = (shape.p1 + shape.p0) * 0.5f;
                var alignedUp = Vector3.UnitY * (up.Length() * 0.5f);
                var startIndex = index;
                if (geoType == GeometryType.FakeWire) {
                    builder.StoreWireSemiCircle(ref index, shape.r, alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
                    builder.StoreWireSemiCircle(ref index, shape.r, alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));

                    builder.StoreWireSemiCircle(ref index, shape.r, -alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
                    builder.StoreWireSemiCircle(ref index, shape.r, -alignedUp, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));

                    var side1 = shape.r * Vector3.Cross(Vector3.UnitY, Vector3.UnitX);
                    var side2 = shape.r * Vector3.Cross(Vector3.UnitY, Vector3.UnitZ);
                    var sideOff1 = Vector3.Normalize(Vector3.Cross(side1, Vector3.UnitY)) * 0.01f;
                    var sideOff2 = Vector3.Normalize(Vector3.Cross(side2, Vector3.UnitY)) * 0.01f;
                    builder.InsertQuad(ref index, -alignedUp + side1 + sideOff1, -alignedUp + side1 - sideOff1, alignedUp + side1 - sideOff1, alignedUp + side1 + sideOff1);
                    builder.InsertQuad(ref index, -alignedUp - side1 + sideOff1, -alignedUp - side1 - sideOff1, alignedUp - side1 - sideOff1, alignedUp - side1 + sideOff1);
                    builder.InsertQuad(ref index, -alignedUp + side2 + sideOff2, -alignedUp + side2 - sideOff2, alignedUp + side2 - sideOff2, alignedUp + side2 + sideOff2);
                    builder.InsertQuad(ref index, -alignedUp - side2 + sideOff2, -alignedUp - side2 - sideOff2, alignedUp - side2 - sideOff2, alignedUp - side2 + sideOff2);
                } else if (geoType == GeometryType.Filled) {
                    var segments = CalculateLineSemicircleVertCount(shape.r);
                    builder.StoreFilledCircle(ref index, shape.r, segments, -alignedUp, -Vector3.UnitY);
                    builder.StoreFilledCircle(ref index, shape.r, segments, alignedUp, Vector3.UnitY);
                    builder.StoreFilledTube(ref index, new Cone() { p0 = -alignedUp, p1 = alignedUp, r0 = shape.r, r1 = shape.r });
                } else {
                    builder.StoreLineSemiCircle(ref index, shape.r, alignedUp, new Vector3(0.5f, 0.5f, 0));
                    builder.StoreLineSemiCircle(ref index, shape.r, alignedUp, new Vector3(0.5f, -0.5f, 0));

                    builder.StoreLineSemiCircle(ref index, shape.r, -alignedUp, new Vector3(0.5f, 0.5f, 0));
                    builder.StoreLineSemiCircle(ref index, shape.r, -alignedUp, new Vector3(0.5f, -0.5f, 0));

                    var side1 = shape.r * Vector3.Cross(Vector3.UnitY, Vector3.UnitX);
                    var side2 = shape.r * Vector3.Cross(Vector3.UnitY, Vector3.UnitZ);
                    builder.InsertLine(ref index, -alignedUp + side1, alignedUp + side1);
                    builder.InsertLine(ref index, -alignedUp - side1, alignedUp - side1);
                    builder.InsertLine(ref index, -alignedUp + side2, alignedUp + side2);
                    builder.InsertLine(ref index, -alignedUp - side2, alignedUp - side2);
                }

                var rotation = Quaternion<float>.Normalize(TransformExtensions.CreateFromToQuaternion(Vector3.Normalize(alignedUp), Vector3.Normalize(up))).ToSystem();
                builder.TransformVertices(startIndex, index, rotation, center);
            }
        }

        public override int CalculateVertexCount(ShapeBuilder builder)
        {
            int count = 0;
            if (builder.GeoType == GeometryType.Line) {
                foreach (var sphere in shapes) count += CalculateLineSemicircleVertCount(sphere.r) * 4 + 4 * 2;
            } else if (builder.GeoType == GeometryType.Filled) {
                foreach (var sphere in shapes) count += CalculateLineSemicircleVertCount(sphere.r) * 5 + 4 * 2;
            } else {
                foreach (var sphere in shapes) count += CalculateSemicircleVertCount(sphere.r) * 4 + 4 * 6;
            }
            return count;
        }

        public override int CalculateShapeHash()
        {
            int hash = 17;
            foreach (var obj in shapes) hash = (int)HashCode.Combine(hash, obj.p0, obj.p1, obj.r);
            return hash;
        }
    }
    public class ShapeBuilderCone : ShapeBuilderShapeType<Cone>
    {
        public override void Build(ref int index, ShapeBuilder builder)
        {
            var geoType = builder.GeoType;
            foreach (var shape in shapes) {
                var up = (shape.p1 - shape.p0);
                if (up == Vector3.Zero) up = new Vector3(0, 0.001f, 0);
                var center = (shape.p1 + shape.p0) * 0.5f;
                var alignedUp = Vector3.UnitY * (up.Length() * 0.5f);
                var startIndex = index;
                if (geoType == GeometryType.Line) {
                    builder.StoreLineSemiCircle(ref index, shape.r0, -alignedUp, new Vector3(0.5f, 0.5f, 0));
                    builder.StoreLineSemiCircle(ref index, shape.r0, -alignedUp, new Vector3(0.5f, -0.5f, 0));

                    builder.StoreLineSemiCircle(ref index, shape.r1, alignedUp, new Vector3(0.5f, 0.5f, 0));
                    builder.StoreLineSemiCircle(ref index, shape.r1, alignedUp, new Vector3(0.5f, -0.5f, 0));

                    var side1 = Vector3.Cross(Vector3.UnitY, Vector3.UnitX);
                    var side2 = Vector3.Cross(Vector3.UnitY, Vector3.UnitZ);
                    builder.InsertLine(ref index, -alignedUp + side1 * shape.r0, alignedUp + side1 * shape.r1);
                    builder.InsertLine(ref index, -alignedUp - side1 * shape.r0, alignedUp - side1 * shape.r1);
                    builder.InsertLine(ref index, -alignedUp + side2 * shape.r0, alignedUp + side2 * shape.r1);
                    builder.InsertLine(ref index, -alignedUp - side2 * shape.r0, alignedUp - side2 * shape.r1);
                } else if (geoType == GeometryType.Filled) {
                    var segments = CalculateLineSemicircleVertCount(shape.r0);
                    builder.StoreFilledCircle(ref index, shape.r0, segments, -alignedUp, -Vector3.UnitY);
                    builder.StoreFilledCircle(ref index, shape.r1, segments, alignedUp, Vector3.UnitY);
                    builder.StoreFilledTube(ref index, new Cone(-alignedUp, shape.r0, alignedUp, shape.r1));
                } else {
                    throw new NotImplementedException();
                }

                var rotation = Quaternion<float>.Normalize(TransformExtensions.CreateFromToQuaternion(Vector3.Normalize(alignedUp), Vector3.Normalize(up))).ToSystem();
                builder.TransformVertices(startIndex, index, rotation, center);
            }
        }

        public override int CalculateVertexCount(ShapeBuilder builder)
        {
            int count = 0;
            if (builder.GeoType == GeometryType.Line) {
                foreach (var sphere in shapes) count += CalculateLineSemicircleVertCount(sphere.r0) * 2 + CalculateLineSemicircleVertCount(sphere.r1) * 2 + 4 * 2;
            } else if (builder.GeoType == GeometryType.Filled) {
                foreach (var sphere in shapes) count += CalculateSemicircleVertCount(sphere.r0) * 18; // x3 for bottom cap, x3 for top cap, x12 for tube
            } else {
                foreach (var sphere in shapes) count += CalculateSemicircleVertCount(sphere.r0) * 2 + CalculateSemicircleVertCount(sphere.r1) * 2 + 4 * 6;
            }
            return count;
        }

        public override int CalculateShapeHash()
        {
            int hash = 17;
            foreach (var obj in shapes) hash = (int)HashCode.Combine(hash, obj.p0, obj.p1, obj.r0, obj.r1);
            return hash;
        }
    }
    public class ShapeBuilderCircle : ShapeBuilderShapeType<(Vector3 pos, Vector3 dir, float radius)>
    {
        public override void Build(ref int index, ShapeBuilder builder)
        {
            var geoType = builder.GeoType;
            foreach (var (pos, dir, radius) in shapes) {
                var startIndex = index;
                if (geoType == GeometryType.Line) {
                    builder.StoreLineSemiCircle(ref index, radius, Vector3.Zero, new Vector3(0.5f, 0.5f, 0));
                    builder.StoreLineSemiCircle(ref index, radius, Vector3.Zero, new Vector3(0.5f, -0.5f, 0));
                } else {
                    throw new NotImplementedException();
                }

                var rotation = Quaternion<float>.Normalize(TransformExtensions.CreateFromToQuaternion(Vector3.UnitY, Vector3.Normalize(dir))).ToSystem();
                builder.TransformVertices(startIndex, index, rotation, pos);
            }
        }

        public override int CalculateVertexCount(ShapeBuilder builder)
        {
            int count = 0;
            if (builder.GeoType == GeometryType.Line) {
                foreach (var sphere in shapes) count += CalculateLineSemicircleVertCount(sphere.radius) * 2;
            }
            return count;
        }

        public override int CalculateShapeHash()
        {
            int hash = 17;
            foreach (var obj in shapes) hash = (int)HashCode.Combine(hash, obj.pos, obj.dir, obj.radius);
            return hash;
        }
    }

    public class ShapeBuilderOBB : ShapeBuilderShapeType<OBB>
    {
        public override void Build(ref int index, ShapeBuilder builder)
        {
            if (builder.GeoType == GeometryType.Line) {
                foreach (var shape in shapes) {
                    for (int i = 0; i < BoxLinePoints.Length; ++i) {
                        builder.InsertVertex(ref index, shape.Coord.Multiply(BoxLinePoints[i] * shape.Extent));
                    }
                }
            } else {
                foreach (var shape in shapes) {
                    // an OBB is just an AABB with a transformation applied to it, we can use the same logic
                    for (int i = 0; i < BoxTrianglePoints.Length; ++i) {
                        var point = shape.Coord.Multiply(BoxTrianglePoints[i] * shape.Extent);
                        builder.AddBoxUVAttributes(index, i);
                        builder.InsertVertex(ref index, point);
                    }
                }
            }
        }

        public override int CalculateVertexCount(ShapeBuilder builder)
        {
            return shapes.Count * (builder.GeoType == ShapeBuilder.GeometryType.Line ? ShapeBuilder.BoxLinePoints.Length : ShapeBuilder.BoxTrianglePoints.Length);
        }

        public override int CalculateShapeHash()
        {
            int hash = 17;
            foreach (var obj in shapes) hash = (int)HashCode.Combine(hash, obj.Extent, obj.Coord.ToSystem());
            return hash;
        }
    }
    public class ShapeBuilderAABB : ShapeBuilderShapeType<AABB>
    {
        public override void Build(ref int index, ShapeBuilder builder)
        {
            if (builder.GeoType == ShapeBuilder.GeometryType.Line) {
                foreach (var shape in shapes) {
                    var extent = shape.Size / 2;
                    for (int i = 0; i < BoxLinePoints.Length; ++i) {
                        builder.InsertVertex(ref index, shape.Center + BoxLinePoints[i] * extent);
                    }
                }
            } else {
                foreach (var shape in shapes) {
                    var extent = shape.Size / 2;
                    for (int i = 0; i < BoxTrianglePoints.Length; ++i) {
                        builder.AddBoxUVAttributes(index, i);
                        builder.InsertVertex(ref index, shape.Center + BoxTrianglePoints[i] * extent);
                    }
                }
            }
        }

        public override int CalculateVertexCount(ShapeBuilder builder)
        {
            return shapes.Count * (builder.GeoType == ShapeBuilder.GeometryType.Line ? BoxLinePoints.Length : BoxTrianglePoints.Length);
        }

        public override int CalculateShapeHash()
        {
            int hash = 17;
            foreach (var obj in shapes) hash = (int)HashCode.Combine(hash, obj.minpos, obj.maxpos);
            return hash;
        }
    }

    public class ShapeBuilderLineSegment : ShapeBuilderShapeType<LineSegment>
    {
        public override void Build(ref int index, ShapeBuilder builder)
        {
            if (builder.GeoType == ShapeBuilder.GeometryType.Line) {
                foreach (var shape in shapes) {
                    builder.InsertLine(ref index, shape.start, shape.end);
                }
            } else {
                throw new NotImplementedException();
            }
        }

        public override int CalculateVertexCount(ShapeBuilder builder)
        {
            return shapes.Count * (builder.GeoType == ShapeBuilder.GeometryType.Line ? 2 : 6);
        }

        public override int CalculateShapeHash()
        {
            int hash = 17;
            foreach (var obj in shapes) hash = (int)HashCode.Combine(hash, obj.start, obj.end);
            return hash;
        }
    }
}
