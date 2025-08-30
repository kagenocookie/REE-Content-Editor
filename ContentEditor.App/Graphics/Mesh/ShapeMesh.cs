using System.Numerics;
using ReeLib.Rcol;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class ShapeMesh : Mesh
{
    public ShapeType ShapeType { get; private set; } = ShapeType.Invalid;

    public ShapeMesh(GL gl) : base(gl)
    {
        SetAttributesNoTangents();
    }

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

    #region BOX
    public void SetShape(AABB aabb)
    {
        SetupBoxMesh(aabb);

        var center = aabb.Center;
        var extent = aabb.Size / 2;
        for (int i = 0; i < BoxTrianglePoints.Length; ++i) {
            VertexData[i * attributeNumberCount + 0] = center.X + BoxTrianglePoints[i].X * extent.X;
            VertexData[i * attributeNumberCount + 1] = center.Y + BoxTrianglePoints[i].Y * extent.Y;
            VertexData[i * attributeNumberCount + 2] = center.Z + BoxTrianglePoints[i].Z * extent.Z;
        }
        BoundingBox = aabb;
        ShapeType = ShapeType.Aabb;
        // TODO verify - do we need to negate the GameObject's transform (are AabbShapes truly AABB or just AABB relative to the GameObject they're in?)
        UpdateBuffers();
    }

    public void SetShape(OBB box)
    {
        var center = box.Coord.Multiply(Vector3.Zero);
        var extent = box.Extent / 2;
        var baseAabb = new AABB() { minpos = center - extent, maxpos = center + extent };
        SetupBoxMesh(baseAabb);

        var totalBounds = AABB.MaxMin;
        // an OBB is just an AABB with a transformation applied to it
        for (int i = 0; i < BoxTrianglePoints.Length; ++i) {
            var point = box.Coord.Multiply(BoxTrianglePoints[i]);
            VertexData[i * attributeNumberCount + 0] = point.X;
            VertexData[i * attributeNumberCount + 1] = point.Y;
            VertexData[i * attributeNumberCount + 2] = point.Z;
            totalBounds = totalBounds.Extend(point);
        }
        BoundingBox = totalBounds;
        ShapeType = ShapeType.Box;
        UpdateBuffers();
    }

    private void SetupBoxMesh(AABB aabb)
    {
        if (ShapeType != ShapeType.Box && ShapeType != ShapeType.Aabb) {
            Indices = new int[36];
            VertexData = new float[36 * attributeNumberCount];
            for (int i = 0; i < BoxTrianglePoints.Length; ++i) {
                var side = i / 6;
                // positions get set separately
                // uv
                VertexData[i * attributeNumberCount + 3] = i % 6 is 1 or 2 or 4 ? 1 : 0;
                VertexData[i * attributeNumberCount + 4] = i % 6 is 0 or 1 or 3 ? 1 : 0;
                // normals - note: correctness untested
                VertexData[i * attributeNumberCount + 5] = side is 0 or 1 ? 1 : 0;
                VertexData[i * attributeNumberCount + 6] = side is 2 or 3 ? 1 : 0;
                VertexData[i * attributeNumberCount + 7] = side is 4 or 5 ? 1 : 0;
                // index
                VertexData[i * attributeNumberCount + 8] = (float)i;
            }
            Array.Copy(BoxIndices, Indices, Indices.Length);
        }
    }
    #endregion

    #region SPHERICAL
    private static int CalculateSemicirclePointCount(float radius) => (int)Math.Clamp((radius + 0.01f) * 6f, 6, 32);
    private static int CalculateSemicircleVertCount(float radius) =>(CalculateSemicirclePointCount(radius) - 1) * 6;

    public void SetWireShape(Sphere sphere, ShapeType type)
    {
        // TODO ideally, we'd just use some sort of Line rendering method instead of full triangles meshes here
        // but it's simpler to just start off with pure tris cause that already works
        var semiVertCount = CalculateSemicircleVertCount(sphere.R);
        var vertCount = semiVertCount * 6;
        if (VertexData == null || VertexData.Length != vertCount * attributeNumberCount) {
            VertexData = new float[vertCount * attributeNumberCount];
            for (int i = 0; i < vertCount; ++i) {
                // UVs - I don't think we care here
                VertexData[i * attributeNumberCount + 3] = 0;
                VertexData[i * attributeNumberCount + 4] = 0;
                // normals
                VertexData[i * attributeNumberCount + 5] = 0;
                VertexData[i * attributeNumberCount + 6] = 1;
                VertexData[i * attributeNumberCount + 7] = 0;
                // index
                VertexData[i * attributeNumberCount + 8] = (float)i;
            }
        }
        if (Indices == null || Indices.Length != vertCount) Indices = new int[vertCount];

        var index = 0;
        StoreWireSemiCircle(ref index, sphere.R, sphere.pos, new Vector3(0, 0, 1), new Vector3(0, 0, 0));
        StoreWireSemiCircle(ref index, sphere.R, sphere.pos, new Vector3(0, 0, 1), new Vector3(0, 1, 0));
        StoreWireSemiCircle(ref index, sphere.R, sphere.pos, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
        StoreWireSemiCircle(ref index, sphere.R, sphere.pos, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));
        StoreWireSemiCircle(ref index, sphere.R, sphere.pos, new Vector3(1, 0, 0), new Vector3(-0.5f, 0, 0.5f));
        StoreWireSemiCircle(ref index, sphere.R, sphere.pos, new Vector3(1, 0, 0), new Vector3(0.5f, 0, 0.5f));
        ShapeType = type;
        RecalcBoundingBox();
        UpdateBuffers();
    }

    public void SetWireShape(Capsule capsule, ShapeType type)
    {
        if (capsule.p1 == capsule.p0) {
            SetWireShape(new Sphere() { pos = capsule.p0, r = capsule.R }, ShapeType.Sphere);
            ShapeType = type;
            return;
        }

        var semiVertCount = CalculateSemicircleVertCount(capsule.R);
        var vertCount = semiVertCount * 8 + 4 * 6;
        if (VertexData == null || VertexData.Length != vertCount * attributeNumberCount) {
            VertexData = new float[vertCount * attributeNumberCount];
            for (int i = 0; i < vertCount; ++i) {
                // UVs - I don't think we care here
                VertexData[i * attributeNumberCount + 3] = 0;
                VertexData[i * attributeNumberCount + 4] = 0;
                // normals - can be anything really
                VertexData[i * attributeNumberCount + 5] = 0;
                VertexData[i * attributeNumberCount + 6] = 1;
                VertexData[i * attributeNumberCount + 7] = 0;
                // index
                VertexData[i * attributeNumberCount + 8] = (float)i;
            }
        }
        if (Indices == null || Indices.Length != vertCount) Indices = new int[vertCount];

        var index = 0;
        StoreWireSemiCircle(ref index, capsule.R, capsule.p1, new Vector3(0, 0, 1), new Vector3(0, 0, 0));
        StoreWireSemiCircle(ref index, capsule.R, capsule.p1, new Vector3(1, 0, 0), new Vector3(0.5f, 0, 0));
        StoreWireSemiCircle(ref index, capsule.R, capsule.p1, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
        StoreWireSemiCircle(ref index, capsule.R, capsule.p1, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));

        StoreWireSemiCircle(ref index, capsule.R, capsule.p0, new Vector3(0, 0, 1), new Vector3(0, 1, 0));
        StoreWireSemiCircle(ref index, capsule.R, capsule.p0, new Vector3(1, 0, 0), new Vector3(0.5f, 1, 0));
        StoreWireSemiCircle(ref index, capsule.R, capsule.p0, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
        StoreWireSemiCircle(ref index, capsule.R, capsule.p0, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));

        ShapeType = type;
        var up = (capsule.p1 - capsule.p0);
        var side1 = capsule.R * Vector3.Normalize(Vector3.Cross(up, Vector3.Dot(up, Vector3.UnitY) >= 0.9999f ? Vector3.UnitX : Vector3.UnitY));
        var side2 = capsule.R * Vector3.Normalize(Vector3.Cross(up, side1));
        var sideOff1 = Vector3.Normalize(Vector3.Cross(side1, up)) * 0.01f;
        var sideOff2 = Vector3.Normalize(Vector3.Cross(side2, up)) * 0.01f;
        InsertQuad(ref index, capsule.p0 + side1 + sideOff1, capsule.p0 + side1 - sideOff1, capsule.p1 + side1 - sideOff1, capsule.p1 + side1 + sideOff1);
        InsertQuad(ref index, capsule.p0 - side1 + sideOff1, capsule.p0 - side1 - sideOff1, capsule.p1 - side1 - sideOff1, capsule.p1 - side1 + sideOff1);
        InsertQuad(ref index, capsule.p0 + side2 + sideOff2, capsule.p0 + side2 - sideOff2, capsule.p1 + side2 - sideOff2, capsule.p1 + side2 + sideOff2);
        InsertQuad(ref index, capsule.p0 - side2 + sideOff2, capsule.p0 - side2 - sideOff2, capsule.p1 - side2 - sideOff2, capsule.p1 - side2 + sideOff2);
        RecalcBoundingBox();
        UpdateBuffers();
    }

    public void SetWireShape(Cylinder cylinder)
    {
        ShapeType = ShapeType.Cylinder;
        // mostly identical to capsule, except without the two spherical caps on both sides
        var semiVertCount = CalculateSemicircleVertCount(cylinder.r);
        var vertCount = semiVertCount * 4 + 4 * 6;
        if (VertexData == null || VertexData.Length != vertCount * attributeNumberCount) {
            VertexData = new float[vertCount * attributeNumberCount];
            for (int i = 0; i < vertCount; ++i) {
                // UVs - I don't think we care here
                VertexData[i * attributeNumberCount + 3] = 0;
                VertexData[i * attributeNumberCount + 4] = 0;
                // normals - can be anything really
                VertexData[i * attributeNumberCount + 5] = 0;
                VertexData[i * attributeNumberCount + 6] = 1;
                VertexData[i * attributeNumberCount + 7] = 0;
                // index
                VertexData[i * attributeNumberCount + 8] = (float)i;
            }
        }
        if (Indices == null || Indices.Length != vertCount) Indices = new int[vertCount];

        var index = 0;
        StoreWireSemiCircle(ref index, cylinder.r, cylinder.p1, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
        StoreWireSemiCircle(ref index, cylinder.r, cylinder.p1, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));

        StoreWireSemiCircle(ref index, cylinder.r, cylinder.p0, new Vector3(0, 1, 0), new Vector3(0.5f, 0.5f, 0));
        StoreWireSemiCircle(ref index, cylinder.r, cylinder.p0, new Vector3(0, 1, 0), new Vector3(0.5f, -0.5f, 0));

        var up = (cylinder.p1 - cylinder.p0);
        var side1 = cylinder.r * Vector3.Normalize(Vector3.Cross(up, Vector3.Dot(up, Vector3.UnitY) >= 0.9999f ? Vector3.UnitX : Vector3.UnitY));
        var side2 = cylinder.r * Vector3.Normalize(Vector3.Cross(up, side1));
        var sideOff1 = Vector3.Normalize(Vector3.Cross(side1, up)) * 0.01f;
        var sideOff2 = Vector3.Normalize(Vector3.Cross(side2, up)) * 0.01f;
        InsertQuad(ref index, cylinder.p0 + side1 + sideOff1, cylinder.p0 + side1 - sideOff1, cylinder.p1 + side1 - sideOff1, cylinder.p1 + side1 + sideOff1);
        InsertQuad(ref index, cylinder.p0 - side1 + sideOff1, cylinder.p0 - side1 - sideOff1, cylinder.p1 - side1 - sideOff1, cylinder.p1 - side1 + sideOff1);
        InsertQuad(ref index, cylinder.p0 + side2 + sideOff2, cylinder.p0 + side2 - sideOff2, cylinder.p1 + side2 - sideOff2, cylinder.p1 + side2 + sideOff2);
        InsertQuad(ref index, cylinder.p0 - side2 + sideOff2, cylinder.p0 - side2 - sideOff2, cylinder.p1 - side2 - sideOff2, cylinder.p1 - side2 + sideOff2);
        RecalcBoundingBox();
        UpdateBuffers();
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

    #endregion

    private void InsertVertex(ref int index, Vector3 vec)
    {
        VertexData[index * attributeNumberCount + 0] = vec.X;
        VertexData[index * attributeNumberCount + 1] = vec.Y;
        VertexData[index * attributeNumberCount + 2] = vec.Z;
        Indices[index] = index;
        index++;
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

    private void RecalcBoundingBox()
    {
        if (BoxTrianglePoints.Length == 0) {
            BoundingBox = new AABB(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
            return;
        }
        var totalBounds = AABB.MaxMin;
        for (int i = 0; i < BoxTrianglePoints.Length; ++i) {
            var point = new Vector3(
                VertexData[i * attributeNumberCount + 0],
                VertexData[i * attributeNumberCount + 1],
                VertexData[i * attributeNumberCount + 2]
            );
            totalBounds = totalBounds.Extend(point);
        }
        BoundingBox = totalBounds;
    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}