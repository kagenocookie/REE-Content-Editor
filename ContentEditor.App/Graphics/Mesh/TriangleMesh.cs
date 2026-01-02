using System.Numerics;
using System.Runtime.InteropServices;
using ReeLib;
using ReeLib.Aimp;
using ReeLib.via;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class TriangleMesh : Mesh
{
    private TriangleMesh() {}

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

    public TriangleMesh(MeshFile sourceMesh, ReeLib.Mesh.Submesh submesh)
    {
        PrepareMeshVertexBufferData(sourceMesh, submesh);
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

    public TriangleMesh(AimpFile file, ContentGroupContainer container, ContentGroupWall data, int nodeOffset)
    {
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

    private void PrepareMeshVertexBufferData(MeshFile sourceMesh, ReeLib.Mesh.Submesh submesh)
    {
        if (sourceMesh.MeshBuffer == null)
        {
            return;
        }

        var integerIndices = sourceMesh.MeshData?.integerFaces ?? false;
        var hasTan = sourceMesh.MeshBuffer.Tangents.Length != 0;
        var hasWeights = sourceMesh.BoneData?.Bones.Count > 0;
        var hasNormals = sourceMesh.MeshBuffer.Normals.Length > 0;
        layout = MeshLayout.Get(MeshAttributeFlag.Triangles | (hasWeights ? MeshAttributeFlag.Weight : 0));

        var meshVerts = submesh.Positions;
        var meshUV = submesh.UV0;
        var meshNormals = hasNormals ? submesh.Normals : default;
        var meshTangents = hasTan ? submesh.Tangents : default;
        var meshWeights = hasWeights ? submesh.Weights : default;

        var indicesShort = !integerIndices ? submesh.Indices : default;
        var indicesInt = integerIndices ? submesh.IntegerIndices : default;

        var attrs = layout.VertexSize;
        Indices = new int[submesh.indicesCount];
        VertexData = new float[submesh.indicesCount * attrs];
        var boneIndsOffset = layout.AttributeIndexOffsets[MeshLayout.Index_BoneIndex];
        var boneWeightsOffset = layout.AttributeIndexOffsets[MeshLayout.Index_BoneWeight];

        for (int index = 0; index < Indices.Length; index++) {
            var vert = integerIndices ? indicesInt[index] : indicesShort[index];
            Indices[index] = vert;
            var pos = meshVerts[vert];
            var uv = meshUV[vert];
            var norm = hasNormals ? meshNormals[vert] : Vector3.UnitX;
            var vertOffset = index * attrs;
            VertexData[vertOffset + 0] = pos.X;
            VertexData[vertOffset + 1] = pos.Y;
            VertexData[vertOffset + 2] = pos.Z;
            VertexData[vertOffset + 3] = uv.X;
            VertexData[vertOffset + 4] = uv.Y;
            VertexData[vertOffset + 5] = norm.X;
            VertexData[vertOffset + 6] = norm.Y;
            VertexData[vertOffset + 7] = norm.Z;
            VertexData[vertOffset + 8] = BitConverter.Int32BitsToSingle(index);
            if (HasBones) {
                var vertWeight = meshWeights[vert];

                VertexData[vertOffset + boneIndsOffset + 0] = BitConverter.Int32BitsToSingle(vertWeight.boneIndices[0]);
                VertexData[vertOffset + boneIndsOffset + 1] = BitConverter.Int32BitsToSingle(vertWeight.boneIndices[1]);
                VertexData[vertOffset + boneIndsOffset + 2] = BitConverter.Int32BitsToSingle(vertWeight.boneIndices[2]);
                VertexData[vertOffset + boneIndsOffset + 3] = BitConverter.Int32BitsToSingle(vertWeight.boneIndices[3]);

                VertexData[vertOffset + boneWeightsOffset + 0] = vertWeight.boneWeights[0];
                VertexData[vertOffset + boneWeightsOffset + 1] = vertWeight.boneWeights[1];
                VertexData[vertOffset + boneWeightsOffset + 2] = vertWeight.boneWeights[2];
                VertexData[vertOffset + boneWeightsOffset + 3] = vertWeight.boneWeights[3];
                if (vertWeight.boneWeights[4] > 0) {
                    // normalize weights - if the mesh has more than 4 weights per bone, ignore any extra bones
                    // this simplifies the shader code and is usually visually neglible
                    // for fully accurate animations, blender or ingame exists
                    ref var weights = ref MemoryMarshal.Cast<float, Vector4D<float>>(VertexData.AsSpan(vertOffset + boneWeightsOffset, 4))[0];
                    weights /= (weights.X + weights.Y + weights.Z + weights.W);
                }
            }
        }

        BoundingBox = sourceMesh.MeshData?.boundingBox ?? default;
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