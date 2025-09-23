using System.Numerics;
using System.Runtime.InteropServices;
using Assimp;
using ReeLib;
using ReeLib.Common;
using ReeLib.via;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace ContentEditor.App.Graphics;

public class TriangleMesh : Mesh
{
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
        if (sourceMesh.MeshBuffer == null)
        {
            return;
        }

        Flags = (sourceMesh.MeshBuffer.Tangents.Length != 0 ? MeshFlags.HasTangents : MeshFlags.None)
            | (sourceMesh.BoneData?.Bones.Count > 0 ? MeshFlags.HasBones : MeshFlags.None);

        var attrs = AttributeCount;
        Indices = new int[submesh.Indices.Length];
        VertexData = new float[submesh.Indices.Length * attrs];
        var hasNormals = sourceMesh.MeshBuffer.Normals.Length > 0;
        var tangentsOffset = TangentAttributeOffset;
        var boneIndsOffset = BonesAttributeOffset;
        var boneWeightsOffset = BonesAttributeOffset + 4;

        int index = 0;
        foreach (var vert in submesh.Indices) {
            Indices[index] = vert;
            var pos = submesh.Positions[vert];
            var uv = submesh.UV0[vert];
            var norm = hasNormals ? submesh.Normals[vert] : Vector3.UnitX;
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
            if (HasTangents) {
                var tan = submesh.Tangents[vert];
                VertexData[vertOffset + tangentsOffset + 0] = tan.X;
                VertexData[vertOffset + tangentsOffset + 1] = tan.Y;
                VertexData[vertOffset + tangentsOffset + 2] = tan.Z;
            }
            if (HasBones) {
                var vertWeight = submesh.Weights[vert];

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
            index++;
        }

        BoundingBox = sourceMesh.Meshes[0].boundingBox;
        UpdateBuffers();
    }

    public TriangleMesh(GL gl, Assimp.Mesh sourceMesh) : base(gl)
    {
        Flags = (sourceMesh.HasTangentBasis ? MeshFlags.HasTangents : MeshFlags.None);
        // note: ignoring bones and animations for Assimp meshes - the bone weight structure is horrifyingly annoying

        Indices = sourceMesh.GetIndices().ToArray();

        var attrs = AttributeCount;
        VertexData = new float[Indices.Length * attrs];
        var uv0 = sourceMesh.TextureCoordinateChannels[0];
        var hasNormals = sourceMesh.HasNormals;
        var tangentsOffset = TangentAttributeOffset;
        for (int index = 0; index < Indices.Length; ++index) {
            var vert = (int)Indices[index];
            VertexData[index * attrs + 0] = sourceMesh.Vertices[vert].X;
            VertexData[index * attrs + 1] = sourceMesh.Vertices[vert].Y;
            VertexData[index * attrs + 2] = sourceMesh.Vertices[vert].Z;
            VertexData[index * attrs + 3] = uv0[vert].X;
            VertexData[index * attrs + 4] = uv0[vert].Y;
            VertexData[index * attrs + 5] = !hasNormals ? 0 : sourceMesh.Normals[vert].X;
            VertexData[index * attrs + 6] = !hasNormals ? 0 : sourceMesh.Normals[vert].Y;
            VertexData[index * attrs + 7] = !hasNormals ? 1 : sourceMesh.Normals[vert].Z;
            VertexData[index * attrs + 8] = BitConverter.Int32BitsToSingle(index);
            if (HasTangents) {
                VertexData[index * attrs + tangentsOffset + 0] = sourceMesh.Tangents[vert].X;
                VertexData[index * attrs + tangentsOffset + 1] = sourceMesh.Tangents[vert].Y;
                VertexData[index * attrs + tangentsOffset + 2] = sourceMesh.Tangents[vert].Z;
            }
        }

        BoundingBox = new AABB(sourceMesh.BoundingBox.Min, sourceMesh.BoundingBox.Max);
        UpdateBuffers();
    }

    public override string ToString() => $"{VAO} {VBO} indices: {Indices.Length}";
}