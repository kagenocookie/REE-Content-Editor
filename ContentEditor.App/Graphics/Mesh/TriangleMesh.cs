using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ReeLib;
using ReeLib.Aimp;
using ReeLib.Mesh;
using ReeLib.MplyMesh;
using ReeLib.via;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class TriangleMesh : Mesh
{
    private TriangleMesh() { }

    public TriangleMesh(GL gl, float[] vertexData, int[] indices) : base(gl)
    {
        VertexData = vertexData;
        Indices = indices;
        UpdateBuffers();
    }

    public TriangleMesh(GL gl, float[] vertexData, int[] indices, AABB bounds, in MeshLayout layout) : this(gl, vertexData, indices)
    {
        BoundingBox = bounds;
        this.layout = layout;
    }

    public TriangleMesh(GL gl, MeshFile sourceMesh, ReeLib.Mesh.Submesh submesh) : base(gl)
    {
        PrepareMeshVertexBufferData(sourceMesh, submesh);
        UpdateBuffers();
    }

    public TriangleMesh(MeshFile sourceMesh, ReeLib.Mesh.Submesh submesh, MeshBuffer? buffer = null)
    {
        PrepareMeshVertexBufferData(sourceMesh, submesh, buffer);
    }

    public TriangleMesh(AimpFile file, ContentGroupContainer container, ContentGroupTriangle data)
    {
        layout = MeshLayout.ColoredPositions;
        Indices = new int[data.NodeCount * 3];
        VertexData = new float[Indices.Length * layout.VertexSize];

        BoundingBox = container.bounds;
        var nodes = container.NodeInfo.Nodes;
        var verts = container.Vertices;
        var triangles = data.Nodes;
        var pointData = MemoryMarshal.Cast<float, Vector4>(VertexData.AsSpan());

        var index = 0;
        for (int i = 0; i < data.NodeCount; ++i) {
            var node = nodes[i];
            var tri = triangles[i];
            var color = BitConverter.Int32BitsToSingle((int)node.GetColor(file).rgba);
            var a = verts[tri.index1].Vector3;
            var b = verts[tri.index2].Vector3;
            var c = verts[tri.index3].Vector3;

            pointData[index++] = new Vector4(a, color);
            pointData[index++] = new Vector4(b, color);
            pointData[index++] = new Vector4(c, color);
        }
    }

    public TriangleMesh(AimpFile file, ContentGroupContainer container, ContentGroupPolygon data)
    {
        layout = MeshLayout.ColoredPositions;
        var polygons = data.Nodes;
        var nodes = container.NodeInfo.Nodes;
        var verts = container.Vertices;
        BoundingBox = container.bounds;

        var totalTriangleCount = 0;
        foreach (var poly in polygons) {
            totalTriangleCount += poly.indices.Length - 2;
        }
        Indices = new int[totalTriangleCount * 3];
        VertexData = new float[Indices.Length * layout.VertexSize];

        var pointData = MemoryMarshal.Cast<float, Vector4>(VertexData.AsSpan());

        var index = 0;
        for (int i = 0; i < data.NodeCount; ++i) {
            var node = nodes[i];
            var poly = polygons[i];
            var color = BitConverter.Int32BitsToSingle((int)node.GetColor(file).rgba);
            for (int x = 2; x < poly.indices.Length; ++x) {
                var a = verts[poly.indices[0]].Vector3;
                var b = verts[poly.indices[x - 1]].Vector3;
                var c = verts[poly.indices[x - 0]].Vector3;

                pointData[index++] = new Vector4(a, color);
                pointData[index++] = new Vector4(b, color);
                pointData[index++] = new Vector4(c, color);
            }
        }
    }

    public TriangleMesh(AimpFile file, ContentGroupContainer container, ContentGroupWall data)
    {
        layout = MeshLayout.ColoredPositions;
        var polygons = data.Nodes;
        var nodes = data.NodeInfos;
        var verts = container.Vertices;
        BoundingBox = container.bounds;

        Indices = new int[polygons.Count * (4 * 6)];
        VertexData = new float[Indices.Length * layout.VertexSize];

        var pointData = MemoryMarshal.Cast<float, Vector4>(VertexData.AsSpan());

        var index = 0;
        Span<Vector3> pts = stackalloc Vector3[8];
        foreach (var info in data.NodeInfos) {
            var node = nodes[info.localIndex];
            var poly = polygons[info.localIndex];
            var color = BitConverter.Int32BitsToSingle((int)node.GetColor(file).rgba);
            for (int k = 0; k < 8; ++k) pts[k] = verts[poly.indices[k]].Vector3;

            // TODO figure out what the point of the matrix / transforms is - maybe OBB-like data?

            pointData[index++] = new Vector4(pts[0], color);
            pointData[index++] = new Vector4(pts[1], color);
            pointData[index++] = new Vector4(pts[2], color);
            pointData[index++] = new Vector4(pts[0], color);
            pointData[index++] = new Vector4(pts[2], color);
            pointData[index++] = new Vector4(pts[3], color);

            pointData[index++] = new Vector4(pts[4], color);
            pointData[index++] = new Vector4(pts[5], color);
            pointData[index++] = new Vector4(pts[6], color);
            pointData[index++] = new Vector4(pts[4], color);
            pointData[index++] = new Vector4(pts[6], color);
            pointData[index++] = new Vector4(pts[7], color);

            pointData[index++] = new Vector4(pts[0], color);
            pointData[index++] = new Vector4(pts[1], color);
            pointData[index++] = new Vector4(pts[4], color);
            pointData[index++] = new Vector4(pts[1], color);
            pointData[index++] = new Vector4(pts[4], color);
            pointData[index++] = new Vector4(pts[5], color);

            pointData[index++] = new Vector4(pts[2], color);
            pointData[index++] = new Vector4(pts[3], color);
            pointData[index++] = new Vector4(pts[6], color);
            pointData[index++] = new Vector4(pts[3], color);
            pointData[index++] = new Vector4(pts[6], color);
            pointData[index++] = new Vector4(pts[7], color);
        }
    }
    public TriangleMesh(AimpFile file, ContentGroupContainer container, ContentGroupMapBoundary data, int nodeOffset)
    {
        // I'm not yet sure if we wanna use this one or not... boundary shapes look stupid
        layout = MeshLayout.ColoredPositions;
        var polygons = data.Nodes;
        var nodes = container.NodeInfo.Nodes;
        var verts = container.Vertices;
        BoundingBox = container.bounds;

        Indices = new int[polygons.Count * (4 * 6)];
        VertexData = new float[Indices.Length * layout.VertexSize];

        var pointData = MemoryMarshal.Cast<float, Vector4>(VertexData.AsSpan());

        var index = 0;
        Span<Vector3> pts = stackalloc Vector3[8];
        for (int i = 0; i < data.NodeCount; ++i) {
            var node = nodes[nodeOffset + i];
            var poly = polygons[i];
            var color = BitConverter.Int32BitsToSingle((int)node.GetColor(file).rgba);
            for (int k = 0; k < 8; ++k) pts[k] = verts[poly.indices[k]].Vector3;

            pointData[index++] = new Vector4(pts[0], color);
            pointData[index++] = new Vector4(pts[1], color);
            pointData[index++] = new Vector4(pts[5], color);
            pointData[index++] = new Vector4(pts[0], color);
            pointData[index++] = new Vector4(pts[5], color);
            pointData[index++] = new Vector4(pts[4], color);

            pointData[index++] = new Vector4(pts[4], color);
            pointData[index++] = new Vector4(pts[5], color);
            pointData[index++] = new Vector4(pts[2], color);
            pointData[index++] = new Vector4(pts[4], color);
            pointData[index++] = new Vector4(pts[2], color);
            pointData[index++] = new Vector4(pts[3], color);

            pointData[index++] = new Vector4(pts[3], color);
            pointData[index++] = new Vector4(pts[2], color);
            pointData[index++] = new Vector4(pts[6], color);
            pointData[index++] = new Vector4(pts[3], color);
            pointData[index++] = new Vector4(pts[6], color);
            pointData[index++] = new Vector4(pts[7], color);

            pointData[index++] = new Vector4(pts[6], color);
            pointData[index++] = new Vector4(pts[7], color);
            pointData[index++] = new Vector4(pts[0], color);
            pointData[index++] = new Vector4(pts[6], color);
            pointData[index++] = new Vector4(pts[0], color);
            pointData[index++] = new Vector4(pts[1], color);
        }
    }

    private void PrepareMeshVertexBufferData(MeshFile sourceMesh, ReeLib.Mesh.Submesh submesh, MeshBuffer? buffer = null)
    {
        buffer ??= sourceMesh.MeshBuffer;
        if (buffer == null) {
            return;
        }

        var integerIndices = sourceMesh.MeshData?.integerFaces ?? false;
        var hasWeights = sourceMesh.BoneData?.DeformBones.Count > 0;
        var hasUV = buffer.UV0.Length > 0;
        var hasNormals = buffer.NormalsTangents.Length > 0;
        layout = MeshLayout.Get(MeshAttributeFlag.Triangles
            | (hasWeights ? MeshAttributeFlag.Weight : 0)
            | (hasWeights && submesh.Weights[0].IndexCount == 6 ? MeshAttributeFlag.Use6Weight : 0));

        var weightIndexCount = layout.Is6Weight ? 6 : 8;

        var meshVerts = submesh.Positions;
        var meshUV = hasUV ? MemoryMarshal.Cast<HFloat2, float>(submesh.UV0) : default;
        var meshNormals = hasNormals ? MemoryMarshal.Cast<QuantizedNorTan, Vector2>(submesh.NormalsTangents) : default;
        var meshWeights = hasWeights ? submesh.Weights : default;

        var indicesShort = !integerIndices ? submesh.Indices : default;
        var indicesInt = integerIndices ? submesh.IntegerIndices : default;

        var attrs = layout.VertexSize;
        Indices = new int[submesh.indicesCount];
        VertexData = new float[submesh.indicesCount * attrs];
        var boneIndsOffset = layout.AttributeIndexOffsets[MeshLayout.Index_BoneIndex];

        for (int index = 0; index < Indices.Length; index++) {
            var vert = integerIndices ? indicesInt[index] : indicesShort[index];
            Indices[index] = vert;
            var pos = meshVerts[vert];
            var uv = hasUV ? meshUV[vert] : 0;
            var norm = hasNormals ? meshNormals[vert] : default;
            var vertOffset = index * attrs;
            VertexData[vertOffset + 0] = pos.X;
            VertexData[vertOffset + 1] = pos.Y;
            VertexData[vertOffset + 2] = pos.Z;
            VertexData[vertOffset + 3] = uv;
            VertexData[vertOffset + 4] = norm.X;
            VertexData[vertOffset + 5] = BitConverter.Int32BitsToSingle(index);
            if (HasBones) {
                meshWeights[vert].CopyTo(MemoryMarshal.Cast<float, byte>(VertexData.AsSpan(vertOffset + boneIndsOffset)));
            }
        }

        if (sourceMesh.MeshData != null) {
            BoundingBox = sourceMesh.MeshData.boundingBox;
        } else {
            RecomputeBounds();
        }
    }

    private void RecomputeBounds()
    {
        BoundingBox = AABB.MaxMin;
        var vertSize = layout.VertexSize;
        var verts = VertexData.Length / vertSize;
        for (int i = 0; i < verts; ++i) {
            var vec = new Vector3(VertexData[i * vertSize + 0], VertexData[i * vertSize + 1], VertexData[i * vertSize + 2]);
            BoundingBox = BoundingBox.Extend(vec);
        }
    }

    public override Mesh Clone()
    {
        // for triangle meshes, reuse ths arrays directly instead of copying them
        // reasoning being that these are generally imported and never modified so it shouldn't be an issue
        var copy = new TriangleMesh();
        CopyGeometryDataReuseArrays(copy);
        return copy;
    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}