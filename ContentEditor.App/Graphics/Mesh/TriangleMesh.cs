using System.Numerics;
using System.Runtime.InteropServices;
using ReeLib;
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

    public TriangleMesh(GL gl, float[] vertexData, int[] indices, AABB bounds, bool includeTangents = false) : this(gl, vertexData, indices)
    {
        BoundingBox = bounds;
        Flags = (includeTangents ? MeshFlags.HasTangents : MeshFlags.None);
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
        Flags = (hasTan ? MeshFlags.HasTangents : MeshFlags.None)
            | (hasWeights ? MeshFlags.HasBones : MeshFlags.None);

        var meshVerts = submesh.Positions;
        var meshUV = submesh.UV0;
        var meshNormals = hasNormals ? submesh.Normals : default;
        var meshTangents = hasTan ? submesh.Tangents : default;
        var meshWeights = hasWeights ? submesh.Weights : default;

        var indicesShort = !integerIndices ? submesh.Indices : default;
        var indicesInt = integerIndices ? submesh.IntegerIndices : default;

        var attrs = AttributeCount;
        Indices = new int[submesh.indicesCount];
        VertexData = new float[submesh.indicesCount * attrs];
        var tangentsOffset = TangentAttributeOffset;
        var boneIndsOffset = BonesAttributeOffset;
        var boneWeightsOffset = BonesAttributeOffset + 4;

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
            if (hasTan) {
                var tan = meshTangents[vert];
                VertexData[vertOffset + tangentsOffset + 0] = tan.X;
                VertexData[vertOffset + tangentsOffset + 1] = tan.Y;
                VertexData[vertOffset + tangentsOffset + 2] = tan.Z;
            }
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